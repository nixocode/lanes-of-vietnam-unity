# Lanes of Vietnam '65 — Unity / WebGL

A rebuild of the three.js project in Unity, targeting the browser.
`reference/TARGET.jpg` is the specification for the look. `Docs/BUILD_BRIEF.md`
is the original brief and still governs everything it covers.

**Status: scaffolded, not yet buildable on this machine.** See §0.

---

## 0. The blocker, first

Unity is not installed here. No Unity Hub, no Editor, no `dotnet`, no `mono`.
Nothing in this folder can be compiled, built or run until that changes, and
installing it needs a Unity account login, which is the owner's to do.

**What the owner needs to do, once:**

1. Install **Unity Hub** — <https://unity.com/download>
2. Through Hub, install **Unity 6000.x LTS** (Unity 6). During install tick:
   - **WebGL Build Support** (required)
   - **Mac Build Support (Mono)** (for fast iteration in the Editor)
3. Open this folder as a project. Unity will generate `Library/` and the rest.

Until then everything here is authored blind, and this file says so wherever
that matters. Nothing below claims to have been measured.

**Why that matters more than usual on this project:** the single most
expensive habit in the three.js build was claiming a thing worked because the
code looked right. Every wrong claim came from an instrument that could not
see the defect. Unity cannot be verified from this shell at all, so the rule
here is stricter — **nothing in this project is "done" until it has been run
in a browser and measured.** Code written before the Editor exists is a draft.

---

## 1. Why Unity, and what we are giving up

The three.js version reached a §4 colour table within a point on every
whole-frame row and still did not hold up beside `TARGET.jpg`. The honest
closing assessment was that the remaining gap had stopped being grading and
become **assets and rendering features**:

- palms that are bare poles with small crowns, not full fronded trees
- clouds that are horizontal streaks, not cumulus
- mountains with vertical banding and no ridgelines
- 60k alpha-cut foliage instances at a near-level camera, which is close to
  the worst case that exists for temporal stability

Unity answers exactly that list: real foliage LOD and billboard imposters, a
mature TAA, terrain with proper layering, a huge supply of usable art, and
lighting that does not have to be hand-derived from the tonemap curve up.

**What we lose, and it is not small:**

- The simulation was the part that worked — pure, deterministic, headless,
  100 ms a match, balance-tested across 48 seeds. It is TypeScript and has to
  be rewritten in C#. §4 below treats that as a port, not a redesign, and
  keeps its determinism guarantees.
- The measurement harnesses — capture, measure, sweep, flicker, speckle,
  bench, uiaudit, audio, memory. These caught nearly every real bug in the
  last project. Unity has no equivalent out of the box and §7 rebuilds the
  minimum set.
- The CC0 asset pipeline and `ASSETS.md` ledger.

**The WebGL tax is the other cost.** A Unity WebGL build is heavier than a
hand-written three.js one — a naive build is 30-60 MB and takes 20 s to load.
§6 is entirely about not letting that happen.

---

## 2. Target and budget — decided up front, not discovered

| | target | why |
|---|---|---|
| Platform | **WebGL 2.0**, Unity 6 LTS, **URP** | HDRP does not ship to WebGL. WebGPU in Unity 6 is experimental and not worth the risk yet. |
| Build size | **≤ 25 MB** compressed (Brotli), **≤ 40 MB** total download | Above this the game loses players at the loading bar. The three.js build was 44 MB and that was already too much. |
| First interactive | **≤ 12 s** on a cable connection | |
| Frame | **60 fps at 1080p with 60 men**, never below 45 | §2 of the brief, unchanged. The three.js build ended at 58 fps and missed it. |
| Memory | **≤ 512 MB** WebGL heap | WebGL2 on 32-bit heaps; overrunning this is a crash, not a slowdown. |
| Draw calls | **≤ 400** | WebGL2 draw calls are far more expensive than native. This is the number most likely to be the real limit. |
| Triangles | **≤ 3 M** visible | An order of magnitude under the three.js build's 24 M, deliberately — see §6. |

These are not aspirations. §7 builds the harness that prints them and fails
the build when they are missed.

---

## 3. The map, in detail — lanes and everything on them

The brief's §5 shape, restated concretely so it can be built rather than
interpreted.

### 3.1 The axis

The playfield runs along **X**. The camera is fixed in pitch, sits at eye
height, pans along X and dollies slightly in Z. It never orbits — the
composition is authored and an orbit control lets the player break it in one
drag. This was right in the three.js build and carries over unchanged.

