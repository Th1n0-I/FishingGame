# Cloud preview

Numpy ports of the cloud shaders, to look at a change before pushing it (there is no Unity in the cloud container).
They render PNGs on the CPU, about 30 s each. Run them from this folder:

    pip install numpy pillow
    python3 -W ignore big.py storms m2,m8,h0,y1     # storm towers from the ground, 50 km away
    python3 -W ignore clouds.py old,new towers 3,7   # storm towers, old vs new, from the side, above and 30 km up
    python3 -W ignore compare.py                     # rain curtains, old vs new
    python3 -W ignore streaks.py streaks             # rain streaks around the camera, old vs new
    python3 -W ignore seam.py                        # the horizon from above the clouds

What mirrors what:
- `common.py`: the weather map (`NoiseGen.compute` gen_weather, r, a and b channels), the storm map (`CreateStormMap`,
  with a port of .NET's `System.Random`, so the cells are where the game has them), the procedural sky
  (`ProceduralSky.hlsl`).
- `clouds.py`: the low octaves of the perlin-worley texture (64^3), the AnvilMap kernel (`anvil_map`) and
  `get_density`'s storm part (`density(..., 'new')`), plus a small raymarcher with a short light march.
- `render.py`: `rain_curtains` and `rain_density` (`curtains_new`), with a flat stand-in for the cloud layer.
- `streaks.py`: `rain_streaks` and its blend in `FullscreenPass.shader`.

The ports use the same numbers as the shaders. When a shader changes, change its port the same way.

Limits: the base noise is 4x coarser than the game's and has no detail erosion, so clouds look blocky. The lighting is
simplified and the raymarch has no jitter (the wood grain stripes). Good for shapes and amounts, not for exact looks.
