"""Rain curtain preview: the old and the new rain_curtains() from VolumetricCompute.compute, ported to numpy, over a
simplified flat cloud layer, sky and ground. Run: python3 render.py [old|new|both]"""
import sys
import numpy as np
from PIL import Image, ImageDraw
import common as C

WEATHER_RA = C.weather_map()
WEATHER = WEATHER_RA[..., 0]
WISPS = WEATHER_RA[..., 1]
PATCHES = WEATHER_RA[..., 2]
STORM, CENTERS, VALUES = C.storm_map()
BASE = 2000.0
WIND = np.array([-1.0, 0.0])
SUN = np.array([-0.45, np.sin(np.radians(35)), 0.55])
SUN = SUN / np.linalg.norm(SUN)

PRESETS = {
    # coverage, towers, storm cells, rain, darkness, visibility (WeatherSystem.cs)
    'storm': dict(coverage=0.88, towers=1.0, cells=0.55, rain=1.0, darkness=0.75, visibility=0.30),
    'rain': dict(coverage=0.95, towers=0.40, cells=0.30, rain=0.70, darkness=0.60, visibility=0.35),
}


def colors(P):
    ambient = np.array([0.345, 0.38, 0.443]) * (1 - 0.4 * P['darkness'])
    light = 2.608 * 3.0 * np.array([1.0, 0.88, 0.73]) * (1 - 0.6 * P['darkness'])
    exposure = 1.3 * (1 - 0.6 * P['darkness'])
    return ambient, light, exposure


def sky(ray, P):
    return C.procedural_sky(ray, SUN, colors(P)[2])


def sample_maps(xz):
    uv = xz / C.TILE
    return C.sample_wrap(WEATHER, uv), C.sample_wrap(STORM, uv)


def precipitation(weather_r, cell, P):
    """cloud_precipitation() from CloudRain.hlsl (unchanged, the streaks keep using it)."""
    dense = np.clip((P['coverage'] - weather_r - 0.2) * 3.0, 0, 1)
    c = cell[..., 0] * np.clip((P['cells'] - cell[..., 1]) * 8.0, 0, 1)
    return P['rain'] * np.maximum(dense * (1 - 0.8 * P['towers']), c)


def segment(cam, ray, view_len, far=60000.0):
    ry = ray[..., 1]
    with np.errstate(divide='ignore', invalid='ignore'):
        tg = -cam[1] / ry
        tb = (BASE - cam[1]) / ry
    flat = np.abs(ry) < 1e-5
    t0 = np.where(flat, 0.0, np.maximum(np.minimum(tg, tb), 0.0))
    t1 = np.where(flat, far if 0 < cam[1] < BASE else 0.0, np.maximum(tg, tb))
    t1 = np.minimum(t1, np.minimum(view_len, far))
    return t0, t1


def curtains_old(cam, ray, view_len, jitter, P):
    ambient, light, _ = colors(P)
    hz = 80000 * P['visibility']
    t0, t1 = segment(cam, ray, view_len)
    rain_color = ambient * 0.7 + light * 0.02
    haze_color = sky(ray, P)
    dt = np.maximum(t1 - t0, 0) / 6.0
    col = np.zeros(ray.shape)
    T = np.ones(ray.shape[:2])
    for k in range(6):
        t = t0 + (k + jitter) * dt
        xz = cam[[0, 2]] + ray[..., [0, 2]] * t[..., None]
        w, s = sample_maps(xz)
        p = precipitation(w, s, P)
        op = 1 - np.exp(-p / 1500.0 * dt)
        haze = 1 - np.exp(-t / hz)
        col += (T * op)[..., None] * (rain_color * (1 - haze[..., None]) + haze_color * haze[..., None])
        T *= 1 - op
    return col, 1 - T


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# Tunables for the new version, overridden from the command line while experimenting.
NEW = dict(start=2000.0, start_full=5000.0, visibility=1500.0, lean=0.35, core_lo=0.25, core_hi=0.75, veil=0.3,
           strand_size=6000.0, patch_size=24000.0, patch_lo=0.45, patch_hi=0.75, strand_lo=0.3, strand_mul=1.4, far_fade=45000.0, big=1200.0, big_grow=0.06, small=250.0, small_grow=0.02, cap=64)