- Playable extent: **X ∈ [−90, +90] m**. (The three.js build used ±45 after
  discovering that 240 m is six frames wide at a 19° lens. 180 m is a
  compromise: enough to flank, small enough to read.)
- Camera: `y = 5.1`, `z = 44`, **19° vertical FOV**, aimed 0.275° below level.
  These are measured against `TARGET.jpg` and should be treated as given.

### 3.2 The two lanes

§5 says two lanes and a track between them. Not three.

| lane | z | what it is | cover character |
|---|---|---|---|
| **Near lane** | `z = +7` | The firebase's own ground. Dug in and built up. | Trenches, sandbag revetments, bunkers. High quality, high capacity. |
| **The track** | `z ≈ 0` | *Not a lane.* A dirt road running the length of the map. It is where **crossing between lanes** happens — a gap in the wire, a dip in the bank. Crossing it is exposed. | None. That is the point. |
| **Far lane** | `z = −6` | The track's far verge and the treeline's edge. | Craters, banks, fallen timber. Low quality, what the fighting left. |

Behind the far lane the ground rises into the **treeline** (`z −25 → −120`)
and then **mountains** (`z < −400`). Neither is playable; both are most of the
picture.

**The lanes must be visible as lanes.** In the three.js build the two lanes
were a `z` value and nothing else — the player could not see where one ended.
Here: the near lane is bounded by the firebase revetment line and the wire;
the far lane by the treeline edge. The track between them is a real dirt
surface with ruts. A player should be able to point at the screen and say
which lane something is in.

### 3.3 Cover

Cover is the vocabulary of the ground and **what you see must be what shelters
you** — the same object drives the sim's cover list and the renderer's
geometry. In the three.js build the sim generated 16-22 pieces of cover and
*none of it was ever drawn*; squads crossed forty metres under fire to take
cover in visibly open grass. That must not recur, so cover is authored as
prefabs with a `CoverVolume` component that registers itself with the sim at
load. There is no second source of truth.

| kind | quality | capacity | where |
|---|---|---|---|
| Trench | 0.58–0.74 | 1 man / 1.8 m | near lane |
| Sandbag revetment | 0.48–0.64 | 1 / 2.1 m | near lane |
| Bunker | 0.70–0.82 | 1 / 3.4 m | near lane, few |
| Crater | 0.34–0.52 | 1 / 2.6 m | far lane, track edge |
| Bank / berm | 0.25–0.44 | 1 / 2.4 m | far lane |

Vegetation does not grow over cover. A parapet with grass through it hides the
one thing the player most needs to read.

### 3.4 What stands on the map

Drawn from `TARGET.jpg`, which is the specification:

- **Firebase (left):** watchtower, sandbag revetments, M35 trucks, an M151
  jeep, ammo crates, fuel drums, a flag, concertina wire.
- **The track:** curving, rutted, with a visible dirt surface and spoil banks.
- **Vegetation:** bamboo thickets, banana, elephant ear, areca and coconut
  palms, tree fern, broadleaf understory, elephant grass.
- **Treeline:** a layered, clumpy canopy — *not* a uniform wall, which was the
  single most-repeated mistake in the last build.
- **Mountains:** three to five ridges with real silhouette and internal
  detail, hazed by distance.

---

## 4. The simulation — ported, not redesigned

This is the part that worked and it is ported as directly as C# allows.

**Non-negotiable properties, carried over:**

- `Assets/_Project/Scripts/Sim/` compiles in its own assembly definition with
  **no reference to UnityEngine rendering, no `MonoBehaviour`, no `Time`, no
  `Random`.** Plain C#. There is an editor test that asserts this.
- A match is a **pure function of (seed, plan, orders)**. Same inputs, same
  match, byte-identical, in the Editor and in a headless test run.
- Fixed **20 Hz** tick. The view interpolates between ticks and never steps
  the simulation from a frame callback.
- Deterministic RNG (mulberry32, ported), with **named forks** — the view
  takes its own fork so that the number of cosmetic effects drawn in a frame
  cannot shift the simulation's stream.

**Systems, all ported:** squads with formation slots recomputed from the live
roster; two lanes; cover with capacity and crowding; ranged-in timers;
suppression and pin (the core mechanic, and the only thing in the sim with no
hit points behind it); bounding; morale as the clock; command points;
concealment; veterancy; reinforcement.

**Ported *with* the fixes the last build earned:**

