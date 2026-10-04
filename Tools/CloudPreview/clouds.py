"""Storm tower preview: get_density() from VolumetricCompute.compute ported to numpy (old and new), lit with a short
light march towards the sun. The base noise is the real perlin-worley texture, only its low octaves (64^3)."""
import os, sys
import numpy as np
from PIL import Image
import common as C
import render as R

NOISE_SIZE, SQUISH = 53215.0, 1.0
BASE, HEIGHT = 2000.0, 4599.0
WIND_DIR = np.array([-1.0, 0.0])
WIND_SHEAR = 1000.0


# ---------------------------------------------------------------- PerlinTex (NoiseGen.compute GenPerlin), low octaves
def worley(pos, size):
    p = pos * size
    g = np.floor(p)
    best = np.full(p.shape[:-1], 1e9, np.float32)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cp = g + np.array([dx, dy, dz], np.float32)
                wrapped = np.mod(cp + size, size)
                pt = cp + (C.hash33(wrapped.astype(np.int64).astype(np.uint32)) + 1.0) / 2.0
                best = np.minimum(best, np.linalg.norm(p - pt, axis=-1))
    return best


def worley_fbm(pos, freq, gain, lac, octaves, radius, limit):
    result, amp = 1.0, 1.0
    for _ in range(octaves):
        if freq / radius > limit:
            break
        size = int(int(round(freq)) / radius)
        result = result * (1 - worley(pos, size) * amp)
        amp *= gain
        freq *= lac
    return np.clip(result, 0, 1)


def perlin_fbm(pos, freq, gain, lac, octaves, limit):
    result, total, amp = 0.0, 0.0, 1.0
    for _ in range(octaves):
        if freq > limit:
            break
        result = result + (C.perlin(pos, int(round(freq))) + 0.75) / 1.5 * amp
        total += amp
        amp *= gain
        freq *= lac
    return result / total


def perlin_tex(n=64):
    path = os.path.join(C.HERE, f'perlin{n}.npy')
    if os.path.exists(path):
        return np.load(path)
    lim = n / 2
    g = (np.arange(n) + 0.5) / n
    uvw = np.stack(np.meshgrid(g, g, g, indexing='ij'), -1).astype(np.float32)  # [x, y, z]
    pw_perlin = perlin_fbm(uvw, 8, 0.5, 1.94, 7, lim)
    pw_worley = worley_fbm(uvw, 8, 0.5, 1.94, 7, 0.67, lim)
    w1 = worley_fbm(uvw, 8, 0.5, 1.934, 7, 1.0, lim)
    w2 = worley_fbm(uvw, 16, 0.5, 1.934, 7, 1.0, lim)
    w3 = worley_fbm(uvw, 32, 0.5, 1.934, 7, 1.0, lim)
    pw = pw_worley + pw_perlin * (1.0 - pw_worley)
    tex = np.stack([w1, w2, w3, pw], -1).astype(np.float32)
    np.save(path, tex)
    return tex


PERLIN = perlin_tex()


def sample3d(tex, uvw):
    n = tex.shape[0]
    p = uvw * n - 0.5
    i0 = np.floor(p).astype(np.int64)
    f = (p - i0).astype(np.float32)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * (f[..., 2] if dz else 1 - f[..., 2])
                out = out + tex[(i0[..., 0] + dx) % n, (i0[..., 1] + dy) % n, (i0[..., 2] + dz) % n] * w[..., None]
    return out


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def cumulus_lut(hf):
    # The scene's cumulus shape curve: 1 at the base to 0 at the top, flat tangents. Clamped like the LUT.
    t = np.clip(hf, 0, 1)
    return 1 - t * t * (3 - 2 * t)


def remap(v, a, b, c, d):
    return c + (v - a) / (b - a) * (d - c)


