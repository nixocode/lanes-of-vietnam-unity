# Lanes of Vietnam '65 — Unity / WebGL

A single-map, real-time lane-tactics game set in Vietnam, 1965, rebuilt in
Unity for the browser. `reference/TARGET.jpg` is the specification for the
look; `Docs/BUILD_BRIEF.md` is the original brief.

**The toolchain is installed and the project compiles.** Verified headless on
2026-09-29: Unity 6000.6.3f1 with Web Build Support, exit code 0, 0 compile
errors. See `PLAN.md` §0.

```bash
# compile check, no GUI
"/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -nographics -projectPath "$(pwd)" -logFile /tmp/unity.log
grep -c "error CS" /tmp/unity.log     # expect 0
```

## Running things

```bash
# project settings (URP, Linear, WebGL player) — idempotent
tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.ProjectSetup.Apply

# the simulation's tests, headless (PLAN §10 step 1's gate)
tools/unity.sh -nographics -runTests -testPlatform EditMode -testResults "$PWD/Logs/tests.xml"

# the simulation without Unity, on the editor's bundled .NET 8
tools/simcs/run.sh seeds 48 ceiling floor     # a balance block, with a Wilson interval
tools/simcs/run.sh audit                      # every event and ending reachable
tools/simcs/run.sh determinism
tools/simcs/run.sh bench

# parity against the TypeScript original (needs the sibling "Lanes of vietnam" folder)
node --experimental-strip-types tools/parity/trace.ts > /tmp/ts-trace.json
tools/simcs/run.sh parity /tmp/ts-trace.json

# WebGL builds; each writes size.json beside itself
tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.EmptyFloor
tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.WebGL
```

## What is here

```
Assets/_Project/Scripts/Sim/     pure C#, no engine references — the whole game's rules
Assets/_Project/Scripts/View/    everything that draws. Reads sim state, never writes it
Assets/_Project/Scripts/Tools/   editor tooling: project setup, builds, capture
Assets/_Project/Tests/EditMode/  SimTests: determinism, parity, §9 findings as assertions
tools/simcs/                     the sim headless on .NET 8 (brief §10's simnode)
tools/parity/                    the TypeScript side of the parity check
PLAN.md                          the execution plan, budgets, findings and progress (§10a)
ASSETS.md                        asset ledger: licence, source, date, use
reference/TARGET.jpg             the specification for the look
```

## State

| | |
|---|---|
| Toolchain | Unity 6000.6.3f1 + Web Build Support, verified headless |
| Step 0 — empty WebGL build | **8.00 MB** initial, the floor |
| Step 1 — simulation | ported and **proven exact**: 22 matches identical to the TypeScript original at every tick; 30/30 tests |
| Steps 2–10 | not started — PLAN §10 |

## Direction

**Photoreal** — decided 2026-09-29. Scanned PBR materials, `TARGET.jpg` as the
bar. The build-size budget moved to 45 MB initial / 80 MB total to pay for it;
the frame budget did not move, so the look is bought with material quality
rather than triangles. `PLAN.md` §2 and §11.

## The rules this project runs under

- Assets: free, highest quality, reputable sources, every one logged in
  `ASSETS.md` with its licence. Adobe tools (Mixamo, Photoshop) are in scope.
  Owner's decision, 2026-09-29 — see `PLAN.md` §8.
- The simulation imports nothing from the renderer. There is a test.
- A match is a pure function of (seed, plan, orders).
- **Measure before claiming.** Nothing is done until it has run in a browser.
- Never push or deploy without asking.
