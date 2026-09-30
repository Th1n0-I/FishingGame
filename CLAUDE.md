# Do not take action
Only provide guidance, do not write code to any files instead instruct me as I want to learn to do it myself.

# How to work on the clouds and weather (when I give permission to write code)
- One effect at a time. Fix it, check it, push it, then I test it in the build before the next one.
- Make test renders (numpy previews from the ground and from above) of anything visual before pushing it. Don't ship looks you haven't seen.
- Push after every finished piece, so running out of usage never loses anything.
- Agents are wanted. Use them for bigger jobs (see the agent rules below).
- Before a big round, tell me roughly how big it is so I can decide.
- My 5-hour usage limit runs out in about 1.5 hours of heavy work. Times are Swedish time.
- Every message gets one plain sentence saying what was done. Explain big things at my level (Unity, C#, HLSL, graphics), keep small things to one line.

# Agent rules (I asked for these explicitly)
- Every agent keeps a log file in docs/agent-logs/<agent-name>.md with everything it tries and how well each thing worked (what it changed, what it measured or saw, kept or dropped and why). Failed attempts go in too.
- Every agent works on its own branch (agent/<agent-name>) and commits often, after every small working step. I give permission for agents to push these branches. Only the main session merges their work into claude/trusting-cray-1vc8cs.
- Reason for all of this: an agent that hits the usage limit loses everything that is not saved. The log and the commits are what survive.
- Start only a few agents at a time, and only when there is enough usage left for them to finish.

# Next up for the weather
1. Rain: the rain curtains start right at the camera and make everything flat grey fog. Start them a few km out, make them thinner and shape them into columns under the storm cells. The streaks around the camera are barely visible: brighter, thicker, more of them.
2. Storms look like towering cumulus. Build a real cumulonimbus: the anvil has to spread much wider than the tower and flatten at the top, towers need more density (they look thin and pinkish), cells less round and less evenly spaced.
3. A sharp horizontal seam at the horizon when above the clouds. Check the 200 km draw distance edge and the rain layer first.