WISP_MIPS = C.mip_chain(WISPS)
# The game's storm cells (bigger, bent outlines). False gives the cells from before the cumulonimbus work.
BIG_CELLS = True
STORM_BIG = C.storm_map(0.3, 0.15)[0]


def rain_density(p3, P, N, step):
    """Port of rain_density() in VolumetricCompute.compute. Returns the density and whether p is under an active cell."""
    xz = p3[..., [0, 2]] + WIND * ((BASE - p3[..., 1]) * N['lean'])[..., None]
    w = C.sample_wrap(WEATHER, xz / C.TILE)
    if BIG_CELLS:
        warp = np.stack([C.sample_wrap(PATCHES, xz / 24000.0), C.sample_wrap(WISPS, xz / 24000.0)], -1) - 0.5
        s = C.sample_wrap(STORM_BIG, (xz + warp * 5000.0) / C.TILE)
    else:
        s = C.sample_wrap(STORM, xz / C.TILE)
    active = np.clip((P['cells'] - s[..., 1]) * 8.0, 0, 1)
    in_cell = s[..., 0] * active > 0.001
    shaft = smoothstep(N['core_lo'], N['core_hi'], s[..., 0]) * active
    veil = np.clip((P['coverage'] - w - 0.2) * 3.0, 0, 1) * (1 - 0.8 * P['towers']) * N['veil']
    # The light rain between the cells comes in separate patches, else it adds up to a grey wall at the horizon.
    patches = C.sample_wrap(WISPS, xz / N['patch_size'] + 0.5)
    veil = veil * smoothstep(N['patch_lo'], N['patch_hi'], patches)
    lod = np.log2(np.maximum(step * 512 / N['strand_size'], 1.0))
    strands = C.sample_trilinear(WISP_MIPS, xz / N['strand_size'], lod)
    return P['rain'] * np.maximum(shaft, veil) * (N['strand_lo'] + N['strand_mul'] * strands), in_cell


def curtains_new(cam, ray, view_len, jitter, P, N=NEW, stats=None):
    """Port of the new rain_curtains(): big steps through dry air, small steps under the storm cells."""
    ambient, light, _ = colors(P)
    hz = 80000 * P['visibility']
    t0, t1 = segment(cam, ray, view_len)
    rain_color = ambient * 0.7 + light * 0.02
    haze_full = sky(ray, P).reshape(-1, 3)
    below = np.clip((BASE - cam[1]) / 300.0, 0, 1)
    shape = ray.shape[:2]
    rays = ray.reshape(-1, 3)
    horiz = np.maximum(np.linalg.norm(rays[:, [0, 2]], axis=-1), 1e-4)
    t0, t1, jit = t0.ravel(), t1.ravel(), jitter.ravel()
    if below >= 1.0:
        t0 = np.maximum(t0, 2000.0 / horiz)
    n = rays.shape[0]
    col = np.zeros((n, 3))
    T = np.ones(n)
    big = lambda t: np.maximum(N['big'], t * N['big_grow'])
    small = lambda t: np.maximum(N['small'], t * N['small_grow'])
    prev = t0.copy()
    fine = np.zeros(n, bool)
    dry = np.zeros(n, int)
    t = t0 + jit * big(t0)
    iters = np.zeros(n, int)
    for i in range(int(N['cap'])):
        idx = np.nonzero((prev < t1) & (T > 0.01))[0]
        if idx.size == 0:
            break
        iters[idx] += 1
        pv, tt, fn, j, e = prev[idx], t[idx], fine[idx], jit[idx], t1[idx]
        step = np.where(fn, small(pv), big(pv))
        ts = np.minimum(tt, e)
        p3 = cam + rays[idx] * ts[:, None]
        dens, in_cell = rain_density(p3, P, N, step)
        back = ~fn & in_cell
        fn = fn | back
        take = ~back
        seg_end = np.where(fn, np.minimum(pv + small(pv), e), ts)
        dt = np.maximum(seg_end - pv, 0)
        fade = (1 - below * (1 - smoothstep(N['start'], N['start_full'], horiz[idx] * ts))) * \
               (1 - smoothstep(N['far_fade'], 60000.0, ts))
        op = np.where(take, (1 - np.exp(-dens / N['visibility'] * dt)) * fade, 0)
        haze = (1 - np.exp(-ts / hz))[:, None]
        col[idx] += (T[idx] * op)[:, None] * (rain_color * (1 - haze) + haze_full[idx] * haze)
        T[idx] *= 1 - op
        d = np.where(take & fn, np.where(in_cell, 0, dry[idx] + 1), dry[idx])
        leave = take & fn & (d >= 2)
        pv_new = np.where(take, seg_end, pv)
        fn = fn & ~leave
        d = np.where(leave, 0, d)
        t[idx] = np.where(back, pv + j * small(pv), np.where(fn, pv_new + j * small(pv_new), pv_new + big(pv_new)))
        prev[idx], fine[idx], dry[idx] = pv_new, fn, d
    if stats is not None:
        stats.append(iters.reshape(shape))
    return col.reshape(ray.shape), (1 - T).reshape(shape)


