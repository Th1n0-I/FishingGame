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
- For HLSL checks, download DXC (github.com/microsoft/DirectXShaderCompiler releases) and compile each kernel with -T cs_6_0 -HV 2018. The real build uses FXC cs_5_0, which is stricter.
- The scene (Assets/Scenes/NewTHINODEVSCEN.unity) and ProjectSettings are Git LFS and can't be edited here. New settings go in C# field initializers, new objects get created at runtime (NoiseController creates the Day Night Cycle and the Weather System).
- Unity audio is disabled in the project (FMOD does all sound), so AudioSource won't play.
- Main files: Assets/Shaders/VolumetricCompute.compute (kernels in #pragma order: CSMain 0, Resolve 1, CloudsShadows 2, LightVolume 3; every texture has to be bound per kernel from C#), Assets/Scripts/Noise/NoiseController.cs (all plumbing, presets, keys), Assets/Scripts/WeatherSystem.cs, Assets/Scripts/DayNightCycle.cs, Assets/Shaders/FullscreenPass.shader (composite, ground shadows, haze, rain streaks, lightning), Assets/Shaders/ProceduralSky.hlsl, Assets/Shaders/CloudRain.hlsl, Assets/Scripts/Noise/NoiseGen.compute.
- NoiseController has an int field called Time, so write UnityEngine.Time there.
- One global wind moves every cloud layer and the temporal reprojection depends on it. Keep new cloud motion on that same wind.
- Keys: F1-F5 presets, T U K L O H toggles, P [ ] time of day, 3-8 weather, 9 auto weather, B lightning.

# Next up for the weather
1. Rain: the rain curtains start right at the camera and make everything flat grey fog. Start them a few km out, make them thinner and shape them into columns under the storm cells. The streaks around the camera are barely visible: brighter, thicker, more of them.
2. Storms look like towering cumulus. Build a real cumulonimbus: the anvil has to spread much wider than the tower and flatten at the top, towers need more density (they look thin and pinkish), cells less round and less evenly spaced.
3. A sharp horizontal seam at the horizon when above the clouds. Check the 200 km draw distance edge and the rain layer first.
# Later (not urgent)
- Thunder and rain sound through the FMOD core API.
- SDF raymarch acceleration: medium to hard, small gain after the empty space skipping.
- The noise viewer's weather preview is partly see-through, because the weather map alpha now holds the cirrus wisps.
- In the Inspector, NoiseController's perlinWorley3 prefix says perlin_worley_1 instead of perlin_worley_3. Fixing it changes the noise, so it's my call.
