# Code review, 2026-10-02: frames, motion, awareness

Asked for by the owner after playtest 7 (PLAN §12.22): "frames have taken a
hit … way more bugs when walking, sliding around … review the code to fix
performance and animation and AI awareness of the whole codebase. Make a
report + fixes that don't break the build. Remove all the code that isn't
needed." The version before any of this is `daccef6`, on GitHub, as asked.

Every number here was measured, in the WebGL build, in headless Chrome on the
owner's machine (M4 Pro, ANGLE Metal), at the resolution his screen gives the
game: 2592 x 1370 (1728 x 1000 points at the page's 1.5x cap). The frame-time
probe (`?perf=1`, `View/PerfProbe.cs`) now says what each of the game's own
systems cost and can switch parts of the frame off (`&off=…`), so "what does
X cost" is the difference between two runs of one build.

## The result

| | before (`daccef6`) | after |
|---|---|---|
| frame, mean | 10.7 ms | **4.5 ms** |
| frame, median | 10.5 ms | 4.0 ms |
| frame, 95th percentile | 18 to 20 ms | **6 ms** |
| worst frame in five seconds (after the first) | 23 to 29 ms | 9 to 12 ms |
| garbage collections | one a second | none in 20 s |
| late in a match (seed 7 from tick 3400) | 11.1 ms | 4.6 ms |
| at 1843 x 913 (the size it had been measured at) | 6.0 ms | 2.6 ms |
| download | 29.87 MB | 25.07 MB |
| ground covered without the legs (motion audit) | 4.9% | 0.6% |
| at rest with the legs going | 2.2% | 0.0% |
| EditMode / PlayMode tests | 60 / 17 | 60 / 17 |

A 120 Hz screen gives a frame 8.3 ms. At 10.5 the game missed it on every
frame, and every twentieth of a second it missed two: that is the hit. It was
not new in the last build (it had been measured at a small window, where it
was 6 ms), but the squad tags of phase 2 added to it.

## Performance: what was found

In the order of what it cost.

1. **The ground: 6.2 ms of the 10.7.** Found by switching it off
   (`off=mat:Ground`: 10.7 to 4.5 ms). `Ground.shader` read all four of its
   texture layers at every pixel, twelve reads, each of them anisotropic
   because the camera looks along the ground at a few degrees; most of the
   ground is one layer. *Fixed:* a layer is read only where it has weight (the
   same 0.001 the blend already used, so the picture is the same: 0.012 of 255
   mean difference between captures, in two blood marks). Ground cost after:
   2.4 ms.
2. **Anisotropic filtering forced on for every texture: 0.9 ms.** The project
   setting was "forced on", which raises every texture to 9x or more; the
   importer already gives each texture its own level (8 on the ground, 1 to 4
   elsewhere). *Fixed:* per texture. 0.009 of 255 mean difference in a capture.
3. **The interface: every long frame.** With the HUD off, the frames over 15
   ms went from 90 in five seconds to 1.
   - Plates, squad tags and lane tags were moved by `left` and `top`: a layout
     of the whole panel and its text drawn again, whenever the camera or a
     squad moved. *Fixed:* moved by a transform (`translate`,
     `DynamicTransform`), which is neither.
   - The tactical strip was drawn with the vector painter, a path filled per
     pip, on every frame. *Fixed:* its triangles are written straight into
     the panel's mesh, the pips four times a second, and the camera's window
     over it is its own element moved by a transform.
   - A card's cooldown shade changed its height every tick. *Fixed:* a scale.
   - Strings made every frame (a count a plate, the tag text, the hint, the
     orders line, the speed). *Fixed:* written when they change.
   What is left: a frame in which the strip's pips are redrawn still runs 2 to
   3 ms long in the benchmark (frames as fast as they will go). At the
   browser's own pacing no frame ran long in either build at 60 Hz; whether
   it shows at 120 Hz is for the owner's eye.
4. **Garbage: one collection a second.** The largest source was one line:
   `CombatView.Flat` made an array for every quad on the ground, every frame
   (hundreds of them late in a match). Also a roster list a squad a
   frame (dead squads too) in the squad tags, the audio view searching the
   scene for the HUD every frame, the deployer filtering the deck twice a
   frame. *Fixed*, all of them.
5. **The effects renderer: 0.38 ms to 0.10.** Every shot of the last eleven
   seconds had its two men found and the ground under them measured, every
   frame, to draw nothing (a shot's dust is gone in two). And every blood
   pool, scorch and dead man's shadow of the whole match was rebuilt every
   frame from the whole event log, so it grew all match. *Fixed:* each event
   kind has its own lifetime; marks that have stopped changing are built once
   into a second mesh.
6. **Dead men.** A fallen man who has been baked to a mesh still had his
   ground height, his screen position and his skin quality worked out every
   frame. *Fixed:* skipped.

Measured and left alone: shadows (1.0 ms), the plants (0.9), SMAA (0.65), soft
shadows (0.4), bloom, the clouds' shadows and the depth texture (nothing to
speak of), the simulation (0.02 ms a frame), a card in hand (nothing).

## Animation: what was found

The simulation moves a man in steps of a twentieth of a second and at one
speed: at rest, then (in contact) four metres a second, then at rest. The
view drew him exactly there.

1. **He was carried while he got up.** The simulation holds a man 0.6 s to
   rise from a knee and 1.2 s from the ground; the clips take 0.69 and 1.28.
   For the last tenth of a second he was moved at a sprint, 40 cm, still
   coming up.
2. **His legs were a tenth of a second behind his body** at every start, and
   still running after it had stopped.
3. **His feet slid forward and were put back** when he was small on the
   screen: he was moved every frame and posed every other.
4. **A dash of two metres at a sprint** was all three at once, and most
   movement in contact is dashes of two or three metres. This is the sliding.

*Fixed* (`ArmyView.DrawFigures`): the drawn man follows the simulation's
(critically damped, 0.13 s); told to be on his feet and not on them yet, he
waits; his gait is played at the speed he is drawn moving at; he is moved on
the frames he is posed; and he is posed every frame unless the frames come
faster than 90 a second. His shadow, his tag, his selection ring and the
round that hits him are drawn where he is drawn (`ArmyView.Where`).

The motion audit has a measure for it now, and a gate on it: metres covered
that the legs do not account for. On the same match, old code against new:
4.9% to 1.7%; with the slower pace of §12.22, 0.6%.

## AI awareness: what was found

1. **A broken squad could run toward the enemy.** A squad decided from its
   anchor, which is leashed to its living men after it moves, and men fall
   after that. A squad whose lead man had just been shot twelve metres ahead
   of the rest broke, chose "the cover behind it" from where he had been, and
   ran forward 3.6 m to it. Every distance a squad judges by (the enemy in
   front of it, whether to go in) had the same stale point. A test caught it
   the moment the game's economy numbers changed. *Fixed* (`Match.Step`): the
   anchor is leashed to the living before the squad decides. The pinned
   hashes for Senses and Gunnery moved; the baseline, Frag, Drill, Fieldcraft
   and Arms pins did not, and parity with the TypeScript original is untouched.
2. Read and found sound: sight (`Senses.Sight`, `Look`), who fires at whom
   (`PickTarget`, `Suppress`), the task machine (`Decide`, `Take`), the
   stand-off and the levers (`Fieldcraft.Move`, `March`).
3. Measured (`simcs aware`, six seeds, a minute of fighting), before and
   after everything here including the slower pace: squads within 15 m with
   neither firing on the other, 11.1 squad-seconds to 8.0 (of which "neither
   has the other in sight" 3.0 to 0.4); men idle in the open beside cover
   with a seen enemy in range, 20.9 man-seconds to 13.7; men on their feet and
   still with a seen enemy in range, 16% to 15%. Slower men take longer to
   their first shot after contact (median 2.4 s to 4.2).
4. Not changed, for the owner to decide:
   - The two lanes are separate fights (Gunnery): a squad ignores the one
     abreast of it in the other lane. Known since §12.19.
   - The **bound** order (key 3) still exists and does nothing a march does
     not, since Senses. It is no longer taught. Remove the key, or give it
     back a meaning.
   - The VC are the easier side to play at every level.

## Removed

- **The sprite soldiers and the capsule soldiers** (`ArmyView`, some 230 lines; the
  scene builder's wiring; `?perf=sprites`): two stand-ins nothing had drawn
  since the 3D men. With them their atlases (`Art/Soldiers`, 21 MB of source,
  4.8 MB of the download) and five stand-in materials. In git at `daccef6`.
- **The grey-box ridges** (`WorldDressing.Ridges`): a stand-in for the
  mountains, which are in the repository.
- **Members nothing referenced:** `Combat.CanEngage`, `Ground.NormalAt` and
  `SlopeAt`, `Rng.Normal`, `Squads.Strength`, `MatchDriver.StepLength`,
  `JsMath.TwoPow24`, three `Tune` constants (`SlotPull`,
  `MetresPerManInCover`, `BoundOpen`), `GameRoot.MusterCost`,
  `WorldDressing.PlantCount`, `MountainView.Vertices`.
- Left, though interim: the grey-box plant and wall fall-backs in
  `WorldDressing` (they share random draws with the real layout), the
  interim animation clips (a clone of the public repository has no Mixamo
  files and needs them), the soldier recipes in `tools/blender/plant_bake.py`.

## Also found

- `tools/simcs/SimCs.csproj` was never in the repository: `.gitignore` ignores
  every `*.csproj` for Unity's sake. A clone could not build the headless
  simulation the README tells it to run. *Fixed.*
- The frame-time probe paired each frame's time with the next frame's work.
  *Fixed.*

## Playtest 7's other notes (PLAN §12.22), done in the same pass

- **Pace.** In contact a man goes at 2.4 m/s, not 4.0, and his squad at 2.2,
  not 3.8; on the march 1.1, not 1.35. Difficulty and match length measured
  again (`GameRoot.CostFor`, `RateFor`).
- **The climb** into and out of a trench is a quarter slower, clip and
  simulation together.
- **Grenades** in the air are drawn larger, dense, with a pale line behind
  them. They were a 22 cm puff of smoke that faded wherever it neared the
  ground.

## Not done, and why

- A **jog**. At 2.4 m/s the men play the walk clip a third fast, because the
  only faster clip runs at 4.5 m/s and at half speed is a run in slow motion.
  Mixamo has rifle jogs; a download needs the owner's yes.
- The simulation makes a list for every living squad every tick. It is
  small now (no collection in 20 s) and it is the parity-checked core.