def camera(W, H, fov, yaw, pitch):
    tan = np.tan(np.radians(fov) / 2)
    x, y = np.meshgrid(((np.arange(W) + 0.5) / W * 2 - 1) * tan * W / H, (1 - (np.arange(H) + 0.5) / H * 2) * tan)
    d = np.stack([x, y, np.ones_like(x)], -1)
    cp, sp = np.cos(np.radians(pitch)), np.sin(np.radians(pitch))
    d = np.stack([d[..., 0], d[..., 1] * cp + d[..., 2] * sp, -d[..., 1] * sp + d[..., 2] * cp], -1)
    cy, sy = np.cos(np.radians(yaw)), np.sin(np.radians(yaw))
    d = np.stack([d[..., 0] * cy + d[..., 2] * sy, d[..., 1], -d[..., 0] * sy + d[..., 2] * cy], -1)
    return d / np.linalg.norm(d, axis=-1, keepdims=True)


def cloud_layer(cam, ray, P):
    """Flat stand-in for the cloud layer: the base at 2 km seen from below, a top at 3.5 km seen from above."""
    ambient, light, _ = colors(P)
    hz = 80000 * P['visibility']
    above = cam[1] > BASE
    plane = 3500.0 if above else BASE
    with np.errstate(divide='ignore', invalid='ignore'):
        t = (plane - cam[1]) / ray[..., 1]
    hit = (t > 0) & (t < 200000)
    t = np.where(hit, t, 0)
    xz = cam[[0, 2]] + ray[..., [0, 2]] * t[..., None]
    w, s = sample_maps(xz)
    storm = P['towers'] * s[..., 0] * np.clip((P['cells'] - s[..., 1]) * 8, 0, 1)
    cov = np.clip(P['coverage'] + 0.2 * storm, 0, 1)
    # Stand-in for the 3D base noise, which is what opens gaps in the real cloud layer. Storm cells stay closed.
    breakup = C.sample_wrap(WEATHER, xz / 9000.0 + 0.37)
    gaps = np.maximum(np.clip((cov - 0.55 - breakup) * 5.0, 0, 1), np.clip(storm * 3, 0, 1))
    a = np.clip((cov - w) * 6.0, 0, 1) * gaps * hit
    if above:
        c = light * 0.22 + ambient
    else:
        c = (ambient * 0.55 + light * 0.012)[None, None, :] * (1 - 0.5 * storm[..., None])
    haze = (1 - np.exp(-t / hz))[..., None]
    c = c * (1 - haze) + sky(ray, P) * haze
    return c * a[..., None], a


