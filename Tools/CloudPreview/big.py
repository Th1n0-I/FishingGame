import os, sys
import numpy as np
from PIL import Image
import clouds as CL, render as R
for kv in sys.argv[3:]:
    k, v = kv.split('='); CL.NEWP[k] = float(v)
P = dict(R.PRESETS['storm'], density=1.5, visibility=0.6, coverage=float(os.environ.get('COV', 0.75)))
CL.ANVIL = CL.anvil_map(P, reach=CL.NEWP['reach'], stretch=CL.NEWP['stretch'], shift=CL.NEWP['shift'],
                        tower_min=CL.NEWP['tower_min'], tower_grow=CL.NEWP['tower_grow'])
rows = []
def pick(kind, k):
    v = CL.BIG_VALUES
    sel = {'m': v < P['cells'] * 0.5, 'y': (v > P['cells'] * 0.6) & (v < P['cells']),
           'h': (v > P['cells'] * 0.5) & (v < P['cells'] * 0.75)}[kind]
    x, y = np.argwhere(sel)[k]
    return CL.BIG_CENTERS[x, y] / 12 * 128000.0, f"{dict(m='mature', y='young', h='half grown')[kind]} {k}"
for spec in sys.argv[2].split(','):
    c, k = pick(spec[0], int(spec[1:]))
    cam = np.array([c[0] - 50000.0, 2.0, c[1] + 4000.0])
    to = c - cam[[0, 2]]
    yaw = np.degrees(np.arctan2(to[0], to[1]))
    rows.append(R.label(CL.view(P, 'new', cam, yaw, 9.0, 38, W=520, H=330), f'cell {k}, from the ground 50 km away'))
Image.fromarray(np.concatenate(rows, 0)).save(sys.argv[1] + '.png')
