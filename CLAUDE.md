# Do not take action
Only provide guidance, do not write code to any files instead instruct me as I want to learn to do it myself.

# How to work on the clouds and weather (when I give permission to write code)
- One effect at a time. Fix it, check it, push it, then I test it in the build before the next one.
- Make test renders (numpy previews from the ground and from above) of anything visual before pushing it. Don't ship looks you haven't seen.
- Push after every finished piece, so running out of usage never loses anything.
- Agents are wanted. Use them for bigger jobs (see the agent rules below).
- Before a big round, tell me roughly how big it is so I can decide.
- My 5-hour usage limit runs out in about 1.5 hours of heavy work. Times are Swedish time.
- Add a plain sentence saying what was done only when I probably need it (judge it from what I know and how important the thing is). Explain big things at my level (Unity, C#, HLSL, graphics), keep small things to one line.

# Agent rules (I asked for these explicitly)
- Every agent keeps a log file in docs/agent-logs/<agent-name>.md with everything it tries and how well each thing worked (what it changed, what it measured or saw, kept or dropped and why). Failed attempts go in too.
- Every agent works on its own branch (agent/<agent-name>) and commits often, after every small working step. I give permission for agents to push these branches. Only the main session merges their work into claude/trusting-cray-1vc8cs.
- Reason for all of this: an agent that hits the usage limit loses everything that is not saved. The log and the commits are what survive.
- Start only a few agents at a time, and only when there is enough usage left for them to finish.

# Project notes (so a new session doesn't have to rediscover them)
- Work branch: claude/trusting-cray-1vc8cs. GameCI (.github/workflows/build.yml) builds Windows on every push, artifact FishingGame-Windows.
- No Unity and no C# compiler in the cloud container, the CI build is the only C# check. Shader errors do NOT fail the build: read the job log and grep for "Shader error", "error CS" and "Build Finished".
- For HLSL checks, download DXC (github.com/microsoft/DirectXShaderCompiler releases) and compile each kernel with -T cs_6_0 -HV 2018. The real build uses FXC cs_5_0, which is stricter. If GitHub release downloads are blocked (403), apt-get install glslang-tools and compile each kernel with glslangValidator -D -V -S comp -e <kernel> --amb --auto-map-locations --target-env vulkan1.1. For FullscreenPass, copy the changed function into a small compute file with stubbed uniforms.
- Numpy previews: Tools/CloudPreview (see its README). Ports of the weather and storm maps, the AnvilMap kernel, get_density's storm part, the rain curtains and the rain streaks, with the same numbers as the shaders. Keep them in sync when changing a shader.
- The scene can be read (not edited) with git lfs pull --include="Assets/Scenes/NewTHINODEVSCEN.unity", for serialized values like the cumulus curves (base 2000 m, height 4599 m).
- The scene (Assets/Scenes/NewTHINODEVSCEN.unity) and ProjectSettings are Git LFS and can't be edited here. New settings go in C# field initializers, new objects get created at runtime (NoiseController creates the Day Night Cycle and the Weather System).
- Unity audio is disabled in the project (FMOD does all sound), so AudioSource won't play.
- Main files: Assets/Shaders/VolumetricCompute.compute (kernels in #pragma order: CSMain 0, Resolve 1, CloudsShadows 2, LightVolume 3, AnvilMap 4; every texture has to be bound per kernel from C#), Assets/Scripts/Noise/NoiseController.cs (all plumbing, presets, keys), Assets/Scripts/WeatherSystem.cs, Assets/Scripts/DayNightCycle.cs, Assets/Shaders/FullscreenPass.shader (composite, ground shadows, haze, rain streaks, lightning), Assets/Shaders/ProceduralSky.hlsl, Assets/Shaders/CloudRain.hlsl, Assets/Scripts/Noise/NoiseGen.compute.
- NoiseController has an int field called Time, so write UnityEngine.Time there.
- One global wind moves every cloud layer and the temporal reprojection depends on it. Keep new cloud motion on that same wind.
- Keys: F1-F5 presets, T U K L O H toggles, P [ ] time of day, 3-8 weather, 9 auto weather, B lightning.
- The cloud raymarch goes by step length: step = max(Step Length, distance * Step Growth), capped at Max Steps samples. The quality presets set those three.
- Storm clouds: the AnvilMap kernel draws every frame, from the storm_cells arrays (set in CreateStormMap), x: the anvils, y: where the towers stand, z: their height, w: the anvil's storm height. get_density reads it once. Towers and anvils reach past their storm map grid cell, that's why they have their own map. Storm cell outlines are bent by storm_warp (CloudRain.hlsl), the same in the clouds, the rain and the composite.

# Next up for the weather
Done on 2026-10-04, waiting for my test in the build: rain shafts under the storm cells (start 2 to 5 km out), rain streaks, cumulonimbus (wide clustered towers that grow, flat anvils from the AnvilMap kernel), the horizon seam above the clouds, cloud raymarch by step length.
# Later (not urgent)
- Thunder and rain sound through the FMOD core API.
- SDF raymarch acceleration: medium to hard, small gain after the empty space skipping.
- In the Inspector, NoiseController's perlinWorley3 prefix says perlin_worley_1 instead of perlin_worley_3. Fixing it changes the noise, so it's my call.