- `moveMan` moves the squad **anchor**, not every man to the same cover point.
  (Sending each man individually collapsed the squad onto one spot and tripped
  its own crowding penalty; measured, `+cover` took a side from 54% to 27%.)
- `reanchor` must not overwrite `anchorZ` — it erased the z of a march to
  cover and squads drifted out of their lane.
- The bounding branch: `swept && !covered` must not send exposed squads
  *across* swept ground.
- `fire()` sets a cooldown even when it finds no target, or every living man
  rescans every tick for ever. This was 55% of sim runtime.

**New in this version:** `playerOrder` is part of the squad from the start,
not bolted on. Four orders — advance, hold, bound, fall back — plus null for
"the plan decides". A broken squad does not take orders.

---

## 5. Gameplay — what the player actually does

The three.js build's fatal gameplay flaw: the player's entire agency was
panning the camera and buying cards, and **eight of the eighteen cards did
nothing at all** — they took the command points, set a cooldown and returned.
Everything below is specified so that cannot happen: *every* control must
change the simulation, and §7's audit proves it every build.

### 5.1 Command

- **Select** a squad by clicking any of its men. Tab cycles own squads
  nearest-first and moves the camera to them. Escape clears.
- **Order** with 1–4 (advance / hold / bound / fall back), 0 for auto.
- Selection is drawn as **camera-facing rings at chest height**. Not on the
  ground: the camera is at eye level and a ground-plane marker is seen
  edge-on and collapses to nothing. This cost an hour last time.
- The order bar shows the **squad's state**, not the click history. A squad
  the plan sent to ground shows HOLD in outline; one the player ordered there
  shows it filled.

### 5.2 The deck

Command points accrue; cards spend them. Two decks, one per side, and they
are *not* mirrors — the US buys fire support, the VC buys traps and tunnels.

| | US | VC |
|---|---|---|
| Line | Rifle squad, Weapons squad | Guerrilla cell, NVA squad |
| Support | M60 team, Mortar team | RPG team, Marksman |
| Special | Engineers | Sapper |
| Call | Artillery, Smoke, Medevac, Air strike | Punji pit, Tripwire, Spider hole, Tunnel |

**Every CALL card is an area effect with a real mechanic**, specified now
rather than left as a name:

- **Barrage** (artillery, air strike) — salvos walking across a zone on a
  timer. Cover counts heavily against it, which is what makes a trench the
  answer and standing still the mistake. Artillery is wide, long and not very
  lethal per round; the air strike is short, tight and murderous.
- **Smoke** — breaks firing lines and kills nobody. Checked in target
  selection, not in the hit roll: a man who cannot see a target should look
  for another one, not shoot at this one badly.
- **Trap** (punji, tripwire, spider hole) — springs on the first enemy inside,
  pins everyone near it, then is gone.
- **Medevac** — the only card that buys back something already lost. Steadies
  the line and lifts morale. It does not heal, because the sim has no hit
  points and a mechanic with nothing under it is worse than no mechanic.
- **Tunnel** — ignores the spawn edge. That is the whole of its advantage.

Called-in effects land **where the camera is aimed**, in the lane where the
enemy actually is near that x.

### 5.3 Gunplay

Every shot is visible or the firefight reads as men standing still:

- **Muzzle flash** at the shooter, ~0.15 s, ~1.7 m of soft glow. Authored
  against the *grass height*, not against realism — a realistic 0.4 m flash
  at 75 ms is alive one frame in five and lost in the blades in front of it.
- **Tracer** — a short streak that *travels*, not a beam between two men. The
  full shooter-to-target line drawn at once reads as a laser.
- **Impact dust** at the receiving end.
- **Burst** for shells: a large flash plus a rising column.
- Tracers are colour-coded by side, which is the only cue on screen that says
  which way the fire is going.

### 5.4 Movement

Called out separately because the owner named it. Men must *read* as moving
under fire, not slide:

- Walk cycle phase advances with **distance covered**, not time, or feet skate.
- Three postures — standing, crouched, prone — with asymmetric blends. Going
  down is fast (0.20 s), getting up is slow (0.62 s).
- Pin drives posture. A pinned man goes flat and stops advancing; that is the
  visible form of the core mechanic.
- Death is a fall with an accelerating curve, per-man roll direction.
- Bodies persist and are pooled.

---

## 6. Making it light enough for the web

This is the section that decides whether the project ships.

**Rendering**

- **URP Forward+**, no deferred. Post stack kept to: TAA, a bloom with a tight
  threshold, tonemapping, vignette, and a **static, zero-mean** film grain.
  Animated grain re-randomises every frame over an unchanged image and was the
  single largest source of shimmer in the last build.