NEWP = dict(warp=2500.0, warp_size=24000.0, core_lo=0.35, core_hi=0.8, tower=1.4, col_lo=0.5, cov_boost=0.15, dens=1.5,
            lump_size=12000.0, lump_amt=0.7, anvil_shift=5000.0, anvil_lo=0.0, anvil_hi=0.3, anvil_bottom=0.78,
            anvil_top=0.95, anvil_amt=0.85, widen=0.3, wedge=0.6, anvil_cov=0.1, billow=0.9, billow_size=9000.0,
            dome_w=0.35, col_mix=0.5, rag=0.3, anvil_thin=0.06, a_thin=0.88, a_thick=0.16, a_cov=0.3, cov_dome=0.0, col_mix2=0.85, reach=2.5, stretch=1.5, shift=0.3, tower_min=0.9, tower_grow=0.8, col_top=0.6)
STORM_BIG, BIG_CENTERS, BIG_VALUES = C.storm_map(0.3, 0.15)
BIG_RADII = C.LAST_RADII
BIG_EXTRAS = C.LAST_EXTRAS


def anvil_map(P, size=256, **_):
    """Port of the AnvilMap kernel. x: anvil, y: where the towers stand (dome), z: their height, w: the anvil's height."""
    cells = 12
    py, px = np.mgrid[0:size, 0:size]
    p = np.stack([(px + 0.5) / size * cells, (py + 0.5) / size * cells], -1)
    g = np.floor(p).astype(int)
    out = np.zeros((size, size, 4), np.float32)
    along = WIND_DIR / np.linalg.norm(WIND_DIR)
    across = np.array([-along[1], along[0]])
    share = P['cells']
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            cx = g[..., 0] + dx
            cy = g[..., 1] + dy
            ix, iy = (cx + 24) % cells, (cy + 24) % cells
            centre = BIG_CENTERS[ix, iy] + np.stack([cx - ix, cy - iy], -1)
            value = BIG_VALUES[ix, iy]
            extra = BIG_EXTRAS[ix, iy]
            act = np.clip((share - value) * 8.0, 0, 1) * P['towers']
            maturity = np.clip((share - value) / max(share, 1e-3), 0, 1)
            height = act * (0.3 + 0.7 * smoothstep(0.0, 0.8, maturity)) * (0.75 + 0.25 * extra[..., 0])
            radius = BIG_RADII[ix, iy] * (1.2 + maturity)
            angle = extra[..., 1] * 6.2831853
            flank = np.stack([np.cos(angle), np.sin(angle)], -1)
            flanked = extra[..., 2] < 0.7
            for t, (off, rs, hs) in enumerate([(0.0, 1.0, 1.0), (0.75, 0.6, 0.75), (1.35, 0.42, 0.5)]):
                o = centre + flank * (radius * off)[..., None]
                f = np.clip(1 - np.linalg.norm(p - o, axis=-1) / (radius * rs), 0, 1) * act
                if t > 0:
                    f = np.where(flanked, f, 0)
                d = np.sqrt(np.clip(f / 0.5, 0, 1))
                out[..., 1] = np.maximum(out[..., 1], d)
                out[..., 2] = np.maximum(out[..., 2], d * height * hs)
            r = BIG_RADII[ix, iy] * 2.5
            q = p - (centre + along * (r * 0.3)[..., None])
            dd = np.sqrt((q @ along / (r * 1.5)) ** 2 + (q @ across / r) ** 2)
            spread = np.clip(1 - dd, 0, 1) * act * smoothstep(0.15, 0.4, maturity)
            win = spread > out[..., 0]
            out[..., 3] = np.where(win, height, out[..., 3])
            out[..., 0] = np.maximum(out[..., 0], spread)
    return out


ANVIL = None
MATURE = 0.6


def storm_cell(xz, N):
    """Storm map lookup with the outlines pushed around, so the cells aren't circles."""
    uv = xz / N['warp_size']
    warp = np.stack([C.sample_wrap(R.PATCHES, uv), C.sample_wrap(R.WISPS, uv)], -1) - 0.5
    return C.sample_wrap(STORM_BIG, (xz + warp * 2 * N['warp']) / C.TILE)