def render(cam, yaw, pitch, P, version, frames=1, W=480, H=270, fov=65):
    ray = camera(W, H, fov, yaw, pitch)
    with np.errstate(divide='ignore', invalid='ignore'):
        tg = np.where(ray[..., 1] < 0, -cam[1] / ray[..., 1], np.inf)
    hz = 80000 * P['visibility']
    ground = np.array([0.05, 0.06, 0.04])
    gh = (1 - np.exp(-np.where(np.isfinite(tg), tg, 1e9) / hz))[..., None]
    bg = np.where(np.isfinite(tg)[..., None], ground * (1 - gh) + sky(ray, P) * gh, sky(ray, P))
    cc, ca = cloud_layer(cam, ray, P)
    view_len = np.where(np.isfinite(tg), tg, 1e6)
    px, py = np.meshgrid(np.arange(W), np.arange(H))
    out = 0
    for f in range(frames):
        jitter = C.ign(px + (f & 63) * 5.588238, py + (f & 63) * 5.588238)
        if version == 'old':
            rc, ra = curtains_old(cam, ray, view_len, jitter, P)
        elif version == 'new':
            rc, ra = curtains_new(cam, ray, view_len, jitter, P)
        else:
            rc, ra = np.zeros(ray.shape), np.zeros(ray.shape[:2])
        if cam[1] < BASE:
            img = rc + (1 - ra)[..., None] * (cc + (1 - ca)[..., None] * bg)
        else:
            img = cc + (1 - ca)[..., None] * (rc + (1 - ra)[..., None] * bg)
        out = out + img
    return C.to_srgb(out / frames)


def active_cell(P, k=0):
    """World xz of the k-th active storm cell (in the tile copy at the origin)."""
    idx = np.argwhere(VALUES < P['cells'])
    x, y = idx[k]
    return CENTERS[x, y] / 12 * C.TILE


def label(img, text):
    im = Image.fromarray((img * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, 8 + 6 * len(text), 14], fill=(0, 0, 0))
    d.text((4, 2), text, fill=(255, 255, 255))
    return np.asarray(im)


def views(P):
    c = active_cell(P, 5)
    # Looking at the cell from the ground (near and far), from inside it and from above.
    out = []
    for name, off, h, pitch, fov in [('ground 9 km away', np.array([0, -9000.0]), 2.0, 7.0, 45),
                                     ('ground 25 km away', np.array([0, -25000.0]), 2.0, 3.0, 35),
                                     ('ground in the cell', np.array([600, 0.0]), 2.0, 8.0, 65),
                                     ('above, 6 km up', np.array([0, -26000.0]), 6000.0, -12.0, 65)]:
        cam = np.array([c[0] + off[0], h, c[1] + off[1]])
        to = c - cam[[0, 2]]
        yaw = np.degrees(np.arctan2(to[0], to[1])) if np.linalg.norm(to) > 1 else 30.0
        out.append((name, cam, yaw, pitch, fov))
    return out


if __name__ == '__main__':
    versions = sys.argv[1].split(',') if len(sys.argv) > 1 else ['old', 'new']
    frames = int(sys.argv[2]) if len(sys.argv) > 2 else 8
    tag = sys.argv[3] if len(sys.argv) > 3 else 'cmp'
    for k, v in (a.split('=') for a in sys.argv[4:]):
        NEW[k] = type(NEW[k])(float(v))
    rows = []
    for wname, P in PRESETS.items():
        for vname, cam, yaw, pitch, fov in views(P):
            row = [label(render(cam, yaw, pitch, P, ver, frames, fov=fov), f'{wname} | {vname} | {ver}') for ver in versions]
            rows.append(np.concatenate(row, 1))
    Image.fromarray(np.concatenate(rows, 0)).save(f'{tag}.png')
    print('saved', f'{tag}.png')
