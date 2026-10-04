"""Numpy ports of the bits of the cloud system the rain preview needs (weather map, storm map, sky)."""
import os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
TILE = 128000.0


# ---------------------------------------------------------------- .NET legacy System.Random (Knuth subtractive)
class NetRandom:
    MBIG = 2147483647
    MSEED = 161803398

    def __init__(self, seed):
        sa = [0] * 56
        mj = self.MSEED - abs(seed)
        sa[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55
            sa[ii] = mk
            mk = mj - mk
            if mk < 0:
                mk += self.MBIG
            mj = sa[ii]
        for _ in range(1, 5):
            for i in range(1, 56):
                sa[i] -= sa[1 + (i + 30) % 55]
                if sa[i] < 0:
                    sa[i] += self.MBIG
        self.sa, self.inext, self.inextp = sa, 0, 21

    def next_double(self):
        a = self.inext + 1
        if a >= 56:
            a = 1
        b = self.inextp + 1
        if b >= 56:
            b = 1
        r = self.sa[a] - self.sa[b]
        if r == self.MBIG:
            r -= 1
        if r < 0:
            r += self.MBIG
        self.sa[a] = r
        self.inext, self.inextp = a, b
        return r * (1.0 / self.MBIG)


def storm_map(r_min=0.2, r_span=0.15):
    """CreateStormMap() from NoiseController.cs. Returns (256,256,2) and the cell centres/values."""
    size, cells = 256, 12
    rnd = NetRandom(1234)
    centers = np.zeros((cells, cells, 2))
    radii = np.zeros((cells, cells))
    values = np.zeros((cells, cells))
    for y in range(cells):
        for x in range(cells):
            radii[x, y] = 0.2 + 0.15 * rnd.next_double()
            room = 0.5 - radii[x, y]
            cx = x + 0.5 + room * (2 * rnd.next_double() - 1)
            cy = y + 0.5 + room * (2 * rnd.next_double() - 1)
            centers[x, y] = (cx, cy)
            values[x, y] = rnd.next_double()
    py, px = np.mgrid[0:size, 0:size]
    p = np.stack([(px + 0.5) / size * cells, (py + 0.5) / size * cells], -1)
    cx = np.minimum(p[..., 0].astype(int), cells - 1)
    cy = np.minimum(p[..., 1].astype(int), cells - 1)
    d = np.linalg.norm(p - centers[cx, cy], axis=-1) / radii[cx, cy]
    t = np.clip(1 - d, 0, 1)
    data = np.stack([t * t * (3 - 2 * t), values[cx, cy]], -1).astype(np.float32)
    global LAST_RADII, LAST_EXTRAS
    LAST_RADII = radii
    # Drawn after everything else, so the cells stay where they were (CreateStormMap does the same).
    extras = np.zeros((cells, cells, 3))
    for y in range(cells):
        for x in range(cells):
            extras[x, y] = (rnd.next_double(), rnd.next_double(), rnd.next_double())
    LAST_EXTRAS = extras
    return data, centers, values


# ---------------------------------------------------------------- NoiseGen.compute gen_weather (r channel)
def pcg3d(v):
    v = v.astype(np.uint32)
    with np.errstate(over='ignore'):
        v = v * np.uint32(1664525) + np.uint32(1013904223)
        x, y, z = v[..., 0], v[..., 1], v[..., 2]
        x = x + y * z
        y = y + z * x
        z = z + x * y
        x ^= x >> np.uint32(16)
        y ^= y >> np.uint32(16)
        z ^= z >> np.uint32(16)
        x = x + y * z
        y = y + z * x
        z = z + x * y
    return np.stack([x, y, z], -1)


def hash33(p):
    h = pcg3d(p).astype(np.float64)
    return (h * (1.0 / 4294967295.0) * 2.0 - 1.0).astype(np.float32)


def fade(x):
    return 6 * x ** 5 - 15 * x ** 4 + 10 * x ** 3


def perlin(pos, freq):
    p = pos * freq
    pmin = np.floor(p)
    f = p - pmin
    dots = {}
    for c in range(8):
        o = np.array([(c >> 2) & 1, (c >> 1) & 1, c & 1], dtype=np.float32)  # x, y, z
        corner = pmin + o
        lat = (corner.astype(np.int64) % freq).astype(np.uint32)
        h = hash33(lat)
        h = h / np.linalg.norm(h, axis=-1, keepdims=True)
        dots[c] = np.sum(h * (p - corner), -1)
    u = fade(f)
    x0 = dots[0] + u[..., 0] * (dots[4] - dots[0])
    x1 = dots[2] + u[..., 0] * (dots[6] - dots[2])
    x2 = dots[1] + u[..., 0] * (dots[5] - dots[1])
    x3 = dots[3] + u[..., 0] * (dots[7] - dots[3])
    y0 = x0 + u[..., 1] * (x1 - x0)
    y1 = x2 + u[..., 1] * (x3 - x2)
    return y0 + u[..., 2] * (y1 - y0)


def perlin_fbm(pos, frequency, gain, lacunarity, octaves):
    result, total, amp = 0.0, 0.0, 1.0
    for _ in range(octaves):
        if frequency > 128:
            break
        result = result + (perlin(pos, int(round(frequency))) + 0.75) / 1.5 * amp
        total += amp
        amp *= gain
        frequency *= lacunarity
    return result / total


def weather_map():
    path = os.path.join(HERE, 'weather3.npy')
    if os.path.exists(path):
        return np.load(path)
    n = 512
    py, px = np.mgrid[0:n, 0:n]
    uvw = np.stack([(px + 0.5) / n, (py + 0.5) / n, np.zeros_like(px, dtype=float)], -1).astype(np.float32)
    r = perlin_fbm(uvw, 2, 0.5, 1.97, 7)
    r = np.clip(-0.5 + (r - 0.31) / (0.74 - 0.31) * 1.5, 0, 1)
    # a: the cirrus wisps, an evenly spread fbm.
    uvw[..., 2] = 0.71
    a = np.clip((perlin_fbm(uvw, 4, 0.6, 2, 6) - 0.38) / (0.62 - 0.38), 0, 1)
    uvw[..., 2] = 0.37
    b = np.clip((perlin_fbm(uvw, 2, 0.5, 2, 4) - 0.42) / (0.62 - 0.42), 0, 1)
    out = np.stack([r, a, b], -1).astype(np.float32)
    np.save(path, out)
    return out


def sample_wrap(tex, uv):
    """Bilinear, repeat. tex: (H, W) or (H, W, C), uv: (..., 2) with u along x (columns)."""
    h, w = tex.shape[:2]
    x = uv[..., 0] * w - 0.5
    y = uv[..., 1] * h - 0.5
    x0 = np.floor(x)
    y0 = np.floor(y)
    fx = x - x0
    fy = y - y0
    x0 = x0.astype(np.int64) % w
    y0 = y0.astype(np.int64) % h
    x1 = (x0 + 1) % w
    y1 = (y0 + 1) % h
    if tex.ndim == 3:
        fx = fx[..., None]
        fy = fy[..., None]
    a = tex[y0, x0] * (1 - fx) + tex[y0, x1] * fx
    b = tex[y1, x0] * (1 - fx) + tex[y1, x1] * fx
    return a * (1 - fy) + b * fy


# ---------------------------------------------------------------- ProceduralSky.hlsl
def sky_scale(c):
    x = 1.0 - c
    return 0.25 * np.exp(-0.00287 + x * (0.459 + x * (3.83 + x * (-6.80 + x * 5.25))))


def procedural_sky(ray, sun_dir, exposure, thickness=1.0, tint=(0.5, 0.5, 0.5)):
    eye = np.stack([ray[..., 0], np.maximum(ray[..., 1], 0), ray[..., 2]], -1) + np.array([0, 1e-4, 0])
    eye = eye / np.linalg.norm(eye, axis=-1, keepdims=True)
    outer, inner, cam_h = 1.025, 1.0, 0.0001
    scale = 1 / (outer - 1)
    sod = scale / 0.25
    mie, sun_b, pi = 0.0010, 20.0, np.pi
    rayleigh = 0.0025 * thickness ** 2.5
    tint = np.array(tint)
    wl = (np.array([0.65, 0.57, 0.475]) - 0.15) * tint + (np.array([0.65, 0.57, 0.475]) + 0.15) * (1 - tint)
    inv_wl = 1 / wl ** 4
    sun_dir = np.array(sun_dir)
    camera = np.array([0, inner + cam_h, 0])
    ey = eye[..., 1]
    far = np.sqrt(outer * outer + ey * ey - 1) - ey
    start_offset = np.exp(sod * -cam_h) * sky_scale(ey)
    sl = far * 0.5
    scaled = sl * scale
    sray = eye * sl[..., None]
    sp = camera + sray * 0.5
    front = np.zeros(eye.shape)
    for _ in range(2):
        h = np.linalg.norm(sp, axis=-1)
        depth = np.exp(sod * (inner - h))
        la = np.sum(sun_dir * sp, -1) / h
        ca = np.sum(eye * sp, -1) / h
        scatter = start_offset + depth * (sky_scale(la) - sky_scale(ca))
        att = np.exp(-np.clip(scatter, 0, 50)[..., None] * (inv_wl * rayleigh * 4 * pi + mie * 4 * pi))
        front += att * (depth * scaled)[..., None]
        sp = sp + sray
    ec = np.sum(sun_dir * eye, -1)
    return exposure * front * (inv_wl * rayleigh * sun_b) * (0.75 + 0.75 * ec * ec)[..., None]


def ign(px, py):
    return np.mod(52.9829189 * np.mod(px * 0.06711056 + py * 0.00583715, 1.0), 1.0)


def to_srgb(c):
    c = np.clip(c, 0, None)
    c = c / (1 + c * 0.15)  # a soft shoulder, roughly what a tonemapper does to the brights
    return np.clip(np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055), 0, 1)


def mip_chain(tex):
    mips = [tex]
    while mips[-1].shape[0] > 1:
        m = mips[-1]
        mips.append(0.25 * (m[0::2, 0::2] + m[1::2, 0::2] + m[0::2, 1::2] + m[1::2, 1::2]))
    return mips


def sample_trilinear(mips, uv, lod):
    lod = np.clip(lod, 0, len(mips) - 1)
    l0 = np.floor(lod).astype(int)
    f = lod - l0
    out = np.zeros(uv.shape[:-1])
    for level in np.unique(l0):
        m = l0 == level
        a = sample_wrap(mips[level], uv[m])
        b = sample_wrap(mips[min(level + 1, len(mips) - 1)], uv[m])
        out[m] = a * (1 - f[m]) + b * f[m]
    return out
