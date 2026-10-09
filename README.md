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

# play the demo locally (Brotli headers set correctly), then open http://127.0.0.1:8065/
tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.WebGL
python3 tools/serve.py Builds/web --port 8065

# stage the build for a static host, with its headers, and check what would go up; `prod` deploys it (PLAN §12.28)
tools/deploy.sh

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
| Step 0 — empty WebGL build | **7.34 MB** initial with physics removed (was 8.00), the floor |
| Step 1 — simulation | ported and **proven exact**: 22 matches identical to the TypeScript original at every tick; 60/60 tests |
| Step 2 — harnesses | capture, LookMeter, Flicker, Budget: running, zeros proven |
| Step 2a — technology decisions | editor, physics, modules, streaming: decided and measured — PLAN §12.9a |
| Step 3 — grey-box map | lanes readable; the map's layout rules kept under the art |
| Step 5/5a — command and interface | selection, orders, field glasses, the HUD to TARGET.jpg, a hold/go lever over every strongpoint; UIAudit 11/11 |
| Step 6a/6b — art | photographed sky that lights the scene; scanned ground; 13 baked plant species; palms, bamboo, grass; the real Chu Pong massif (SRTM); sandbags, tower, trucks; cloud shadows; a measured grade |
| Soldiers | six rebuilt bodies (three a side) on a Humanoid avatar, 48 Mixamo clips, stepped by match time (~0.35 ms a frame in WebGL); twelve built weapon models, one for every class |
| Step 7 — the fighting | tracers, flashes, dust, shells, smoke, scorch marks, all from the sim's events |
| Step 9 — audio | sourced recordings through a Web Audio port of the measured distance model |
| Part 2 | grenades, squad smoke, drill and fieldcraft (stand-off, strongpoints with limited room and Warfare 1944's lever, melee, rounds through men), and arms (a weapon and a model for every class, a sniper team, fights at each weapon's distance), each behind a match option; parity baseline tagged and unchanged |
| Playtest 9 | squads that open fire when they see each other and close by bounds, fewer grenades, a sniper's distance (Tactics); men who differ, and luck (Fortune); bodies thrown by a burst, limbs taken off, more blood, twice the deaths (PLAN §12.26) |
| Build | 25.58 MB initial (≤ 45); flicker 0.08 (≤ 1.30), SMAA |
| Frame | 4.2 to 4.7 ms mean, 6 ms 95th percentile at 2592 x 1370 in WebGL (M4 Pro); the review that got it there is `Docs/CODE-REVIEW-2026-10-02.md` |
| Demo | on Vercel since 2026-10-09, after a security check: only the build goes up, under a content policy that lets it call nobody (PLAN §12.28) |
| Tests | EditMode 71/71, PlayMode 19/19 (2026-10-09, PLAN §12.28) |

The measured detail, step by step, is in PLAN §10a. Where it stands and what is next: PLAN §12.27, the plan from here (polish, then scenarios on the firebase, then maps, Ia Drang first). The latest work is §12.28.

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
