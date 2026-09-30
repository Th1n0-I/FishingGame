# Do not take action
Only provide guidance, do not write code to any files instead instruct me as I want to learn to do it myself.

# How to work on the clouds and weather (when I give permission to write code)
- One effect at a time. Fix it, check it, push it, then I test it in the build before the next one.
- Make test renders (numpy previews from the ground and from above) of anything visual before pushing it. Don't ship looks you haven't seen.
- Push after every finished piece, so running out of usage never loses anything.
- No big multi-agent swarms or panels. They use a lot of usage, and when the limit hits they lose everything they did.
- Before a big round, tell me roughly how big it is so I can decide.
- My 5-hour usage limit runs out in about 1.5 hours of heavy work. Times are Swedish time.
- Every message gets one plain sentence saying what was done. Explain big things at my level (Unity, C#, HLSL, graphics), keep small things to one line.

# Next up for the weather
1. Rain: the rain curtains start right at the camera and make everything flat grey fog. Start them a few km out, make them thinner and shape them into columns under the storm cells. The streaks around the camera are barely visible: brighter, thicker, more of them.
2. Storms look like towering cumulus. Build a real cumulonimbus: the anvil has to spread much wider than the tower and flatten at the top, towers need more density (they look thin and pinkish), cells less round and less evenly spaced.
3. A sharp horizontal seam at the horizon when above the clouds. Check the 200 km draw distance edge and the rain layer first.