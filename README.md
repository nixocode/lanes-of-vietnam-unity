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

## What is here

```
Assets/_Project/Scripts/Sim/     pure C#, no UnityEngine, no MonoBehaviour
Assets/_Project/Scripts/View/    everything that draws. Reads sim state, never writes it
Packages/manifest.json           URP, Addressables, Input System, Test Framework
PLAN.md                          the execution plan, the budgets, and the findings carried over
ASSETS.md                        asset ledger: licence, source, date, use
reference/TARGET.jpg             the specification for the look
```

## State

| | |
|---|---|
| Plan | written — `PLAN.md` |
| Project scaffold | done: manifest, assembly definitions, `.gitignore` |
| `Sim.Rng` | ported, **and parity-checked against the original: 31,500 draws over 7 seeds and 5 fork names, 0 mismatches** |
| `Sim.Types`, `Sim.Tune` | ported |
| `Sim.Combat` | ported — fire, suppression, cover, hit chance, targeting |
| `Sim.Squads` | ported — roster, slots, anchor, march |
| **Compiles** | **yes, verified headless, 0 errors** |
| Everything else | not started |

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