- **No real-time shadows from vegetation.** One directional light with a
  cascade over the play area only; everything else baked.
- **Baked lighting** for the firebase and terrain; light probes for characters.
- Reflection probes: one, baked.

**Geometry**

- Foliage is **GPU-instanced with LODs and billboard imposters** past ~60 m.
  This is the single biggest thing Unity gives us that three.js did not.
- The treeline beyond ~120 m is **imposter cards**, not geometry.
- Mountains are a single low-poly mesh with a hand-painted normal map.
- Target ≤ 3 M visible triangles against the last build's 24 M.

**Textures**

- **Crunch/DXT compressed**, max 1024 for hero surfaces, 512 for everything
  else. The last build carried 23 unique 1024² RGBA sources at 186 MB decoded,
  of which 63 MB was the same twelve images loaded twice because two GLBs each
  embedded their own copy. Unity's importer dedupes by asset, which removes
  that class of bug entirely — but the resolution budget still has to be held.
- Normal and ORM maps at **half** the albedo's resolution. At this camera
  distance nobody can tell.

**Loading**

- **Addressables** with the firebase and near lane in the initial bundle and
  the treeline streamed.
- Brotli compression, `Decompression Fallback` off.
- Strip engine code: `Managed Stripping Level = High`, IL2CPP, no unused
  modules (physics is not used — see §4, the sim has its own).

---

## 7. Measurement — rebuilt, because it is what actually worked

The three.js project's harnesses caught nearly every real bug in it. The same
set, in Unity terms. **These come first, before content, exactly as §10 of the
brief says — and in that project two of them were advertised in `package.json`
for several sessions before they were written.**

| harness | what it answers | form |
|---|---|---|
| `SimTests` | Is a match a pure function of (seed, plan, orders)? Every event kind and every ending reachable? | Unity Test Framework, EditMode, headless in CI |
| `SimBench` | How long does a match take with no renderer? | EditMode test, prints ms |
| `FrameCapture` | Deterministic frame from a fixed camera and seed | Editor script → PNG |
| `LookMeter` | The §4 band table **and the whole-frame histogram** | C# image analysis over the capture |
| `Flicker` | Frame-to-frame difference of a scene holding still | Two captures, differenced |
| `Budget` | Draw calls, triangles, build size, heap — against §2 | Profiler API, fails the build |
| `UIAudit` | Does every registered control actually change? | Play-mode test driving every control twice |

**`LookMeter` must report the whole-frame histogram from day one, with equal
weight to the bands.** In the three.js build the four band means were inside a
point and monotonic while the frame read flat and lifeless, because dark pixels
were 13.5% against a reference 18.2%, p98 was four low, and standard deviation
and range were both short. A band mean cannot express flatness. That lesson
cost the whole project.

---

## 8. Assets — CC0 only, logged, and *looked at*

Carried over from the brief's §1.2 and not relaxed: **CC0 only, reputable
sources only, every one logged in `ASSETS.md` with its licence, source URL and
date. If the only thing that fits is not CC0, stop and ask the owner.**

**Trusted sources, in order of preference:**

