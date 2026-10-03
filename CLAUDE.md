# Working on Lanes of Vietnam '65

A real-time lane-tactics game in Unity 6 (6000.6.3f1), built for WebGL. Read
`README.md` first, then `PLAN.md` §12.17 onward: the sections after it are the
owner's playtests in order and what was built for each. The latest is the
code review, §12.23 and `Docs/CODE-REVIEW-2026-10-02.md`.

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
dotnet tools/simcs/bin/Release/net8.0/simcs.dll hash 1 frag smoke drill fieldcraft arms senses gunnery map
dotnet tools/simcs/bin/Release/net8.0/simcs.dll player 24 29 rate=0.6
dotnet tools/simcs/bin/Release/net8.0/simcs.dll aware 3 2400 6 fieldcraft arms senses gunnery tempo
```

## Rules this project keeps

- **The simulation is deterministic and ported line for line** from a
  TypeScript original. It owns no engine objects, reads no clock, and draws
  randomness only from its seeded `Rng`. Positions are `double`.
- **Every Part 2 rule sits behind a `MatchOptions` flag** (Frag, SquadSmoke,
  Drill, Fieldcraft, Arms, Senses, Gunnery). With a flag off the match is
  what it was to the bit: check with `simcs hash` that only the layers you
  meant to change have moved.
- **Pinned hashes** in `Assets/_Project/Tests/EditMode/SimTests.cs` are
  recorded with `simcs hash`. When a change is meant to move a layer, record
  the new values and say why in the commit; the Mac runs the tests.
- **Balance** (`GameRoot.CostFor`, `RateFor`) is measured with `simcs player`;
  a change to pace or fire is followed by measuring it again.
- **Never commit licensed files.** The repository is public. Mixamo FBX files
  and Sonniss audio stay in ignored folders; only their `.meta` files are
  committed. Check before every push.
- **No new downloads** (Mixamo clips, audio, models) without the owner's yes.
- **Measure, then say what was measured.** Numbers in `PLAN.md` come from a
  tool and say which. Frame times are taken in the WebGL build at the owner's
  screen (2592 x 1370), on the Mac.
- **Comments and docs** are plain prose in the owner's terms; write in the
  style of the surrounding code.
- Work on a branch; the owner decides what reaches `main`.
