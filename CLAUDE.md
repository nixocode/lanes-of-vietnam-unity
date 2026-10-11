# Working on Lanes of Vietnam '65

A real-time lane-tactics game in Unity 6 (6000.6.3f1), built for WebGL. Read
`README.md` first, then `PLAN.md` §12.17 onward: the sections after it are the
owner's playtests in order and what was built for each. The plan from here is
§12.27: polish, then scenarios on the firebase map, then maps one at a time,
and what the owner still has to decide. The latest work is §12.29 to §12.41 (fire control,
the Mixamo batches, rounds seen to land, ballistics, what a round and a burst do to a
man, the watchtower, the computer's commander, squads that keep together, and Phase S:
the map and the scenario as data, a siege and an assault, four operations on the firebase,
on the branch `scenarios`, which is what the demo serves; and the history rewritten on
2026-10-11 to take personal addresses out of it: a commit id written down before that day is
not one the repository has; and Bearing, how a man carries himself, on the branch `smooth`).

## What runs where

The owner's Mac has Unity, the GPU, Chrome and the licensed assets. A cloud
session has none of them. It has the repository and can install the .NET 8 SDK.

| | cloud session | the Mac |
|---|---|---|
| the simulation (`Assets/_Project/Scripts/Sim/`): rules, AI, balance | yes, through `tools/simcs` | yes |
| `tools/simcs`: hashes, determinism, the audit, balance tables, the awareness measurements | yes | yes |
| everything in `Scripts/View/`, `Scripts/Tools/`, shaders, the scene | edit with care; cannot compile or test | yes |
| EditMode and PlayMode tests, captures, the motion audit, WebGL builds, frame times | no | yes |
| Mixamo clips and licensed audio (`Assets/_Licensed/`, `StreamingAssets/Audio/licensed/`) | not in the repository | yes |

So cloud work belongs in the simulation: the AI, the rules, balance,
measurements. View work done in the cloud is unverified until the Mac
compiles and plays it; say so in the commit message.

## The simulation, headless

`tools/simcs/run.sh` needs zsh and the editor's bundled .NET. Without them:

```bash
dotnet build tools/simcs/SimCs.csproj -c Release
dotnet tools/simcs/bin/Release/net8.0/simcs.dll determinism
dotnet tools/simcs/bin/Release/net8.0/simcs.dll audit
dotnet tools/simcs/bin/Release/net8.0/simcs.dll hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics map
dotnet tools/simcs/bin/Release/net8.0/simcs.dll player 48 35 rate=0.95 tactics fortune firecontrol ballistics vantage commander cohesion bearing
dotnet tools/simcs/bin/Release/net8.0/simcs.dll shape 20 2400 20 fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing tempo
dotnet tools/simcs/bin/Release/net8.0/simcs.dll smooth 20 2400 20 fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing tempo
dotnet tools/simcs/bin/Release/net8.0/simcs.dll scenario 48
dotnet tools/simcs/bin/Release/net8.0/simcs.dll scenario hash 1
dotnet tools/simcs/bin/Release/net8.0/simcs.dll aware 3 2400 6 fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics tempo
dotnet tools/simcs/bin/Release/net8.0/simcs.dll muddle 3 2400 6 fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics tempo
dotnet tools/simcs/bin/Release/net8.0/simcs.dll brain 3 2400 6 fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics tempo
dotnet tools/simcs/bin/Release/net8.0/simcs.dll gunplay 3 2400 6 fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics tempo
```

Before changing anything, a build of `main` gives these; if it does not, stop
and say so:

- `determinism`: ok.
- `hash 1` (the parity baseline, every flag off): 4390 ticks, final `2256114725u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo map` (every rule
  up to playtest 8): 2725 ticks, final `988759736u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune map`
  (every rule up to playtest 9): 2719 ticks, final `4215652832u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol map`
  (every rule up to fire control; on the branch `polish`, `main` does not have these flags
  yet): 5070 ticks, final `1731903378u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics map`
  (every rule up to ballistics, on `polish`): 3493 ticks, final `2457736883u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage map`
  (with the watchtower's rule, on `polish`; nobody is on it, so the match is the one above with
  more hashed): 3493 ticks, final `2586780023u`. With `aiposts` (the computer mans its tower,
  which the game does not do): 4236 ticks, final `1179344068u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander map`
  (every rule up to the commander, on `polish`): 5206 ticks, final `4057359457u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion map`
  (every rule up to cohesion, on `polish`): 2524 ticks, final `3667431746u`.