| source | what for | licence |
|---|---|---|
| [Poly Haven](https://polyhaven.com) | HDRIs, ground and bark textures, some props | CC0 |
| [ambientCG](https://ambientcg.com) | PBR materials — earth, sandbag, canvas, metal | CC0 |
| [Quaternius](https://quaternius.com) | Low-poly vegetation and props, ideal for the web budget | CC0 |
| [Kenney](https://kenney.nl) | UI, prototyping | CC0 |
| [Freesound](https://freesound.org) (CC0 filter only) | Ambience, weapon layers | CC0 (filter required) |
| [OpenGameArt](https://opengameart.org) (CC0 filter only) | Gap-filling | CC0 (filter required) |

**Rules learned the hard way in the last build:**

1. **Look at a texture before using it.** The first pick there was blue dotted
   shirting for fatigues and patterned red upholstery for boots, chosen by
   asset ID.
2. **Check the geometry, not the label.** A Poly Haven "1k" tree had a 90 MB
   `.bin`: their resolution tiers size the *textures*, not the mesh.
3. **Reject on style, not only on licence.** Quaternius is CC0 and correctly
   licensed, and stylised low-poly next to a photogrammetry scan reads as two
   different games. Pick one visual family and hold it. *For a web build the
   stylised family is probably the right choice and that is a decision for the
   owner, not for me — see §11.*
4. Audio is **synthesised, not sampled**, unless the owner prefers otherwise.
   "A gun sounds different at 30 m and 300 m" is a propagation problem — travel
   delay, air absorption, spreading, scattered tail — and a recording arrives
   with a room and a distance already baked in. It also means no audio asset
   to license.

**No asset is downloaded without asking the owner first.**

---

## 9. Findings carried over from the three.js build

The expensive lessons, kept because they are engine-independent.

1. **An instrument with no proven zero is not an instrument.** Three flicker
   diagnoses in a row were wrong because a harness silently dropped `--wind 0`
   (`'0'` is falsy in JS). Every "off" run still had wind on. The fix was
   rendering the same scene twice with everything off until the number read
   **0.000 at a known-good mean luma** — a floor *and* a sanity reading, so a
   broken render cannot score a perfect zero.
2. **Vary one thing at a time, from a baseline known to be still.** Every
   wrong diagnosis came from changing several flags and crediting whichever
   was on my mind.
3. **A band mean cannot see flatness, aliasing, or silhouette.** A hole and a
   mass average alike. Trust the difference image and the histogram.
4. **Measure in the space you apply in.** A contrast pivot was set from the
   §4 p50 converted to linear, but the post chain runs *before* exposure is
   applied — so the right pivot was that number divided by 3.2. Two wrong
   pivots came from measuring in the output space and applying in the input
   space.
5. **A thing can be in the frame and not in the picture.** The dirt track was
   correct, in the right place, and a few pixels deep behind the near grass.
   The cover was generated for months and never drawn at all.
6. **Effects must be sized and timed against the camera**, not against
   realism. Realistic is frequently invisible.
7. **Dead controls are the default failure.** The old 2D game shipped a dead
   message, a dead timer and a dead win condition; this one shipped eight
   cards that did nothing and a UI audit that cleared a button it never
   clicked. Audit by driving every control and asserting it changed.
8. **Authoring by eye without reference to the exposure curve.** Colours were
   repeatedly chosen that sat past the tonemap knee and saturated to white
   however they were authored.
9. **The sim must not be perturbed by the renderer.** Cosmetic randomness gets
   a forked stream or a browser run and a headless run stop being the same
   match.
10. **Two GPU textures per image is the default, not the exception**, unless
    something dedupes by content.

---

## 10. Execution order

Nothing here starts before §0 is done.

| # | step | gate |
|---|---|---|
| 0 | Unity installed; project opens; empty scene builds to WebGL | **Build size of an empty URP scene recorded.** This is the floor everything else is measured against. |
| 1 | `Sim` assembly ported; tests green; determinism asserted | `SimTests` passes headless |
| 2 | Harnesses: `FrameCapture`, `LookMeter`, `Budget` | They run and print numbers |
| 3 | Grey-box map: terrain, two lanes, the track, cover volumes | Lanes readable in a grey-box capture |
| 4 | Sim drives grey-box men at 20 Hz with interpolation | 60 men inside frame budget, grey-box |
| 5 | Command: selection, orders, camera | `UIAudit` green |
| 6 | Art pass: vegetation, firebase, treeline, mountains | §4 band table **and** histogram |
| 7 | Gunplay: flashes, tracers, impacts, bursts | Visible in a capture, not just spawned |
| 8 | Deck: every card does something | `UIAudit` proves all 18 |
| 9 | Audio | Distance model measured |
| 10 | Polish, and a build a stranger can load | All §2 budgets |

---

## 11. Questions for the owner

1. **Visual family.** Photoreal-ish (Poly Haven / ambientCG scans, heavier,
   closer to `TARGET.jpg`) or stylised low-poly (Quaternius, far lighter, ships
   comfortably to the web, reads as a different game)? This decides the whole
   art pipeline and I should not pick it alone. `TARGET.jpg` argues for the
   first; the 25 MB budget argues for the second. **A middle path exists —
   stylised geometry with scanned PBR materials — and is my recommendation.**
2. **Unity version.** Unity 6 LTS unless you have a reason to prefer 2022 LTS.
3. **Audio** — synthesised as before, or sourced CC0?
4. Is the ±90 m playfield right, or do you want the larger 240 m map back?

---

## 12. Standing rules

- CC0 only. Nothing downloaded without asking.
- The simulation imports nothing from the renderer. There is a test.
- Never push or deploy without asking.
- Measure before claiming. Nothing is done until it has run in a browser.
- Kill every dev server, watcher and background job when stopping.
