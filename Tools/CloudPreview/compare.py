import numpy as np
from PIL import Image
import sys
import render as R, zoom as Z
R.BIG_CELLS = True
for k, v in dict(visibility=3500, veil=0.15, strand_size=4000, strand_lo=0.1, strand_mul=1.8, cap=96).items():
    R.NEW[k] = type(R.NEW[k])(v)
rows = []
gray = lambda a: np.repeat(np.clip(a, 0, 1)[..., None], 3, -1)
for wname in ['storm', 'rain']:
    P = R.PRESETS[wname]
    c = R.active_cell(P, 5)
    for vname, off, pitch, fov in [('9 km from a storm cell', (0, -9000.0), 6.0, 40), ('in the cell', (600, 0.0), 8.0, 65)]:
        cam = np.array([c[0] + off[0], 2.0, c[1] + off[1]])
        to = c - cam[[0, 2]]
        yaw = np.degrees(np.arctan2(to[0], to[1])) if np.linalg.norm(to) > 1 else 30.0
        row = []
        for ver in ['old', 'new']:
            row.append(R.label(R.render(cam, yaw, pitch, P, ver, 4, W=Z.W, H=Z.H, fov=fov), f'{wname}, {vname}: {ver}'))
            row.append(R.label(gray(Z.shot(P, cam, yaw, pitch, fov, 4, ver)), f'{ver}: rain opacity only'))
        rows.append(np.concatenate(row, 1))
Image.fromarray(np.concatenate(rows, 0)).save('rain_old_vs_new.png')
print('ok')
