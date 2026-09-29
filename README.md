# Lanes of Vietnam '65 — Unity / WebGL

A single-map, real-time lane-tactics game set in Vietnam, 1965, rebuilt in
Unity for the browser. `reference/TARGET.jpg` is the specification for the
look; `Docs/BUILD_BRIEF.md` is the original brief.

**This project does not build yet.** Unity is not installed on this machine.
See `PLAN.md` §0 for the one-time setup the owner needs to do.

## What is here

```
Assets/_Project/Scripts/Sim/     pure C#, no UnityEngine, no MonoBehaviour
Assets/_Project/Scripts/View/    everything that draws. Reads sim state, never writes it
Packages/manifest.json           URP, Addressables, Input System, Test Framework
PLAN.md                          the execution plan, the budgets, and the findings carried over
ASSETS.md                        CC0 ledger. Nothing is fetched without asking
reference/TARGET.jpg             the specification for the look
```

## State

| | |
|---|---|
| Plan | written — `PLAN.md` |
| Project scaffold | done: manifest, assembly definitions, `.gitignore` |
| `Sim.Rng` | ported, **and parity-checked against the original: 31,500 draws over 7 seeds and 5 fork names, 0 mismatches** |
| `Sim.Types`, `Sim.Tune` | ported |
| Everything else | not started |

Nothing above has been compiled. The Sim assembly is plain C# with
`noEngineReferences`, so it should compile standalone, but that has not been
demonstrated and will not be claimed until it has.

## The rules this project runs under

- CC0 assets only, logged in `ASSETS.md`. Nothing downloaded without asking.
- The simulation imports nothing from the renderer. There is a test.
- A match is a pure function of (seed, plan, orders).
- **Measure before claiming.** Nothing is done until it has run in a browser.
- Never push or deploy without asking.