def density(p, P, version, N=NEWP):
    xz = p[..., [0, 2]]
    y = p[..., 1]
    w = C.sample_wrap(R.WEATHER, xz / C.TILE)
    if version == 'old':
        cell = C.sample_wrap(R.STORM, xz / C.TILE)
        active = np.clip((P['cells'] - cell[..., 1]) * 8.0, 0, 1)
        storm = P['towers'] * cell[..., 0] * active
        tower = 1 + 1.4 * storm
        cov = np.clip(P['coverage'] + 0.2 * storm, 0, 1)
        hf = (y - BASE) / (HEIGHT * tower)
        shape = cumulus_lut(hf)
        shape = np.maximum(shape, storm * 0.7 * smoothstep(0.7, 0.82, hf) * (1 - smoothstep(0.9, 1.0, hf)))
        core = storm
        dens_boost = 0.5 * storm
    else:
        # Mirrors get_density() in VolumetricCompute.compute line by line.
        uvw = xz + (np.stack([C.sample_wrap(R.PATCHES, xz / 24000.0), C.sample_wrap(R.WISPS, xz / 24000.0)], -1) - 0.5) * 5000.0
        shapes = C.sample_wrap(ANVIL, uvw / C.TILE)
        dome = shapes[..., 1]
        tower = 1 + 1.4 * shapes[..., 2]
        cov = np.clip(P['coverage'] + 0.2 * dome, 0, 1)
        hf = (y - BASE) / (HEIGHT * tower)
        shape = cumulus_lut(hf)
        column = 1 - smoothstep(N['col_top'], 1.0, hf)
        shape = shape + (column - shape) * (dome * N['col_mix2'])
        af = (y - BASE) / (HEIGHT * (1 + 1.4 * shapes[..., 3]))
        near_anvil = (af > 0.65) & (af < 0.93) & (shapes[..., 0] > 0)
        billow = sample3d(PERLIN, np.stack([xz[..., 0] / 12000.0, y / 12000.0, xz[..., 1] / 12000.0], -1))[..., 3]
        spread = shapes[..., 0]
        # The anvil: a wide flat plate under the tops of the tallest towers, thin out to its edges, thicker over the
        # towers, with a ragged underside.
        bottom = N['a_thin'] - N['a_thick'] * smoothstep(0.3, 1.0, spread) + 0.2 * (0.68 - billow)
        anvil = smoothstep(0.0, 0.15, spread) * 0.9 * smoothstep(bottom, bottom + 0.04, af) * (1 - smoothstep(0.915, 0.93, af))
        anvil = np.where(near_anvil, anvil, 0)
        shape = np.maximum(shape, anvil)
        cov = np.maximum(cov, np.clip(P['coverage'] + N['a_cov'] * anvil, 0, 1))
        shape = shape * (1 - dome * 0.35 * (1 - smoothstep(0.58, 0.78, billow)))
        dens_boost = 1.5 * dome
    ok = (hf >= 0) & ((1 - cov) + w < 1.0)
    q = xz - WIND_DIR * (np.clip(hf, 0, 1) * WIND_SHEAR)[..., None]
    uvw = np.stack([q[..., 0] / NOISE_SIZE, y * SQUISH / NOISE_SIZE, q[..., 1] / NOISE_SIZE], -1)
    n = sample3d(PERLIN, uvw)
    detail = n[..., 0] * 0.625 + n[..., 1] * 0.25 + n[..., 2] * 0.125
    base = remap(n[..., 3], (1 - detail) * 0.37, 1, 0, 1)
    base = base * shape
    base = np.clip(remap(base, np.clip((1 - cov) + w, 0, 1), 1, 0, 1), 0, 1)
    scale = 0.02 * P['density'] * 0.79 * (1 + dens_boost)
    result = remap(base, 0.5 * 0.44, 1, 0, 1)
    return np.where(ok, np.maximum(result, 0) * scale, 0)


def active_cell_big(P, k):
    _, centers, values = C.storm_map(0.3, 0.15)
    idx = np.argwhere(values < P['cells'] * 0.5)
    x, y = idx[k]
    return centers[x, y] / 12 * C.TILE


SUN = R.SUN


