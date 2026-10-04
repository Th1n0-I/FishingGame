"""Bigger views of the shafts: colour and opacity only, one frame and averaged."""
import sys
import numpy as np
from PIL import Image
import render as R, common as C

for k, v in (a.split('=') for a in sys.argv[2:]):
    R.NEW[k] = type(R.NEW[k])(float(v))
tag = sys.argv[1] if len(sys.argv) > 1 else 'zoom'
W, H = 400, 190


def shot(P, cam, yaw, pitch, fov, frames, version='new'):
    ray = R.camera(W, H, fov, yaw, pitch)
    tg = np.where(ray[..., 1] < 0, -cam[1] / np.minimum(ray[..., 1], -1e-9), 1e6)
    px, py = np.meshgrid(np.arange(W), np.arange(H))
    acc = 0
    for f in range(frames):
        j = C.ign(px + (f & 63) * 5.588238, py + (f & 63) * 5.588238)
        f_ = R.curtains_new if version == 'new' else R.curtains_old
        c, a = f_(cam, ray, tg, j, P)
        acc = acc + a
    return acc / frames


if __name__ == "__main__":
    rows = []
    for wname in ['storm', 'rain']:
        P = R.PRESETS[wname]
        c = R.active_cell(P, 5)
        for name, off, h, pitch, fov in [('9 km', (0, -9000.0), 2.0, 6.0, 40), ('20 km', (3000, -20000.0), 2.0, 3.0, 30)]:
            cam = np.array([c[0] + off[0], h, c[1] + off[1]])
            to = c - cam[[0, 2]]
            yaw = np.degrees(np.arctan2(to[0], to[1]))
            col = R.render(cam, yaw, pitch, P, 'new', 4, W=W, H=H, fov=fov)
            a1 = shot(P, cam, yaw, pitch, fov, 1)
            a8 = shot(P, cam, yaw, pitch, fov, 6)
            gray = lambda a: np.repeat(np.clip(a, 0, 1)[..., None], 3, -1)
            rows.append(np.concatenate([R.label(col, f'{wname} {name} colour'),
                                        R.label(gray(a1), 'opacity 1 frame'), R.label(gray(a8), 'opacity 6 frames')], 1))
    Image.fromarray((np.concatenate(rows, 0) * 255).astype(np.uint8) if rows[0].dtype != np.uint8 else np.concatenate(rows, 0)).save(tag + '.png')
    print('saved', tag + '.png')