- `hash 1 frag smoke drill fieldcraft arms senses gunnery ammo tactics fortune firecontrol ballistics vantage commander cohesion bearing map`
  (the game, on `smooth`): 7104 ticks, final `465850309u`.
- `scenario hash 1` (each operation as the game plays it against a Veteran, with nobody playing).
  On `scenarios`: the firebase 5924 ticks, final `200793914u`; The Wire 6000, `3885593046u`;
  First Light 3709, `800917681u`; The Earth Fights 9080, `2805131492u`. On `smooth`, where a
  scenario is played with Bearing and at the costs and rates of §12.41: the firebase 4297,
  `3054897140u`; The Wire 5402, `3894234249u`; First Light 1370, `2733762918u`; The Earth
  Fights 3427, `3886742372u`.

## Rules this project keeps

- **The simulation is deterministic and ported line for line** from a
  TypeScript original. It owns no engine objects, reads no clock, and draws
  randomness only from its seeded `Rng`. Positions are `double`.
- **Every Part 2 rule sits behind a `MatchOptions` flag** (Frag, SquadSmoke,
  Drill, Fieldcraft, Arms, Senses, Gunnery, Ammo, Tactics, Fortune, FireControl, Ballistics,
  Vantage, Commander, Cohesion, Bearing: all on in the game). With a
  flag off the match is what it was to the bit: check with `simcs hash` that
  only the layers you meant to change have moved.
- **A scenario is data** (`Sim/Scenario.cs`, on a map from `Sim/Maps.cs`), and
  what it changes in a match is an option that defaults to the baseline: a
  side's `Means`, its deck, the `Attacker`, the `MatchMode` and its clock. The
  game's dials (cost by difficulty, rate by length, the economy) are kept in
  `Scenario`, so `simcs scenario` measures with the numbers the game plays at.
  A scenario's numbers are measured with it (28 to 40 wins of 48 against a
  Veteran; a rule that moves how men fight moves every one of them, and one
  seed block of 48 can be ten wins off another: use 192, §12.41) and its pin
  recorded with `simcs scenario hash`.
- **Pinned hashes** in `Assets/_Project/Tests/EditMode/SimTests.cs` are
  recorded with `simcs hash`. When a change is meant to move a layer, record
  the new values and say why in the commit; the Mac runs the tests.
- **Balance** (`GameRoot.CostFor`, `RateFor`) is measured with `simcs player`;
  a change to pace or fire is followed by measuring it again.
- **Never commit licensed files.** The repository is public. Mixamo FBX files
  and Sonniss audio stay in ignored folders; only their `.meta` files are
  committed. Check before every push.
- **No new downloads** (Mixamo clips, audio, models) without the owner's yes.
- **The demo goes up with `tools/deploy.sh` and nothing else** (PLAN §12.28),
  on the owner's word each time. It stages the build alone, checks every file
  against what a build is made of, and writes the headers. Never run `vercel`
  from the project folder: that uploads the project.
- **Measure, then say what was measured.** Numbers in `PLAN.md` come from a
  tool and say which. Frame times are taken in the WebGL build at the owner's
  screen (2592 x 1370), on the Mac.
- **Comments and docs** are plain prose in the owner's terms; write in the
  style of the surrounding code.
- Work on a branch; the owner decides what reaches `main`.

## A cloud session's hand-over

The Mac has to finish what a cloud session starts, so leave it what it needs:

- A branch named `cloud/<what it is>`, pushed, never merged into `main`.
- New pins recorded in `SimTests.cs`, with the `simcs hash` line that gave each
  one in the commit message, and a test for each new rule beside the others.
- A new section at the end of `PLAN.md` §12 in the style of §12.24: what was
  asked, what was built, the measurements before and after and the command
  that gave each, and what is left for the Mac (tests to run, what the view
  needs to show the rule, what to look at in a playtest).
- A new `EventKind` goes at the end of the enum, and anything the view will
  have to draw is listed in that section rather than guessed at in `View/`.
- A new `.cs` file comes without its `.meta`; Unity on the Mac makes it. Name
  the new files in the section so they are not missed.

The briefs for cloud work are in `Docs/cloud/`, one a session. From the
project folder, in a terminal of its own: `claude --cloud "Do the work in
Docs/cloud/m1-sim.md"`.
