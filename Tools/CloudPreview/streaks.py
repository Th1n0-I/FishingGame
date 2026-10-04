"""Rain streaks around the camera: rain_streaks() and its blend from FullscreenPass.shader, old and new, over a rainy
background frame from the rain preview."""
import sys
import numpy as np
from PIL import Image
import common as C
import render as R

W, H, FOV = 960, 540, 60.0
PIXEL_ANGLE = 2 * np.tan(np.radians(FOV / 2)) / H
TIME = 1.37
WIND_GROUND = np.array([-1.0, 0.0]) * 55 * 0.3  # rain preset wind 55 m/s, 0.3 of it near the ground


def rain_hash(p):
    p = np.mod(p * np.array([0.1031, 0.1030]), 1.0)
    p = p + np.sum(p * (p[..., ::-1] + 33.33), -1, keepdims=True)
    return np.mod((p[..., 0] + p[..., 1]) * p[..., 0], 1.0)


def streaks(ray, scene_distance, amount, version, cam_y=2.0):
    horizontal = np.linalg.norm(ray[..., [0, 2]], axis=-1)
    angle = np.arctan2(ray[..., 2], ray[..., 0])
    wind_across = (WIND_GROUND[0] * -ray[..., 2] + WIND_GROUND[1] * ray[..., 0]) / np.maximum(horizontal, 1e-4)
    cover = np.zeros(ray.shape[:2])
    if version == 'old':
        layers = [(3.0 * 2.3 ** l, 0.3 * (1 + l), 2.0 * (1 + l), 0.4, 1.2, 1 - 0.18 * l) for l in range(4)]
    else:
        layers = [(2.5 * 1.9 ** l, 0.18 * (1 + 0.6 * l), 1.6 * (1 + 0.7 * l), 0.5, 2.2, 1 - 0.12 * l) for l in range(6)]
    for layer, (radius, spacing, cell_height, length, px, weight) in enumerate(layers):
        t = radius / np.maximum(horizontal, 1e-4)
        ok = (horizontal >= 0.05) & (t <= scene_distance)
        columns = max(round(2 * np.pi * radius / spacing), 1)
        cell_width = 2 * np.pi * radius / columns
        height = cam_y + ray[..., 1] * t
        u = (angle / (2 * np.pi) + 0.5) * columns - wind_across * TIME / cell_width
        v = (height + 9.0 * TIME) / cell_height + layer * 0.37
        cell = np.floor(np.stack([u, v], -1))
        f = np.stack([u, v], -1) - cell
        h = rain_hash(cell + layer * 17.0)
        along = np.mod(f[..., 1] - np.mod(h * 7.7, 1.0), 1.0) / length
        slant = np.clip(wind_across * cell_height / (9.0 * cell_width), -0.5, 0.5)
        x = 0.15 + 0.7 * np.mod(h * 13.1, 1.0) - along * length * slant
        width = np.maximum(t * PIXEL_ANGLE * px / cell_width, 0.005)
        c = np.clip(1 - np.abs(f[..., 0] - x) / width, 0, 1) * np.sin(np.clip(along, 0, 1) * np.pi) * weight
        cover += np.where(ok & (h <= amount) & (along <= 1.0), c, 0)
    return np.clip(cover, 0, 1)


def composite(bg, ray, cover, version, P):
    sky = R.sky(np.stack([ray[..., 0], np.full(ray.shape[:2], 0.2), ray[..., 2]], -1), P)
    if version == 'old':
        return bg + (sky * 0.8 - bg) * (cover * 0.35)[..., None]
    # Drops catch the sky light around them and show up lighter than what is behind them.
    rain_color = np.maximum(sky * 1.1, bg * 1.35)
    return bg + (rain_color - bg) * (cover * 0.6)[..., None]


if __name__ == '__main__':
    tag = sys.argv[1]
    P = R.PRESETS['rain']
    c = R.active_cell(P, 5)
    rows = []
    for name, off, pitch, yaw_add in [('looking at the horizon', (0, -9000.0), 3.0, 0.0), ('looking at a dark storm', (600, 0.0), 6.0, 40.0)]:
        cam = np.array([c[0] + off[0], 2.0, c[1] + off[1]])
        to = c - cam[[0, 2]]
        yaw = np.degrees(np.arctan2(to[0], to[1])) + yaw_add
        ray = R.camera(W, H, FOV, yaw, pitch)
        # The background frame without streaks, in linear colour.
        bg_srgb = R.render(cam, yaw, pitch, P, 'new', 2, W=W, H=H, fov=FOV).astype(np.float64)
        bg = np.where(bg_srgb <= 0.04045, bg_srgb / 12.92, ((bg_srgb + 0.055) / 1.055) ** 2.4)
        dist = np.where(ray[..., 1] < 0, 2.0 / np.maximum(-ray[..., 1], 1e-6), 1e9)
        out = []
        for version in ['old', 'new']:
            cover = streaks(ray, dist, 0.5, version)
            img = composite(bg, ray, cover, version, P)
            srgb = np.clip(np.where(img <= 0.0031308, img * 12.92, 1.055 * np.power(np.clip(img, 0, None), 1 / 2.4) - 0.055), 0, 1)
            out.append(R.label(srgb, f'{name}: {version}'))
        rows.append(np.concatenate(out, 1))
    Image.fromarray(np.concatenate(rows, 0)).save(tag + '.png')
    print('saved')
