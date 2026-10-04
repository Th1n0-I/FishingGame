"""Horizon seam from above the clouds: the cloud layer ends at the 200 km draw distance, below the horizon the sky pixels
show the skybox's ground. Before: as now. After: haze reaches 1 at the draw distance, below-horizon sky = horizon haze."""
import numpy as np
from PIL import Image
import common as C, render as R

DRAW = 200000.0
W, H = 900, 300


def skybox_below(ray, P):
    # Unity's procedural skybox: the ground colour (0.369, 0.349, 0.341 in gamma) below -0.02, blended over the horizon.
    ground = np.array([0.369, 0.349, 0.341]) ** 2.2 * R.colors(P)[2]
    t = np.clip(-ray[..., 1] / 0.02, 0, 1)[..., None]
    return R.sky(ray, P) * (1 - t) + ground * t


def frame(P, after, cam_y=8000.0, pitch=-2.0):
    ray = R.camera(W, H, 50, 30.0, pitch)
    cam = np.array([5000.0, cam_y, 7000.0])
    hz = 80000 * P['visibility']
    ambient, light, _ = R.colors(P)
    with np.errstate(divide='ignore', invalid='ignore'):
        t = (3500.0 - cam[1]) / ray[..., 1]
    hit = (t > 0) & (t < DRAW)
    t = np.where(hit, t, 0)
    xz = cam[[0, 2]] + ray[..., [0, 2]] * t[..., None]
    w = C.sample_wrap(R.WEATHER, xz / C.TILE)
    gaps = np.clip((P['coverage'] - 0.55 - C.sample_wrap(R.WEATHER, xz / 9000.0 + 0.37)) * 5.0, 0, 1)
    a = np.clip((P['coverage'] - w) * 6.0, 0, 1) * gaps * hit
    haze = 1 - np.exp(-t / hz)
    if after:
        haze = np.maximum(haze, R.smoothstep(0.6 * DRAW, DRAW, t))
    c = (light * 0.22 + ambient) * (1 - haze[..., None]) + R.sky(ray, P) * haze[..., None]
    # Behind the clouds: below the horizon there is no ground out here, so it is sky pixels.
    bg = skybox_below(ray, P)
    if after:
        # Below the horizon the sky pixels stand for ground at sea level, hazed by its distance like real ground.
        d = cam[1] / np.maximum(-ray[..., 1], 1e-6)
        h = np.where(ray[..., 1] < 0, 1 - np.exp(-d / hz), 0)[..., None]
        bg = bg * (1 - h) + R.sky(ray, P) * h
    img = c * a[..., None] + bg * (1 - a[..., None])
    return C.to_srgb(img)


rows = []
for wname in ['fair', 'storm']:
    P = dict(R.PRESETS['storm'], visibility=1.0, coverage=0.88) if wname == 'fair' else R.PRESETS['storm']
    rows.append(np.concatenate([R.label(frame(P, False), f'{wname}, 8 km up: before'),
                                R.label(frame(P, True), f'{wname}, 8 km up: after')], 1))
    rows.append(np.concatenate([R.label(frame(P, False, pitch=-25.0), f'{wname}, looking down: before'),
                                R.label(frame(P, True, pitch=-25.0), f'{wname}, looking down: after')], 1))
Image.fromarray(np.concatenate(rows, 0)).save('seam.png')
print('ok')