def march(cam, ray, P, version, top=14500.0, step=200.0, far=70000.0):
    rays = ray.reshape(-1, 3)
    n = rays.shape[0]
    ry = rays[:, 1]
    with np.errstate(divide='ignore', invalid='ignore'):
        ta = (BASE - cam[1]) / ry
        tb = (top - cam[1]) / ry
    t0 = np.where(np.abs(ry) < 1e-6, 0, np.maximum(np.minimum(ta, tb), 0))
    t1 = np.where(np.abs(ry) < 1e-6, far, np.minimum(np.maximum(ta, tb), far))
    if BASE <= cam[1] <= top:
        t0 = np.zeros(n)
    t = t0 + step * 0.5
    T = np.ones(n)
    col = np.zeros((n, 3))
    ambient, light, _ = R.colors(P)
    hz = 80000 * P['visibility']
    haze_col = R.sky(ray, P).reshape(-1, 3)
    while True:
        idx = np.nonzero((t < t1) & (T > 0.01))[0]
        if idx.size == 0:
            break
        p = cam + rays[idx] * t[idx, None]
        d = density(p, P, version)
        hit = d > 0
        if hit.any():
            h = idx[hit]
            ph = p[hit]
            od = 0
            for s in (150.0, 500.0, 1200.0, 2600.0):
                od = od + density(ph + SUN * s, P, version) * (s * 0.6)
            hfrac = np.clip((ph[:, 1] - BASE) / 11000.0, 0, 1)
            lit = light * 0.12 * np.exp(-od)[:, None] + ambient * (0.45 + 0.55 * hfrac)[:, None]
            op = 1 - np.exp(-d[hit] * step)
            haze = (1 - np.exp(-t[h] / hz))[:, None]
            col[h] += (T[h] * op)[:, None] * (lit * (1 - haze) + haze_col[h] * haze)
            T[h] *= 1 - op
        t[idx] += step
    return col.reshape(ray.shape), T.reshape(ray.shape[:2])


def view(P, version, cam, yaw, pitch, fov, W=300, H=200):
    ray = R.camera(W, H, fov, yaw, pitch)
    with np.errstate(divide='ignore', invalid='ignore'):
        tg = np.where(ray[..., 1] < 0, -cam[1] / ray[..., 1], np.inf)
    hz = 80000 * P['visibility']
    ground = np.array([0.05, 0.06, 0.04])
    gh = (1 - np.exp(-np.where(np.isfinite(tg), tg, 1e9) / hz))[..., None]
    bg = np.where(np.isfinite(tg)[..., None], ground * (1 - gh) + R.sky(ray, P) * gh, R.sky(ray, P))
    c, T = march(cam, ray, P, version)
    return C.to_srgb(c + T[..., None] * bg)


def views(P, k=5):
    c = active_cell_big(P, k)
    out = []
    for name, off, h, pitch, fov in [('ground, 70 km away', (-70000.0, 0), 2.0, 7.0, 40),
                                     ('above, 9 km up, 45 km away', (0, -45000.0), 9000.0, 2.0, 40),
                                     ('high above, 30 km up', (0, -60000.0), 30000.0, -22.0, 45)]:
        cam = np.array([c[0] + off[0], h, c[1] + off[1]])
        to = c - cam[[0, 2]]
        out.append((name, cam, np.degrees(np.arctan2(to[0], to[1])), pitch, fov))
    return out


if __name__ == '__main__':
    versions = sys.argv[1].split(',')
    tag = sys.argv[2]
    cells = [int(x) for x in sys.argv[3].split(',')] if len(sys.argv) > 3 else [5]
    for kv in sys.argv[4:]:
        k, v = kv.split('=')
        NEWP[k] = float(v)
    P = dict(R.PRESETS['storm'], density=1.5, visibility=0.6, coverage=float(os.environ.get('COV', 0.88)))
    globals()["ANVIL"] = anvil_map(P, reach=NEWP['reach'], stretch=NEWP['stretch'], shift=NEWP['shift'],
                                   tower_min=NEWP['tower_min'], tower_grow=NEWP['tower_grow'])
    am = globals()["ANVIL"]
    Image.fromarray((np.concatenate([am[..., 0], am[..., 1], am[..., 2]], 1) * 255).astype(np.uint8)).save(tag + '_anvilmap.png')
    rows = []
    for k in cells:
        for name, cam, yaw, pitch, fov in views(P, k):
            rows.append(np.concatenate([R.label(view(P, v, cam, yaw, pitch, fov), f'cell {k} | {name} | {v}') for v in versions], 1))
    Image.fromarray(np.concatenate(rows, 0)).save(tag + '.png')
    print('saved', tag + '.png')
