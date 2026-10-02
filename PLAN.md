# Lanes of Vietnam '65 — Unity / WebGL

A rebuild of the three.js project in Unity, targeting the browser.
`reference/TARGET.jpg` is the specification for the look. `Docs/BUILD_BRIEF.md`
is the original brief and still governs everything it covers.

**Status: §10 steps 0 and 1 done and measured** — see §10a. The project builds
to WebGL, and the simulation is ported and proven exact against the original.

---

## 0. Toolchain — installed and verified

**This is done.** Checked on 2026-09-29 from the terminal, not assumed:

| | |
|---|---|
| Unity Hub | `/Applications/Unity Hub.app` — 3.21.3 |
| Editor | **6000.6.3f1** (Apple silicon) |
| Web Build Support | **installed** (`modules.json`, `webgl: selected`) |
| Documentation | installed |
| Homebrew | 7.0.3, `unity-hub` cask available if a reinstall is ever needed |

**The project compiles.** Run headless:

```bash
"/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -nographics \
  -projectPath "$(pwd)" -logFile /tmp/unity.log
```

Exit code 0, **0 compile errors**, on 2026-09-29 with `Sim/{Rng,Types,Tune,
Combat,Squads}.cs` in place. This matters more than it looks: it means the
verification loop this project needs *exists*. Unity can be driven from the
CLI, so the same discipline that found the real bugs in the three.js build —
build it, run it, measure it, believe the number — carries over.

### Two things learned installing it

**The editor is 6000.6.3f1, which is a tech-stream release, not LTS.** It
works, and it is what is installed, so it is what this project targets. The
risk is package churn, and it bit immediately — see below. If that becomes a
recurring cost, installing 6000.0 LTS alongside it is a Hub checkbox and the
project version file is one line.

**Correction (2026-09-30): Input System and Addressables *do* compile on
6000.6.3f1** — at the versions this editor recommends (Input System 1.20.0,
Addressables 2.11.2, from the editor's own package manifest). The scaffold had
pinned older releases, and it was *those* that call APIs 6.6 made
obsolete-as-error. Measured in a scratch copy: both packages compile and all
tests pass, on 6.6 and on 6.3 LTS alike. The test-framework and Rider packages
are now pinned at this editor's recommended versions too. See §12.9a for the
editor decision this fed.

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
| Build size | **≤ 80 MB** total download, **≤ 45 MB** initial | Revised upward — see the note under this table. |
| First interactive | **≤ 12 s** on a cable connection | |
| Frame | **60 fps at 1080p with 60 men**, never below 45 | §2 of the brief, unchanged. The three.js build ended at 58 fps and missed it. |
| Memory | **≤ 512 MB** WebGL heap | WebGL2 on 32-bit heaps; overrunning this is a crash, not a slowdown. |
| Draw calls | **≤ 400** | WebGL2 draw calls are far more expensive than native. This is the number most likely to be the real limit. |
| Triangles | **≤ 3 M** visible | An order of magnitude under the three.js build's 24 M, deliberately — see §6. |

**The build-size number moved, and it should be understood rather than
filed.** It was 25 MB. The owner has chosen the photoreal direction — §11 Q1,
answered 2026-09-29 — and 25 MB does not survive contact with scanned
vegetation and 2K PBR materials. Pretending otherwise would mean discovering
it at the end, which is exactly how the last project ran out of road.

So the budget is honest instead: **45 MB initial, 80 MB total**, with the
treeline and the deep background streamed after first interaction. That is a
real cost — a slower first load than a hand-written WebGL scene — and it is
the price of the look that was asked for. The mitigations in §6 are what keep
it from being worse, and every one of them is measured by `Budget` in §7.

The frame, draw call and memory numbers are unchanged and are not negotiable:
they are what decides whether it *runs*, where the size decides only how long
someone waits. These are not aspirations. §7 builds the harness that prints
them and fails the build when they are missed.

## 3. The map, in detail — lanes and everything on them

The brief's §5 shape, restated concretely so it can be built rather than
interpreted.

### 3.1 The axis

The playfield runs along **X**. The camera is fixed in pitch, sits at eye
height, pans along X and dollies slightly in Z. It never orbits — the
composition is authored and an orbit control lets the player break it in one
drag. This was right in the three.js build and carries over unchanged.

- Playable extent: **X ∈ [−45, +45] m for now** — the three.js value, kept by
  the port. This section proposed ±90 as a compromise, but every balance
  number carried over was measured at 45, and a longer map is more walking in
  a game where men already spend ~77% of their time moving with no enemy in
  range (brief §9 finding 6). Widening it is a measured experiment for later,
  not a default. (240 m was six frames wide at a 19° lens.)
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

## 8. Assets — free, highest quality, reputable, logged, and *looked at*

**Changed by the owner on 2026-09-29, superseding the brief's §1.2 and §6
CC0-only rule.** The owner's instruction: *pick the assets yourself — they
must be of the highest quality, from reputable and trusted sources, and
free.* So:

- **Free and reputable, not CC0-only.** CC0 is still preferred when two
  candidates are equal, because it carries no obligations. A free asset
  under another licence (CC-BY, a vendor's royalty-free terms) is acceptable
  when it is clearly better, and its obligations — attribution, no
  redistribution of the raw files — are recorded in `ASSETS.md` and met.
- **Asset selection is delegated.** The old rule "no asset is downloaded
  without asking the owner first" no longer applies. What still applies:
  every asset is logged in `ASSETS.md` with its licence, source URL, date and
  what it is used for, and anything whose licence is unclear is not used.
- **Adobe tools are in scope and can be used** (owner, 2026-09-29). The owner
  has an Adobe ID and Creative Cloud on this machine. That puts in reach:
  - **Mixamo** — Adobe's free character rigging and animation library,
    royalty-free for commercial games. With Unity's Humanoid retargeting it is
    the answer to the brief's hardest ask, "the soldier moves": locomotion,
    posture transitions, weapon handling, reactions and deaths as real motion
    rather than hand-keyed approximations.
  - **Photoshop 2026** (installed, scriptable) — authoring and cleaning
    textures, leaf and foliage atlases, card portraits, UI art.
  - **Premiere Pro / Media Encoder 2026** (installed) — audio and video
    processing if it is needed.
  - Anything else in the owner's Creative Cloud entitlement, installed on
    request.

**Trusted sources, in order of preference:**

| source | what for | licence |
|---|---|---|
| [Poly Haven](https://polyhaven.com) | HDRIs, scanned models, ground and bark textures | CC0 |
| [ambientCG](https://ambientcg.com) | PBR materials — earth, sandbag, canvas, metal, leaves | CC0 |
| [Mixamo](https://www.mixamo.com) (Adobe) | Humanoid animation, rigging | Adobe royalty-free terms: use in games, no redistribution of raw files |
| Adobe Photoshop | Textures and atlases authored here | our own work |
| [Kenney](https://kenney.nl) | UI, prototyping | CC0 |
| [Freesound](https://freesound.org) | Ambience, weapon layers | per-file; CC0 preferred, CC-BY logged with attribution |
| [OpenGameArt](https://opengameart.org) | Gap-filling | per-file; checked and logged |
| [Quaternius](https://quaternius.com) | Low-poly props | CC0 — stylised, so only where style does not clash |

**Rules learned the hard way in the last build:**

1. **Look at a texture before using it.** The first pick there was blue dotted
   shirting for fatigues and patterned red upholstery for boots, chosen by
   asset ID.
2. **Check the geometry, not the label.** A Poly Haven "1k" tree had a 90 MB
   `.bin`: their resolution tiers size the *textures*, not the mesh.
3. **Reject on style, not only on licence.** Quaternius is CC0 and correctly
   licensed, and stylised low-poly next to a photogrammetry scan reads as two
   different games. Pick one visual family and hold it. The owner chose the
   photoreal family (§11 Q1), so stylised packs are out wherever they would
   stand next to scanned material.
4. Audio is **synthesised, not sampled**, unless the owner prefers otherwise.
   "A gun sounds different at 30 m and 300 m" is a propagation problem — travel
   delay, air absorption, spreading, scattered tail — and a recording arrives
   with a room and a distance already baked in. It also means no audio asset
   to license.- YOU SHOULD SOURCE IT!
5. **Log it the moment it lands.** Licence, source URL, date, sha256, what it
   is used for, and any obligation the licence carries. An asset that is in
   the project and not in `ASSETS.md` is a bug.

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

## 10a. Progress — what is done, and the number that says so

| step | gate | measured |
|---|---|---|
| 0 | empty URP scene builds to WebGL; size recorded | **8.00 MB initial** (wasm 4.95, data 2.97, framework 0.06, loader 0.03 MB, Brotli), 126 s build. `Builds/empty-floor/size.json`. The floor everything else is measured against. |
| 1 | `SimTests` green headless; determinism asserted | **30/30** under Unity's EditMode runner, 25 s headless. |
| 5 | command: selection, orders, camera, field glasses — `UIAudit` green | **3/3** (`Tests/PlayMode/UIAuditTests.cs`): click-select (and a click on sky selects nothing), Escape, Tab twice with the camera following, all four orders and auto read back from the *simulation's* squad after the tick that applies them, the ring colour showing who decided the order, pan, dolly, and the glasses up and down. Its first run caught two real bugs, both of the "dead HUD button" family: the input layer overwrote the glasses' state every frame, and the rig read the mouse itself and overrode any other aim. |
| 3 | grey-box map: lanes readable in a grey-box capture | met: `captures/lanes*.png`. The lanes are bounded as §3.2 says — the firebase's sandbagged front and the wire (gapped at the bank's cuts, the crossings) behind the near lane, the dirt track, scrub, then the treeline's ragged edge behind the far lane, ridges beyond. **91-118 draw calls, ~0.9 M triangles** with the whole grey-box dressing merged per material. LookMeter baseline (flat colours, expected to be wrong): mean L\* 26.7, near lane 24.4 against 31.2, no highlights. |
| 2 | harnesses run and print numbers | `tools/capture.sh` renders the real game at a frozen moment in **7-9 s**; `lookmeter.py` reproduces the brief's band table off TARGET.jpg within **1.24 L\*, 1.10 S**; `flicker.py`'s zero is proven (**0.000** at mean L\* 51.5, AA off); `budget.py` checks §2 and exits non-zero on a miss. |
| 3a/6c (interim) | soldiers | The earlier build's rigged MPFB2 soldiers, **baked as posed sprites** (stand, six-frame walk, kneel, prone, two dead), lit like the plants. Each man is one quad; the frame comes from posture, and when standing from metres walked (1.45 m stride, so no foot skate). US face right, VC mirrored left; 2 draw calls. The US uniform is re-coloured to OG-107 olive (it baked at albedo 0.008). **An interim, and the owner's call:** it sidesteps §12.3's WebGL skinning risk, but it is not the §12.3 soldier. Mixamo is still blocked on the browser. Flicker 0.65; build **17.24 MB**. |
| 3a (started) | soldiers, 3D | **Skinned men on a Humanoid avatar**, in the game (`SoldierFigure`, `SoldierBuilder`, `tools/blender/soldier_rig.py`). The MPFB2 soldiers keep their rifle-holding bind pose; the avatar is built from an enforced T-pose, so T-posed clips (Mixamo's) retarget. One atlas material, 7k triangles, rifle rigid on the hand. Animator: stand (idle/walk/run by speed, the clip sped to the man so feet do not skate), crouch, prone, transitions at §5.4's 0.20 s down / 0.62 s up, two deaths, an upper-body aim layer (a man aims while walking). Stepped by hand on match time (still when paused, 3x at 3x, settled after a jump). After the Animator: recoil on every sim shot, the support hand on the handguard by IK, the man turned to the man he shoots at. The dead drop their rifles and are baked to plain meshes. Clips are interim (the sprite bake's poses and CMU mocap) until Mixamo. **Measured in a WebGL build** (headless Chrome, ANGLE/Metal, M4 Pro, `?perf=1` against `?perf=sprites`): **~0.5 ms a frame** for 20-33 live men (Animators 0.2-0.3 ms), inside the 4 ms sub-budget; mitigations 2-3 in: men under 110 px stepped every other frame on 2-bone skinning, men off screen every fourth. Zooming now tilts the camera to keep the lanes where the authored view has them (the men had dropped out of the frame). Build **25.70 MB** (sprites still inside as the fallback). |
| 3a (Mixamo) | soldiers, Mixamo motion | **All 44 Mixamo clips the game uses, on our own soldiers** through Humanoid: stand (idle, walk 1.60, run 4.00 m/s), crouch (kneel idle, crouched walk 1.73), prone (idle, crawl 0.20), five posture-change clips sped to §5.4's gates (0.45-0.66 s), 8 standing deaths plus deaths chosen by how he died (running, a blast with no shot before it, kneeling, prone), aim layer per posture, a React layer (flinch when pinned, kneel and prone hits, reloads in a lull, the grenade toss). Fixed on the way: "Original" root orientation turned Mixamo's men 90 deg left (now body orientation offset by each clip's measured blade); fingers unmapped (curls tore the hands); the rifle in Mixamo's grip, learned from the aiming idle, laid along the hands while aiming, along the forearm while crawling; each posture's aim turns him by its measured offset (24/43/27 deg) so the rifle points at his target. 0.58 ms a frame for 37 live men in the Editor. |
| 3a/6c (playtest 3) | soldiers rebuilt, sandbags as geometry, flicker | **Bodies:** six MPFB2 men (three a side) with skeletons fitted to them, hair under the helmets, the M1 raised 3 cm; necks and legs whole in every Mixamo pose (`captures/soldiers/*_close.png`). **Sandbags:** `tools/blender/sandbag_mesh.py` builds 2.4 m runs of real bags (7 courses 1.15 m, 4,608 triangles; 3 courses 0.49 m, 2,016), one 2048 atlas weathered from the wall's own occlusion, sky-facing and height bakes; the cards and their z-fighting are gone, and the firebase's east wall runs away from the lens. **Flicker, found:** it was TAA's jitter on a still lens. Firebase view, wind and men moving at 60 fps, mean \|dL\*\|: sandbag wall **2.07 -> 0.004**, foreground grass 0.53 -> 0.17, whole frame **0.79 -> 0.08**, by changing TAA (Very High, jitter 0.35) for SMAA High; no TAA setting came near (blend 0.95, jitter 0.15: 1.05 on the wall). The hessian weave was dropped for its 40-400 texel band: threads under two pixels beat against the pixel grid. EditMode 40/40, PlayMode 13/13. WebGL **29.99 MB** initial; real Chrome (ANGLE Metal, M4 Pro) **6.5-7.2 ms** a frame, median 7.0, with 56-67 men; army view 0.36-0.41 ms. |
| 11 (fieldcraft) | Part 2 sim extension behind `MatchOptions.Fieldcraft`; parity baseline unchanged; balance block; audit; UIAudit row | **Men who fight as a squad** (`Sim/Fieldcraft.cs`, §12.15): files and ranks, places in cover (single file down a trench), a pause in cover, a stand-off from an enemy in sight, the assault when he is pinned or outnumbered, the charge inside 6 m and **melee** inside 2 m, fear when a man falls beside you, **a round that carries on through the man it killed**, climbing in and out of trenches, positions that change hands, and **Warfare 1944's lever** on every trench, wall and bunker (Hold / Go / Auto; a plate over each, keys H and G). As the game plays (map, grenades, smoke, drill), six seeds, per minute of fighting, baseline -> fieldcraft: men walking through a living enemy **4.2 -> 0.0**; man-seconds within 3 m of an enemy 16.2 -> 0.7; man-seconds within 0.6 m of a friend **177 -> 23**. Balance, 48 seeds on the map: US 62.5% (CI 48.4-74.8) without, **54.2% (CI 40.3-67.4)** with; match 144 s -> 160 s; per match 2.7 blows hand to hand, 115 climbs, 0.7 positions taken, 10.8 rounds through a man. View: a man faces the enemy and turns to his travel only when he is going somewhere (backward clips for the rest), a lunge for the blow, blood thrown out along the round and left on the ground. EditMode **45/45** (22 parity matches and earlier pins unchanged, two new pins), PlayMode **14/14**. WebGL 30.09 MB; real Chrome 7.1-7.2 ms a frame. Still to come: Mixamo clips for the climb and the blow, trenches rebuilt to climb into. |
| 11 (fieldcraft, strongpoints) | the owner's correction; clips sourced; behaviour re-measured | **Every piece of cover is a strongpoint** with a lever and a plate, built or natural (17 on the map; the three bank-to-trench conversions tried first are dropped). Files moved off the cover line and new squads arrive closed up and on the map: per minute of fighting, six seeds, baseline -> now: men walking through a living enemy 4.2 -> **0.0**; man-seconds within 0.6 m of a friend 177 -> **3.0**; squadmates changing sides (clear by 0.3 m each way) 5.8 -> 6.3; a man's travel swinging over 120 degrees 18.5 -> 25.4 (falling back, and going round a pinned man: no rule against either). Balance, 48 seeds on the map: US **47.9%** (CI 34.5-61.7), 152 s; per match 3.9 blows, 95 climbs, 3.2 strongpoints taken, 9.2 rounds through a man. **Mixamo clips in** (owner's OK): Bayonet Stab and Slash Advance for the blow, Jump Down and Climbing Up Wall for the trench; a man in a trench is drawn on its line and carried over its lip while he climbs. EditMode **45/45**, PlayMode **14/14**. WebGL 30.25 MB; real Chrome 7.7-7.8 ms a frame, median 8.0. |
| 11/12 (arms) | weapon classes behind `MatchOptions.Arms`; the sniper class; the models; balance; audit; UIAudit | **Every man carries a weapon, every card buys its squad, every fight is at its weapons' distance** (`Sim/Arms.cs`, §12.16). Eleven kits (the new US **SNIPER TEAM**; the VC marksman is a sniper), ten weapons with their own range, rate, accuracy, suppression; launchers and the mortar fire bursting rounds. Distances fit the frame (23 m of the near lane at its widest): rifles 20 m (the baseline: 28 for everyone), machine guns 28-30, SMGs 15, snipers 44; squads close to their kit's distance (sappers 6 m, rifles 13, MG teams 20, snipers 32). Measured over 24 matches: rifle shots at a mean 16.4 m, machine guns 20.6, snipers 27.2, SMGs 11.9; kills a man carried: rifles 0.28-0.34, M60 0.75, RPD 0.89, sniper 2.11, RPG 1.58, M79 0.66, SMG 0.07. Balance, **96 seeds** on the map: US **51.0%** (CI 41.2-60.8), 156 s (first numbers gave 89.6%). **Twelve weapon models built in Blender** (`tools/blender/weapons.py`, ASSETS.md) and put in each man's hands by his weapon; a Mosin-Nagant recording for the snipers; rockets, M79 grenades and mortar bombs drawn in flight. A man shot falls the way the round pushes him (each death clip's direction measured). EditMode **49/49**, PlayMode **14/14**. WebGL **29.85 MB**; real Chrome 8.4-8.5 ms a frame, median 8.0. Open: the launchers' and the mortar's own firing sounds are not sourced (the battery's recording, pitched up, stands in); the mortar is carried, not emplaced; SMG men rarely get to fire. |
| 11 (playtest 4) | "still a little clunky and do random things. Points are gained too slow. Too many soldiers at the start" | **Found by measuring** (`simcs muddle`, six seeds, as the game played): a man changed posture **21.2** times a minute (kneeling at every pause of the file), made **15.4** hops of under a second (a step at a time after a slot that drifted), and his squad's decision to close or to assault was retaken every tick. Now: a squad goes to ground only when halted in contact (machine-gunners and snipers flat behind their guns); a man at rest sets off only when his place is 0.9 m off and then goes all the way; the decisions to close and to assault stand for 2 and 3 s; his front has to move 22 degrees before he turns to it. Posture changes **12.2** (4.8 of them going to ground under fire), hops **3.3**, men on top of each other 0.8 man-seconds. Of 7.7 order changes a squad a minute, 4 are Advance<->Bound, which move alike. **Economy:** the player's own side had been run by the computer's plan, which bought it a squad every time it reached 22 points: he could never save for an air strike (40), and men arrived unasked. `MatchOptions.Player` stops that; the game opens with a squad a lane (8+ men a side, was 16+), 20 points in hand, and **1.6 points a second** (was 0.9). Balance after the changes, 96 seeds, computer against computer: US **49.0%** (CI 39.2-58.8); without Arms 52.1%. EditMode **50/50**, PlayMode **14/14**. WebGL 29.85 MB; real Chrome 5.9-6.3 ms a frame, median 6.0. |
| 11 (playtest 4, second batch) | the computer's tempo; sappers; the trench; gunfire heard; a second hat | **The computer's squad costs it 28** at the game's income (`MatchOptions.MusterCost`; the baseline's 22 is at the baseline's 0.9 a second). Chosen with `simcs player 24 N`: a player who buys nothing loses in about a minute at any cost; one who only ever buys line squads wins 12/24 at 22, **17-19/24 at 28**, 24/24 at 40. **Sappers** come up bent double while unseen (found at 16 m, not 26). A squad stays 8 s in a trench it climbed into (3 s in other cover). **Gunfire:** 1/d from 40 m, not 24 (the near lane at full level, the far at four fifths), and no travel delay inside 60 m: a rifle on screen is heard with its flash. **US soldier B** wears the boonie hat in place of the helmet (`soldier_body.py`). Balance, 96 seeds, computer v computer at the baseline tempo: US 43.8% (CI 34.3-53.7). EditMode **50/50**, PlayMode **14/14**. WebGL 29.85 MB. |
| 9 (started) | audio, sourced | **The three.js Web Audio graph ported to a `.jslib`**: 343 m/s travel delay, 20 kHz·e^(−d/120) absorption (cover darkens rather than quietens), 1/d spreading from 10 m, a reverb send that rises with distance into the valley impulse, a 24-voice cap and a limiter. It is fed by **recordings**: CC0 AR-15, AK-47 and SKS shots sliced by onset, and Sonniss GDC 2017 explosions, a flyby crack, a body thump and a Thai jungle bed that ducks under fire (licensed, git-ignored, rebuilt by script). `AudioView` ports the old game layer's measured rationing: 3 new shots a tick, nearest first, 55 ms apart. UIAudit **9/9** covers it, checking that shots are heard, rationed, and come from the firing man's position. Not heard in a browser yet: no browser was connected. Build 18.05 MB initial, 19.28 MB with the streamed audio. |
| 6b (grade) | the §4 table and histogram | **Cloud shadows** (the sun's light cookie, §12.7 item 4; URP cookies turned on) and a **measured grade**. Search, in sum of band \|dL\*\|: start 52.4; cloud shadows 43.2; massif haze 2.5 km 34.5; tone curves alone could not separate lanes from treeline. Lower exposure for the ground (+0.4 EV) with the sky held up on its own (+0.6, a graduated filter), a gentle gamma/gain, saturation −20 and fog 0.0035 give **29.9**. Band L\* 62.1 / 38.8 / 42.8 / 42.1 against 69.7 / 40.2 / 32.8 / 31.2; saturation 11.8 / 12.6 / 15.7 / 21.2 against 11.1 / 16.7 / 21.8 / 33.8; p50 **43.3** against 39.8. What remains is content: the reference's near band is dense dark vegetation with larger men, ours still shows lawn. Flicker **0.42**. |
| 11 (first mechanic) | Part 2 sim extensions behind options; parity baseline unchanged; balance block; audit | Tagged `sim-parity-baseline`. **Grenades** (`MatchOptions.Frag`, default off; the game turns it on). A man 7–20 m from a seen enemy in cover, not pinned and not prone, throws. It scatters with distance, burns a 2.5 s fuse, and is lethal to 5 m and suppressive to 11 m, whoever threw it. Cover barely helps against a grenade inside it; lying prone in the open helps more. Every draw comes from its own fork, and its state is hashed only when the rule is on. **All six TypeScript parity cases still pass.** 48 seeds, ceiling v ceiling: US wins **41.7% (29–56)** off, **52.1% (38–66)** on. About 20 grenades a match cause 4.8 kills (8% of casualties); length is unchanged. EditMode **34/34**: grenade hashes pinned, reachability, off-by-default, and `Frag.cs` added to the no-transcendentals check. `CombatView` draws the lob and the blast. **Squad smoke** (`MatchOptions.SquadSmoke`, default off; the game turns it on): a squad whose bound has stalled (mean pin at the stop threshold) pops one canister 40% of the way toward the nearest seen enemy. It makes no random draws. Tuned by dose: 7 m for 16 s on any advance halved matches (189→84 s), because ground became the main cause of breaking (0.08→0.79 morale). **3.5 m for 8 s on a stalled bound gives 148 s, casualties −5%, US 52% (38–66).** With both rules: 165 s, US 45.8% (33–60), and grenades cause 28% of kills, since smoke brings the lines close. EditMode **37/37**. |
| 7 (started) | gunplay and call-ins visible, each on its event's tick | `CombatView` draws the sim's own events. Every shot gets a muzzle flash; a third are tracers (red US, green VC/NVA); misses kick dirt beside the man missed. Every shell gets a flash, a fireball, thrown earth, a point-light burst and a drifting brown-grey smoke column; smoke screens are white banks. Every effect is a pure function of its event's age in sim time, so captures are deterministic and nothing fires off-tick. Capture keys `call=card:lane:x:tick` and `usplan`/`vcplan` show a barrage on demand (the computer's plans never call one). Flicker 0.60, and 0.38 inside a barrage. |
| 6a/6b (in progress) | sky and ground sourced, measured, logged; lit from the sky | **Sky:** Poly Haven *Sunflowers (Pure Sky)*, CC0. The window is cut at 40 px/degree; the sun and ambient come from the same photograph. Pure sky **L\* 87.6, RGB (209, 222, 230)** against 88.5 and (215, 224, 230) in TARGET.jpg. Exposure **+1 EV**, Neutral tonemapper, white balance 60% toward a grey card. The derived card (1.154:1:0.710) matches the rendered one (1.155:1:0.71). **Ground:** four scanned CC0 layers chosen from 97 measured candidates (ASSETS.md), height-blended, crunched. The ground's download is **3.7 MB**; the WebGL build is **11.93 MB** initial. Near-ground shimmer **0.26** mean \|dL\*\|, whole frame 0.48, under the 1.30 gate. Draws 97, 0.90 M triangles. The band L\* are still 12–25 over target: bare ground is physically brighter than the reference's vegetation-covered ground, which is the next step. **Plants:** eight CC0 Poly Haven species baked by `tools/blender/plant_bake.py` into lit billboards (albedo, normal, occlusion, and cos x visibility under the game's own fixed sun), 1,983 placed by the grey box's rules, 2.3 MB. Treeline **L\* 38.7** against 40.2 (53.5 before the baked sun visibility). Flicker **0.94** worst pair, under 1.30, after a distance-graded mip bias (1.56 without it). **Grass:** elephant grass and tufts built from CC0 scanned blades, 17,159 plants in all. It made TAA boil (flicker 1.93). Very High TAA quality with the jitter at 0.35 holds it at **0.63**, with no stair-steps on edges. **Coconut palms** are built in Blender from the palm_tree_bark scan and Foliage008 leaflets, laid 10–14 m. **Bamboo** is built from the Bamboo001A culm scan and LeafSet013 leaves, with thickets toward the firebase end as in the reference. Banana is not yet in, and the Sketchfab shortlist in ASSETS.md still needs the owner's login. Flicker **0.70** with the grass bias at 1.0, chosen so the blades stay blades. Open: the frame has no deep darks (0.7% below L\* 20, against 18% in the reference), a job for the grade. **Mountains:** the real Chu Pong massif from SRTM, seen from LZ X-Ray on heading 262°, with a skyline of 2–8.3° against the reference's 5–9°. It is a 0.34 M-triangle fan grid built at load, with its own aerial perspective. Build **16.23 MB**; flicker **0.60**. |

**Step 0 found two settings that were never true.** The scaffold described a
URP project, but no pipeline asset was assigned anywhere — it was rendering
with the built-in pipeline — and the colour space was Gamma. Both are now set
from code by `ProjectSetup.Apply`, which also fails loudly on any setting name
that does not resolve.

**Step 1: the port is exact, and that is measured, not claimed.**
`tools/parity/trace.ts` runs the TypeScript original and hashes the entire
simulation state after every tick; `tools/simcs` does the same for the C#.
**22 matches, every tick identical** — plan-driven fights across all four
plans, and six scripted matches that buy every call-in card (barrages, smoke,
all three traps, the tunnel, medevac) and give player orders. The tests pin
hashes from that run, so parity stays checked in Unity without Node.

What exactness needed, each of which would have silently produced a
*different* match:

- **double, not float.** The previous port used float, which cannot reproduce
  a double-precision original: positions round differently and matches
  diverge within ticks. The balance numbers carried over from the three.js
  build were never going to apply.
- **V8's own `Math.hypot`** (normalise, Kahan-sum, scale), which is not
  `sqrt(x*x + y*y)`. 20,000/20,000 test vectors bit-identical.
- **JavaScript's `Math.round`**: C# rounds halves to even.
- **fdlibm sine and cosine**, ported into `JsMath`, instead of `Math.Sin`.
  The Editor's libm and the browser's are different code and may disagree in
  the last bit; one bit moves a shell burst and flips a lethal roll. This
  Node's V8 uses glibc-derived trig, which differs from fdlibm by ≤ 1 ulp on
  0.5% of inputs — so the TypeScript original itself was never deterministic
  across browsers for barrages. The port is, by construction.
- **Draw order inside what were object literals** (a man's z before his
  cooldown; a cover's x, z, then quality).
- **Three porting bugs fixed**: `Relax` had dropped the faster recovery when
  prone; `Cover.RangedIn` was an int, so it could never climb past zero and
  nothing was ever ranged in; `MaxTicks` had moved from 12 to 14 minutes,
  which moves the time endings.

Measured with the port (`tools/simcs/run.sh`):

| | |
|---|---|
| mirror, ceiling vs ceiling, 48 seeds | US 41.7%, 95% CI 28.8–55.7% (fair) |
| ceiling vs floor | 91.7% as US, 72.9% as VC |
| cost | **10 µs per tick**, 37 ms per match; worst 236 ms (a 12-minute defend) |
| audit | all 17 event kinds fire; 5 of 6 endings in 400 matches — "time, drawn" is rare, and a unit test proves its branch is live |

**Step 3: the numbers came from the reference, not from taste.** The first
grey-box treeline filled the frame, because at 57 m the whole frame is 19 m
tall; its height is now derived from TARGET.jpg's own angle (crowns top out
about 3.5° above the horizon, so top = 5.1 m + distance × tan 3.5°), and the
treeline starts at z −24 as §3.2 says. Ridges are sized the same way (peaks
2-6° up). And the firebase moved into the middle distance: in the reference
the near lane's men stand *in front of* the trucks and the tower, and the
first grey-box put a truck between the near lane and the lens.

**Step 2: three instruments that would have lied, caught before use.**

- *Capture that tested nothing.* `-runTests` with `-quit` exits before the
  test runner starts, returns 0 and writes no results. `tools/unity.sh` now
  drops `-quit` for test runs.
- *Capture that waited forever.* Headless play mode has no Game view, so
  `WaitForEndOfFrame` never returns. Frames are now rendered by explicit
  render request, one per frame, so TAA's history advances as in play.
- *A draw-call budget that could never be missed.* "Draw Calls Count" is
  registered under UI Toolkit in this Unity; read under Render it gave 0 for a
  frame with 100,000 triangles. The real total is the sum of the SRP Batcher,
  standard, instanced, BRG and procedural counters. Grey-box frame: **79 draw
  calls, 100k triangles**. (Video-memory reads 38 GB on unified memory and is
  not used.)

TAA on a frozen grey-box scene moves the frame by mean |ΔL\*| 0.07-0.08, with
0.5% of pixels past 3 L\* — silhouette edges under jitter. That is the
temporal-stability baseline the art pass is measured against.

**Changed on purpose, with the reason:**

- **Card cooldowns live in the simulation and tick with it.** The original
  kept them in the UI and ticked them once per *rendered frame*, so at 60 fps
  every cooldown ran out three times too fast, and a headless run could not
  see the economy at all.
- **Player input is a command log.** Orders and purchases are queued and
  applied at a tick boundary, and `LiveMatch.Replay(options, log)` reproduces
  the match exactly (tested). A match is a pure function of (seed, plans,
  commands), as §4 asks.
- **A bought squad's strength is recorded when it arrives**, so it can break
  like any other. The original caught up only at the next reinforcement
  check, up to six seconds later.
- **Ground craters are an input**, not fourteen random holes independent of
  the simulation's crater cover — the map's crater cover and the ground's
  craters will be one list (§3.3's single source of truth).

## 11. Questions for the owner

1. ~~**Visual family.**~~ **Answered 2026-09-29: photoreal.** The owner asked
   for "those OP graphics", which settles it — scanned PBR materials, the
   heaviest vegetation the frame budget will carry, and `TARGET.jpg` as the
   bar rather than a stylised reinterpretation of it.

   The consequence is recorded in §2: the build-size budget moves from 25 MB
   to 45 MB initial / 80 MB total, and §6's streaming stops being an
   optimisation and becomes load-bearing. The frame budget does *not* move,
   which means the photoreal look has to be bought with texture and material
   quality rather than with triangles — imposters past 60 m, aggressive LODs,
   and a treeline that is cards rather than geometry. That is the whole craft
   of this project now.

2. ~~**Asset licensing.**~~ **Answered 2026-09-29: free, highest quality,
   reputable — chosen by me, not CC0-only.** Recorded in §8, which is
   rewritten to match. The ledger discipline stays.

3. ~~**Adobe tools.**~~ **Answered 2026-09-29: in scope.** The owner's Adobe
   ID and Creative Cloud apps can be used to create assets and textures —
   Mixamo for animation, Photoshop for textures. See §8.

4. **Real device numbers.** Frame rate and frame time on the owner's machine
   in a normal browser window. Automation browsers throttle and read-back
   de-accelerates the canvas (brief §9 finding 11), so the §2 frame budget can
   only be signed off on real hardware.

---

## 12. Agent review and Part 2

*Added 2026-09-30 by a second agent, at the owner's request, after reviewing
this plan against both of the owner's earlier games. **Where this section and
an earlier one disagree, this section wins.** §12.13 lists every conflict, to
be folded into the earlier sections when each is next touched.*

The owner's instruction: this is to be **a more photorealistic and more
immersive game than the owner's earlier ones: a whole new set-up, with the
technology chosen to beat [Vietnam '65 — Lanes of War](https://nixocode.github.io/vietnam-65-lanes-of-war/)**
(public repo `nixocode/vietnam-65-lanes-of-war`, local copy `../Vietnam 1965`).
It is built in Unity with proper assets and real animation, using Adobe
products wherever they help.

### 12.1 The bar is two games, not one

| | *Vietnam '65 — Lanes of War* (2D) | *Lanes of Vietnam '65* (three.js) |
|---|---|---|
| where | `../Vietnam 1965`, public on GitHub, live on GitHub Pages | `../Lanes of vietnam` |
| its strength | **content**: five historical operations, a campaign alternating sides, 14 unit types, the M113, snipers with glint and duels, night at Khe Sanh, monsoon rain on Hill 937, elevation, perks, three difficulties, guided field orders, music | **look and method**: §4 measured to within a point, a deterministic sim, the harnesses |
| its weakness | stylised CC0 sprites on a flat side view | one map, bare palms, streaked clouds, procedural animation, synthesised audio, 58 fps |

**To be better, this game has to beat both at once.** A photoreal one-map
slice loses to the 2D game on everything except looks, and content without
the look is just the 2D game again. So "better" is three gates:

1. **Look:** the §4 band table *and* histogram against `TARGET.jpg`, plus a
   side-by-side the owner looks at: this build beside `TARGET.jpg`, and
   beside the 2D game's `docs/ingame.jpg`. The side-by-side is the one gate
   that is not a number, and it is the owner's call.
2. **Content:** every row of §12.8's matrix reads *matched*, *beaten*, or
   *dropped on purpose, with the reason*.
3. **It runs:** §2's budgets hold in a normal browser on the owner's
   machine.

**Part 1** is the slice: one map at `TARGET.jpg` quality, both sides, the
full interface and sourced audio. It comes first because it proves the
pipeline every later map reuses. **Part 2** is the content that beats the 2D
game (§12.8).

### 12.2 The review: what holds

The path is right where it has been walked:

- The simulation is the same game, and **proven** to be: 22 matches identical
  to the TypeScript at every tick.
- The map, lanes, camera, cover-as-single-source-of-truth, both decks, all 18
  cards and the §9 findings carry over.
- The changes made on purpose are real improvements. Cooldowns tick with the
  sim (the original ran them 3× fast at 60 fps), the command log makes replay
  exact, and player orders are built in.
- Measurement comes before content, as the brief asks.

What the plan lacks is everything that would make the game *look and feel*
better than its predecessors (§12.3–12.7), the technical decisions those
depend on (§12.9), and any Part 2 at all.

### 12.3 Soldiers and motion: the missing step, started now

Brief §6: *"It is the hardest part of the project. Start it first."* §10 has
no step for it: step 4 uses grey-box men, and step 6's art pass names
vegetation, firebase, treeline and mountains. §5.4 describes the three.js
build's procedural animation and never mentions clips, an Animator or Mixamo.
From now on this is its own track, **B** in §12.12, run in parallel with the
engine track, because it takes longest.

**Bodies: built, as the three.js build already proved they can be.**

- **Base:** MPFB2 (MakeHuman) in Blender. Its assets *and* its output are
  CC0, and the pipeline already works in
  `../Lanes of vietnam/pipeline/build_soldier.py`. Blender is at
  `/Applications/Blender.app` (not on `PATH`). Reuse that pipeline and raise
  its output to brief §6: **8–15k triangles at LOD0, three LODs, PBR
  (albedo, normal, ORM)**.
- **Kit sits on sockets, not baked into the mesh**, per force and per
  operation date:
  - *US:* OG-107 fatigues, M1 helmet with cover and band, flak vest, M1956
    webbing, canteens; M16, M60, M79, PRC-25 radio. ARVN share the body with
    lighter kit.
  - *VC/NVA:* black cotton and khaki uniforms, pith helmet, non lá (conical
    hat), bush hat, chest rig; AK-47/Type 56, SKS, RPD, B-40/RPG, Mosin,
    satchel charge.
  - Period kit is researched per operation date and noted in `ASSETS.md`.
    Where `TARGET.jpg` and history disagree, ask the owner.
- **Materials:** start from the three.js build's vetted ambientCG scans
  (Fabric037 fatigue, Fabric019 webbing, Fabric048 vest, Leather032,
  Metal032), whose licence *and* albedo were already checked. Finish them in
  **Photoshop**: wear, sweat, mud, per-man tint masks, subdued insignia.
- **One body, many men:** height, build, skin tone, wear, sleeves, headgear
  and loadout all vary. *"Two identical men should never stand next to each
  other"* becomes a test run over every spawn.

**Motion: Mixamo, through the owner's Adobe ID.**

- Every body goes through Mixamo's auto-rigger, or a Humanoid-compatible
  Blender rig, so that all bodies share one skeleton. Unity's **Humanoid**
  avatar then retargets every clip onto every body. Humanoid retargets
  relative to each rig's rest pose, which is exactly the fix for brief §6's
  "ribboned legs" trap. Every variant is still checked at a zoom where a
  smear cannot hide.
- **In-place clips only, with root motion off.** The simulation owns where a
  man is. The Animator's speed parameter is the sim's distance per tick
  divided by the clip's authored speed. That is §5.4's "phase advances with
  distance, not time", expressed in Animator terms.
- **The clip list.** Download each as *FBX for Unity*, 30 fps, no keyframe
  reduction, and log each in `ASSETS.md` under its Mixamo name.

  | group | clips |
  |---|---|
  | locomotion, per weapon class (rifle, MG, RPG) | idle, walk, run, crouch idle, crouch walk, prone idle, crawl |
  | posture transitions, **all six, both directions** | stand↔kneel, kneel↔prone, stand↔prone |
  | weapon, per posture | aim; fire with recoil through the upper body; reload; MG fire prone; grenade throw; RPG kneel-and-fire; sniper aim |
  | reactions | hit from each of 4 directions; suppressed flinch and duck; dive to cover; **at least 4 deaths per posture that read differently** |
  | life | look around, adjust helmet, wave forward, point, radio handset (the RTO when a call-in is made) |
  | VC-specific | sapper run and satchel throw; climb out of a spider hole or tunnel. If Mixamo lacks these, author them in Blender over a Mixamo base |

- **Animator:**
  - a speed blend tree per posture;
  - a posture state machine built from the transitions above. Its timing
    must still meet §5.4's targets: down in 0.20 s, up in 0.62 s. Those are
    now gates on which clips are chosen, not code;
  - an **upper-body aim layer** with an avatar mask, so a man aims while
    moving. The old 2D game could not, and called it its "single biggest
    limitation";
  - an additive flinch layer.
- **IK uses Humanoid's built-in `OnAnimatorIK`** for feet on the ground, head
  look-at and the off hand on the weapon. It needs no package, which matters
  because §0 shows this editor rejecting two of Unity's own packages. Use
  Animation Rigging only if built-in IK measurably falls short *and* the
  package compiles.
- **Every animation is driven by a sim state or event, never by its own
  timer.** A man is never shown firing on a tick where the sim did not fire.
  Reloads are cosmetic and play only inside long cooldowns.
- **Deaths:** clips, plus a procedural settle onto the terrain whose roll
  direction comes from the view's RNG fork. **No ragdoll** (§12.9).

**Grey-box men animate from day one.** Step 4 uses Mixamo's stock mannequin
(Y Bot) with the real clips. That way the sim→Animator bridge is built and
measured before the soldiers are finished, and the finished soldiers drop
straight in.

**The new performance risk.** WebGL 2 has no compute shaders, so skinning may
run on the CPU, and 60 Animators update on the main thread in wasm. That, not
art taste, decides the LOD0 triangle count. Mitigations, in order:

1. Animator culling.
2. LODs with fewer bones, and 2-bone skin weights.
3. Lower Animator update rates with distance.
4. Last resort: baked vertex-animation textures for the farthest LOD.

**Proposed sub-budget: animation plus skinning for 60 visible men ≤ 4 ms** of
the 16.7 ms frame, measured in a **WebGL build**, not the Editor. The first
measurement confirms or corrects it.

**Gates:**

- a **foot-skate meter**: horizontal drift of a planted foot per stance, read
  from bone positions, **≤ 2 cm**;
- a **posture-matrix capture**: every variant in every posture at 40 px and
  at 400 px, looked at;
- the "no identical neighbours" test;
- the 4 ms sub-budget.

### 12.4 The interface: brief §7, built to `TARGET.jpg`

§5 covers selection and what the deck contains. Brief §7 specifies the whole
screen, and says *"Build this, not something of your own devising."* All of it
belongs in Part 1:

- **Frame and type:** a thin frame and a subtle vignette. Type is **Stardos
  Stencil** (OFL, approved 2026-09-28; `OFL.txt` ships beside it). Olive,
  brass and amber on dark.
- **Top bar:**
  - `★ US / ARVN` with a segmented green morale bar, and `VC / NVA ★` with a
    segmented red one;
  - **two objective diamonds**, one per lane, coloured by whoever holds it;
  - the amber **orders line**, e.g. `ORDERS 2/5 — SET PUNJI STAKES IN THEIR
    PATH [Q]`. These are guided field orders that complete on real sim events,
    and they are *guidance, never a gate*. Port the 2D game's field-order
    content from `js/data.js` and `js/tutor.js`;
  - the **tactical strip**: both lanes drawn schematically, with unit pips,
    each side's front-line trace and the camera's window;
  - **pause, speed (1×/2×), SND, MUS and settings** buttons, each showing its
    own state.
- **Bottom bar:**
  - the **CP counter** (`82 / CP`);
  - unit cards grouped under **LINE · SUPPORT · SPECIAL**, each with a
    portrait, stencil name, cost and composition pips, dimmed when
    unaffordable and showing its cooldown;
  - **call-in cards** with line-art icons, cost and hotkey, visibly different
    per side. The asymmetry is *"the game's best idea — protect it."*
- **Portraits are the real models.** A portrait rig renders each unit in the
  engine (three-quarter view, fixed light), and **Photoshop** finishes it
  (grade, grain, card frame). The card then shows exactly the men you get.
  Call-in icons are drawn in Photoshop, or in Illustrator if the owner
  installs it.
- **Technology: UI Toolkit.** Its runtime module is already in
  `manifest.json`, its stylesheets suit a spec this exact, and it avoids
  adding uGUI. Check that the stencil face renders crisply at 1280×720 and
  at 4K.
- **Colour-blind cue.** Side is shown only by green versus red, on tracers,
  pips and bars, which is the classic colour-blindness failure. Add a shape
  cue (pip shape, tracer pattern) or a colour-blind-safe palette option.
- **Gate:** `UIAudit` drives every element in both states, plus a
  side-by-side of both bars against `TARGET.jpg`.

### 12.5 Match flow: start, opening, ending, settings

The three.js build had all of this; the Unity plan names none of it.

- **Loader:** styled, not Unity's default. Stencil type, a progress bar, and
  interactive in **≤ 12 s** (§2). Streaming carries on behind the start
  screen.
- **Start screen:** over the live scene, slowly panning, not a still image.
  The player picks side and match length (in Part 2 also operation and
  difficulty). **Deploy** is the user gesture that lets audio start.
- **The opening is the infiltration** (three.js D32). Its sim side is already
  ported: the VC move up concealed in the grass, the US hold, and contact
  comes at 8–13 s. The view adds only three things: an authored camera settle
  from the valley onto the firebase, the first radio call, and the first
  order. It is skippable, moves only the camera, and never touches the sim.
- **Two endings, not one card with the winner swapped in:** *THE LINE HELD*
  and *THE WIRE IS BREACHED*, each in its side's colour, with a sub-line taken
  from the sim's own `reason` string. Below it goes an **after-action
  report** (losses per side, squads' veterancy, cards used, time) and a
  **watch the replay** button. The replay costs almost nothing, because
  `LiveMatch.Replay` already reproduces a match from its command log.
- **Pause and settings:**
  - quality tier (vegetation density, shadow distance, render scale);
  - volumes (master, effects, music, ambience, voice);
  - subtitles for radio and voice lines;
  - the key list, camera shake on or off, and hide HUD.

  Settings are saved with `PlayerPrefs` (IndexedDB in WebGL) on a best-effort
  basis: the game must still work when storage is empty or blocked.

### 12.6 Audio: sourced, on the owner's instruction

**On 2026-09-30 the owner overrode §8 rule 4: the audio is to be sourced**
(see the owner's note in §8 and the `ASSETS.md` "Audio" paragraph). The
propagation model stays, applied *on top of* the recordings. The only part of
the old argument that still holds is that recordings arrive with a distance
already baked in. So: **choose dry, close-miked recordings, and let the engine
add the distance.**

**Sources:**

| source | licence | use |
|---|---|---|
| **Sonniss #GameAudioGDC bundles** | free, royalty-free, commercial use, no attribution; not for AI/ML training; not to be resold individually | weapons, explosions, foley, vehicles, ambience |
| **Freesound** | CC0 preferred; CC-BY logged with attribution; **NC rejected** | filling gaps |
| Premiere Pro / Media Encoder 2026 (installed); Audition if the owner installs it | our own processing | cleanup, loudness normalisation, radio band-pass for voice |
| ~~BBC Sound Effects~~ | RemArc licence: personal, educational and research use only | **rejected** |

**What is needed:**

- weapons and ordnance: M16, M60, M79, AK-47, SKS, RPD, RPG, Mosin, .50 cal,
  mortar, 105 mm, napalm, bombs;
- the **UH-1 Huey rotor** (the war's most recognisable sound), aircraft
  passes, radio squelch and chatter;
- ambience beds for each time of day and weather: insects, birds, frogs at
  night, rain, wind in the elephant grass. They duck under sustained fire, as
  the three.js graph did.

**A WebGL fact that decides the architecture.** In a browser, Unity's audio
supports basic positional playback, panning, rolloff, pitch and volume only.
There are **no audio filters and no Audio Mixer effects**
([Unity manual, "Audio in Web"](https://docs.unity3d.com/Manual/webgl-audio.html)).
So the distance model (travel delay, air absorption, late tail, occlusion)
cannot be built from Unity's audio components. Two ways to build it, in order
of preference:

1. **A `.jslib` bridge to Web Audio** that ports the three.js build's
   hand-built, measured graph (`../Lanes of vietnam/src/audio/engine.ts`),
   with recorded sources in place of synthesised ones. It keeps the measured
   model and is native to the browser.
2. **Pre-rendered distance variants** of each sound (close, 100 m, 300 m,
   600 m), with travel delay applied at play time. Simpler, but a heavier
   download.

**Music:** the `MUS` button needs a source, which is an owner question
(§12.14). Period popular songs are under copyright and are out.

**Voice:** radio exchanges make a call-in *"feel like calling for something"*
(brief §8), and squad barks ("Contact left!", "Moving!") make the line feel
manned. Vietnamese lines come from a native speaker or are not used at all;
no put-on accents. All voice is subtitled.

**Gates:**

- the three.js distance table, re-measured on the Unity build. Onset, peak,
  spectral centroid and late energy must all move with distance, and
  occlusion must darken the sound rather than just quieten it;
- no clipping in a scripted 60-man firefight, with peak and loudness measured
  through a limiter.

### 12.7 Immersion: what neither earlier game had

Each item gets an on/off switch, so a capture with and without it can prove
it earns its cost. None may raise `Flicker` or break the frame budget.

1. **Call-ins you watch arrive.** These are view-only choreography, timed to
   the sim's event tick and never touching the sim:
   - *artillery:* the whistle, then rounds walking through the grass;
   - *air strike:* a period aircraft (A-1 Skyraider, F-100) crossing the sky;
   - *medevac:* **a Huey flies in, flares, hovers over the near lane with its
     downwash flattening the grass, and leaves;**
   - *smoke:* grenades pop and the cloud builds;
   - *traps:* the pit opens, the lid lifts;
   - *tunnel:* men climb out of the ground.
2. **The radio operator.** When a call-in is made, the squad's radio operator
   kneels with the handset while the exchange plays, so every call has a
   person attached.
3. **Men who look alive:** idle movement, look-at IK tracking the nearest
   threat, flinching under fire, a leader waving the squad forward.
4. **Air and light:** cloud shadows moving across the valley (a scrolling
   light cookie on the sun: cheap, and large on screen); smoke drifting from
   the firebase and from barrages; dust from movement and impacts; a blob
   ambient-occlusion shadow under each man so his feet sit on the ground.
5. **Scars that stay:** scorch marks, flattened and burnt grass, spent
   casings, lingering smoke; bodies already persist. **A scar that is not
   cover must not look like cover** (§3.3's rule), so a shell mark is a
   shallow scorch unless the sim registers it as a crater.
6. **Field glasses:** hold a key to narrow the field of view toward the cursor
   (19° down to about 7°). There is no orbit, so the composition holds. This
   is where brief §6's *"readable at 400 px"* is actually seen.
7. **Camera shake** from nearby bursts: small, measured, and switchable off
   in settings. Depth of field on the near ground only, as in three.js D9.
8. **Wind that does not flicker:** low-frequency wind in the vertex shader,
   weakening with distance and switched off beyond the imposter distance. The
   three.js build found that wind turns into aliasing at depth.
9. **Night and weather** (Part 2 operations):
   - illumination flares drifting under parachutes, as the light that makes
     night both photoreal and playable;
   - muzzle flashes as real lights, drawn from a small fixed pool;
   - rain with wet materials (darker albedo, lower roughness, puddles in the
     ruts) and splashes;
   - fog.
10. **Documentary tone**, carried over from the 2D game: *neither side is
    glorified*. Deaths read clearly without gore, and civilians are never
    targets. Operation briefings may use **public-domain US government
    photographs** (NARA, US Army), each one logged.

### 12.8 Part 2: content that beats the 2D game

Brief §8 said *"No campaign. One map."* The owner's 2026-09-30 instruction
supersedes that for Part 2. Part 1 stays one map.

| the 2D game has | Unity Part 1 | Part 2 |
|---|---|---|
| **5 historical operations**: Ia Drang (Nov 1965), Cu Chi (Jan 1966), Mekong Delta (Jun 1967), Khe Sanh (Jan 1968, night), Hill 937 (May 1969, rain) | 1: the `TARGET.jpg` firebase, 1965 | all five, as photoreal maps; the slice map stays as the first operation. Proposed order, by how much each reuses: **Khe Sanh** (firebase set + night) → **Cu Chi** (the jungle set + tunnels) → **Ia Drang** (grassland, golden hour, the Chu Pong massif) → **Hill 937** (slope, bunkers, rain) → **Mekong** (paddies, dikes and water: the most new technology) |
| modes: standard, **siege** (timer), **assault** (timer + capture both flags) | standard | siege and assault |
| **campaign**: five operations alternating sides, with briefings, objectives and field orders | none | ported from the `CAMPAIGN` table in `js/data.js`. The briefing text is already written; check its history |
| **difficulty**: Recruit / Veteran / Elite | the sim's plan quality | three levels, set by plan quality and income; balance measured per level |
| US units: rifleman, **ARVN**, M60, engineer, **LRRP recon**, **scout sniper**, **M113 APC**, grenadier | rifle, weapons, M60, mortar, engineers | add ARVN squad, LRRP team (spotting), sniper team, APC section |
| VC units: guerrilla, NVA, **RPD**, RPG, sapper, marksman | guerrilla, NVA, RPG, marksman, sapper | add RPD team |
| US call-ins: fire mission, **napalm**, dustoff, **Air Cav**, **Arc Light** | artillery, smoke, medevac, air strike | napalm (**burns away concealment** in its zone, as in the 2D game); Air Cav (Hueys insert a squad); Arc Light (a line of B-52 bombs, with the longest cooldown) |
| VC call-ins: punji, tripwire, spider hole, tunnel | the same four | matched |
| orders: advance, hold, fall back, **frag**, **smoke** | advance, hold, bound, fall back | add frag (a close assault breaks a position that small arms cannot) and squad smoke (the 2D game's AI pops it when bounding is blocked) |
| **elevation**: firing downhill gains range and damage | flat play | goes into the sim, with Hill 937 |
| **snipers**: scope glint, duels | the marksman card | glint as a view effect of the sim's aim state; port the duel logic |
| villages with **firing ports**, MG nests, dikes | trench, sandbag, bunker, crater, bank | added per operation: dikes, huts with firing ports, MG nests, bunkers |
| **perks**: six, bought with commendation points earned across matches | none | ported, kept as modest as in the 2D game (`js/perks.js`) |
| music, SND/MUS buttons | the buttons (§12.4) | the music (§12.14) |
| frame-time readout; self-test of every map × side | `Budget`, `SimTests`, `UIAudit` | extended to every operation × side × difficulty, headless |
| **mobile version** (a separate repo) | none | **not planned.** A photoreal WebGL build at 45 MB with a 512 MB heap is beyond mobile browsers. This is stated, not quietly dropped (§12.14) |

New in this game, beyond both predecessors: the Mortar team, player orders on
any squad, the Huey and aircraft on screen, field glasses, replays, flares as
a night mechanic, and sourced audio with distance.

**The simulation's policy for Part 2. This matters.** The C# sim is proven
identical to the TypeScript original, but Part 2 adds mechanics the original
never had, so parity cannot cover them:

1. Tag the proven state (`sim-parity-baseline`) before the first Part 2
   mechanic goes in.
2. **Every new mechanic sits behind a match option whose default is the
   baseline**, so the 22 parity matches keep passing unchanged.
3. From then on, the C# sim is the source of truth, and its own pinned
   determinism hashes cover the new content.
4. Each mechanic lands with a 48-seed balance block (with Wilson intervals),
   an audit proving its new events are reachable, and a `UIAudit` row for any
   new control.
5. **The 2D game's numbers do not transfer.** Its units have hit points; this
   sim deliberately has none (§5.2). "The M113 takes about 105 rifle rounds
   and dies to two rockets" is a design intent to translate (immune to
   small-arms lethality, vulnerable to RPG and satchel rolls), not a number
   to copy.

**Budget for Part 2.** §2's 80 MB total was for one map. It becomes **≤ 45 MB
initial** (menu plus the first operation), with **each further operation
≤ about 40 MB, streamed when chosen**. Changing operation unloads the previous
one, and `Budget` checks the heap after three switches as a leak test.

### 12.9 Technology decisions: make them before the art

| decision | recommendation | why | settled by |
|---|---|---|---|
| **Editor version** | Install the newest **LTS** release listed in the Hub *alongside* 6000.6.3f1, and try a copy of the project on it. Move to it if it compiles the packages below and passes `SimTests`. | Only 6000.6.3f1 (tech stream) is installed, although §2 says "Unity 6 LTS". Addressables and Input System already fail to compile on it, and the soldier and streaming work need more. | a compile, `SimTests`, and a new empty-build floor on the LTS |
| **Streaming without Addressables** (if 6000.6 stays) | raw **AssetBundles** via `UnityWebRequestAssetBundle`: engine modules, not a package | §6's streaming, and with it the 45 MB initial budget, currently depends on a package that does not compile | a streamed treeline in a WebGL build |
| **Engine modules** | **Add** `particlesystem`, `terrain`, `assetbundle`, `unitywebrequestassetbundle`, none of which resolve today. `animation` and `physics` already resolve through `render-pipelines.core` (see `packages-lock.json`): **list `animation` explicitly**, so a pipeline upgrade cannot remove the Animator. | each is needed by a step above | a compile, and **each one's build-size delta against the 8.00 MB floor** |
| **Physics** | **not used, no ragdoll**. It is resolved as a dependency, so confirm in the build report that engine stripping removes it. | download size, and the sim already owns movement. Brief §6 made the ragdoll conditional ("if physics is in"). | the build report |
| **Effects** | the built-in Particle System | **VFX Graph, STP upscaling and the GPU Resident Drawer all need compute shaders, which WebGL 2 lacks** | none needed |
| **Anti-aliasing for foliage** | measure **TAA** against **MSAA 4× with alpha-to-coverage**, **in a WebGL build**. Editor captures (`captures/taa.json`) run on Metal and prove nothing about WebGL 2. | URP's TAA on WebGL 2 is unverified here, and alpha-to-coverage is the classic answer for cut-out foliage | `Flicker` and `FrameCapture`, same scene, both ways |
| **Terrain** | a Unity Terrain whose heightmap is **generated from `Sim/Ground.cs`** | Unity's layering and detail instancing, while the sim's ground stays the truth | a test: terrain height matches `Ground` within 2 cm at sampled points (foot IK depends on it) |
| **Colour grade** | a LUT graded **in Photoshop** from a neutral capture, against `TARGET.jpg`, and loaded through URP's Color Lookup | a standard Photoshop workflow, and it keeps the grade an asset rather than a pile of sliders | `LookMeter` before and after |
| **Audio** | the `.jslib` Web Audio bridge (§12.6) | Unity WebGL has no filters and no mixer effects | the distance table |
| **UI** | UI Toolkit (§12.4) | already present | `UIAudit` |
| **Skinning cost** | §12.3's mitigations | skinning may run on the CPU in WebGL | the 4 ms sub-budget |

**Adobe work must be reproducible.** Photoshop jobs (frond and leaf atlases,
seam fixes, portraits, UI chrome, decals, the LUT, the mountain normal map)
are scripted in JSX under `tools/photoshop/` and run through `osascript`
(Photoshop's `do javascript`). That way an asset can be regenerated rather
than remembered, as in the three.js build, where assets were generated and
never committed as sources.

### 12.9a Step 2a: decided, with the measurements

*Worked through 2026-09-30.* Every row of §12.9 that can be settled before
there is foliage to look at is settled here, each with the number that
settled it. Builds are Brotli WebGL, release, empty scene.

| decision | outcome | the measurement |
|---|---|---|
| **Editor version** | **Stay on 6000.6.3f1.** 6000.3.25f1 LTS is installed alongside as a fallback. | The reason to move was false: with each editor's recommended versions, Addressables and Input System compile and all tests pass on **both** editors. With identical packages, the empty floor is **8.51 MB on 6.6 and 9.31 MB on 6.3 LTS** (wasm 5.24 vs 6.17 MB). Moving to 6.3 is also a *downgrade*: URP's global settings asset, saved by URP 17.6, had to be deleted and regenerated before 6.3 would build. The next LTS (6.7, in beta in the Hub) is an upgrade from 6.6, not from 6.3. |
| **Physics** | **Out.** The physics backend is set to "None" (`m_CurrentBackendId` 0xDECAFBAD) by `ProjectSetup`. | With PhysX selected, even the empty floor linked **11 physics internal calls** (raycasts and collision callbacks referenced from UI and render code), and with them native PhysX. Switching the backend to None: **8.00 → 7.34 MB** initial, wasm 4.95 → 4.25 MB. **7.34 MB is the new floor.** |
| **Engine modules** | `animation` (now explicit), `particlesystem`, `terrain`, `assetbundle`, `unitywebrequestassetbundle` added to the manifest. | Listing a module costs nothing; *using* one does. With a probe script per module (`View/Diagnostics/ModuleProbe.cs`, `Build.ModuleDeltas`): Particle System **+0.16 MB**, Animator **+0.11**, Terrain **+0.06**, AssetBundle **+0.06**; all four together **+0.39 MB**. |
| **Streaming** | **Addressables 2.11.2**, added when streaming is built. | §12.9 fell back to raw AssetBundles only because Addressables did not compile, which was wrong. It costs **+0.10 MB** against AssetBundles' +0.06, and buys catalogs and dependency tracking for Part 2's per-operation streaming. |
| **Photoshop scripting** | `tools/photoshop/run.sh` + JSX. **Blocked on the owner** (§12.14). | AppleScript reaches Photoshop 27.8.0 (`get version` answers), but every `do javascript` call times out, even `"app.version"` — Photoshop's script engine is waiting on something only the owner can see or allow. |
| AA for foliage, terrain, colour grade, audio bridge, UI | not yet | each needs content that does not exist yet (foliage, the art pass, audio); they are settled in the steps that build that content, against the gates §12.9 gives. |

### 12.10 Asset sourcing sprint, before the art pass

The three.js build lost on exactly this list. So every item is chosen,
**looked at, measured (triangles, texture sizes) and logged** before the art
pass starts.

| item | look first | fallback | must |
|---|---|---|---|
| **coconut and areca palms** (the three.js build's worst miss) | Sketchfab (CC0 and CC-BY only); free items on Fab and the Unity Asset Store, licence checked per item, and anything restricted to another engine rejected | Blender: a trunk with Poly Haven bark, fronds from a Photoshop atlas cut from CC0 leaf scans | a full, heavy crown that reads in silhouette at 60–120 m, with LODs and an imposter |
| bamboo | as above | a Blender generator plus an atlas | clumps, not poles |
| banana, elephant ear | Poly Haven, Sketchfab | Photoshop atlas cards | |
| understory | **the seven vetted Poly Haven scans**: fern_02, weed_plant_02, calathea_orbifolia_01, nettle_plant, shrub_02, anthurium_botany_01, pachira_aquatica_01. Re-log them here | | |
| elephant grass | terrain detail with a Photoshop atlas made from CC0 scans | | no speckle under `Flicker` |
| treeline | imposters baked in the engine from the near species | | clumpy, never a wall |
| **mountains** | **real terrain: SRTM elevation data** (public domain, NASA/USGS) for the Central Highlands. The Chu Pong massif is a real shape | a hand-shaped mesh with a Photoshop-painted normal map (§6) | three to five ridges with real silhouettes and internal detail |
| **sky** | a Poly Haven HDRI with real cumulus (CC0) | | cumulus, not streaks (the three.js build's second miss) |
| terrain materials | the vetted ambientCG set: Ground103 track, Ground109 verge, Ground037 scrub, Grass004 | | albedo measured, not guessed (three.js D7, D12) |
| firebase: tower, sandbags, revetments, wire, crates, drums, flag | Sketchfab, Fab and Asset Store free items | Blender, with ambientCG sandbag, wood and canvas | |
| M35 truck, M151 jeep; in Part 2 the M113, UH-1 and aircraft | Sketchfab CC-BY military models, Fab, Asset Store free items | Blender | period-correct, PBR, inside the triangle budget |
| soldiers | §12.3 | | |

§8's trusted-source table gains Sonniss, Sketchfab (per-model licence, CC0 or
CC-BY only), free items on Fab and the Asset Store (licence checked per item),
public-domain US government photographs, and SRTM data. Its rejected list
gains BBC Sound Effects.

### 12.11 Publishing: GitHub, like the 2D game

The 2D game is a public repo served by GitHub Pages. If this one goes the same
way, three things break unless they are planned now:

1. **Licences versus a public repo.** Mixamo forbids redistributing its raw
   files. The Asset Store and Fab licences forbid redistributing source
   assets. Sonniss sounds may not be resold individually. A public repo
   holding those files would redistribute them. So either the repo is
   private, or the licensed source files live outside git (an ignored
   `Assets/_Licensed/`, restored by a script from the owner's storage) and
   only builds are published. CC0 assets and our own work can be committed.
2. **Git LFS for textures, models and audio.** Plain git rejects files over
   100 MB. Check the LFS quota before the first asset push.
3. **Brotli on Pages.** Pages cannot send the `Content-Encoding: br` header,
   so a Brotli-compressed Unity build will not load there unless
   **Decompression Fallback** is turned on. That is the opposite of §6's
   "fallback off". Choose one:
   - Pages with the fallback on (a slower first load, measured against the
     12 s budget); or
   - a host that sets the header (Cloudflare Pages, Netlify, Vercel),
     measured the same way.

Nothing is pushed or deployed without the owner's go-ahead. That rule is
unchanged.

### 12.12 Execution order: replaces §10 from step 2 on

There are two tracks. **A** is the engine and the game. **B** is the soldiers;
it starts now, because it is the longest job and brief §6 says so.

**Part 1: the slice**

| # | track | step | gate |
|---|---|---|---|
| 2 | A | harnesses: `FrameCapture`, `LookMeter`, `Budget`, `Flicker`: **done** (§10a) | met. One caveat: they run in the Editor on Metal, so any anti-aliasing or timing number must be re-read from a WebGL build |
| 2a | A | **the technology decisions** (§12.9) | each one written down with its measurement; a new empty-build floor if the editor changes |
| 3 | A | grey-box map *(in progress)* | lanes readable in a grey-box capture |
| 3a | B | **soldier proof:** one US body, rigged, six clips, Humanoid, in a WebGL build | foot skate ≤ 2 cm; 60 animated men inside 4 ms |
| 4 | A | the sim drives Mixamo-mannequin men at 20 Hz through the real Animator (§12.3) | 60 men inside the frame budget |
| 5 | A | command: selection, orders, camera, field glasses | `UIAudit` green |
| 5a | A | **the interface to brief §7, plus loader, start screen, opening, endings and settings** (§12.4–12.5) | `UIAudit` on every element; both bars side by side with `TARGET.jpg` |
| 6a | A | **asset sourcing sprint** (§12.10). It is research, so it can start at any time | every item looked at, measured and logged |
| 6b | A | art pass: terrain, vegetation, firebase, treeline, mountains, sky, grade | §4 table **and** histogram; `Flicker` at or below the three.js build's 1.30; the owner's side-by-side |
| 6c | B | **soldiers complete:** both forces, every Part 1 unit, the full clip list, IK, variation, LODs, portraits | posture matrix at 40 and 400 px; every variant checked at zoom; no identical neighbours |
| 7 | A | gunplay, and the call-in choreography (§12.7 item 1) | visible in a capture; each arrival lands on the sim's event tick |
| 8 | A | deck: every card does something | `UIAudit` proves all 18 |
| 9 | A | **audio, sourced** (§12.6) | the distance table on the Unity build; no clipping at 60 men |
| 9a | A | the immersion layer (§12.7), each item switchable | `Flicker` and the frame budget hold with everything on |
| 10 | A | polish, and a build a stranger can load from the chosen host (§12.11) | every §2 budget, on the owner's machine |

**Part 2: the content that beats the 2D game.** Each step ships on its own.

| # | step | gate |
|---|---|---|
| 11 | sim extensions behind options (§12.8's policy): frag, squad smoke, elevation, sniper glint and duels, napalm versus concealment, armour, flares | the parity baseline still identical; a balance block per mechanic |
| 12 | unit parity: ARVN, LRRP, sniper team, RPD team, APC section (M113) | each card proven by `UIAudit`; balance |
| 13 | the operations, one at a time, in §12.8's order | **each needs its own reference frame before its art starts** (§12.14); look, `Flicker` and budget gates per map; both sides winnable |
| 14 | modes (siege, assault), campaign, difficulty, perks, and field orders for every operation | every operation winnable by both sides at every difficulty: 48 seeds, headless |
| 15 | the stranger test | someone who has never seen the game plays the first operation cold and finishes it without help |

### 12.15 Fieldcraft and levers (owner, 2026-10-01)

After playtest 3 the owner asked for three things at once: soldiers that do
not "run circles around themselves, cross each other", Warfare 1944's cover
system ("the trench system, vaulting, and holding the trench via lever HUD"),
and fighting that is felt ("melee for really close up", "shots go through
other characters and spit out blood. Impacts need to be felt and feared").

They are one rule set, `MatchOptions.Fieldcraft` (`Sim/Fieldcraft.cs`), under
§12.8's policy: off is the parity baseline to the bit, its state is hashed
only when on, it has its own pinned matches, a 48-seed balance block and a
`UIAudit` row. The game turns it on.

| rule | what it does |
|---|---|
| files, ranks | a lane has three files and a squad walks in one; a man's rank closes up when the man ahead falls |
| places | cover has places along it, front first; down a trench in single file |
| pause, dash | a squad stays a few seconds in cover it reaches; told to hold in the open, it makes for cover that is near |
| stand-off | it stops 12 m short of an enemy it can see, kneels and fights |
| assault | unless he is pinned or outnumbered two to one: then it closes |
| close in | fire is deadlier the nearer; inside 6 m a man charges; inside 2 m they fight hand to hand. Smoke hides an enemy from the stand-off, so squads in smoke meet at arm's length |
| fear | a man killed beside you puts suppression on everyone within 6 m |
| through | a round that kills carries on up to 12 m and may hit the next man in its line |
| vault | climbing into a trench (the one kind that is dug) or out of one takes most of a second, in which he neither moves nor fires |
| strongpoints | **every piece of cover**, built (wall, bunker, trench) or natural (crater, bank): the owner, 2026-10-01, "don't think of trenches, it's more like a stronghold for squads to take cover. Not all can fit at once, could be man made or natural". It has places for so many men and the rest wait behind it; it belongs to the last side to have it alone, and losing one costs morale (0.05 built, 0.02 natural) |
| levers | per side, per strongpoint: Hold (stay, and call the next squad to it), Go (leave it; squads behind pass through), Auto (the plan decides). A command, logged and replayed |

**The interface.** A plate floats over each strongpoint: whose it is, the
player's men in it against its room, and the lever. Left click flips Hold and
Go, right click gives it back to Auto; H and G do the same for the plate under
the pointer or the selected squad's position.

**Measured** in §10a's row "11 (fieldcraft)". `tools/simcs/run.sh muddle`
prints the counts; `lever` traces a held and released squad tick by tick.

**Clips (Mixamo, owner's OK 2026-10-01; ASSETS.md):** Bayonet Stab and Slash
Advance for the blow, Jump Down and Climbing Up Wall for a trench. The blows
start past their wind-up and run at 1.7-1.9x to fit the simulation's 0.9 s;
the climb out has its rise left in the root (it was authored for a wall twice
a man's height) and the trench's depth supplies it.

**Tried and dropped:** turning three banks into trenches so each lane had
more to hold. The owner's point stands instead: a bank is already a
strongpoint. (Two of them alone also took the Americans to 68.8%.)


### 12.16 Arms: a weapon to every class (owner, 2026-10-01)

"You need to add all the corresponding gun models in Blender to each class.
Also add a sniper class. And fix distance for all gunfights, it needs to be
properly set."

Until now every card with men on it bought three to six of the same rifleman,
who engaged at 28 m whatever his card said. `MatchOptions.Arms`
(`Sim/Arms.cs`), under §12.8's policy (off is the parity baseline; hashed only
when on; pinned matches; balance block; audit):

| kit (card) | men, lead first | fights from | assaults |
|---|---|---|---|
| RIFLE SQUAD | 5 x M16 | 13 m | yes |
| WEAPONS SQUAD | M16, M60, M79, M16 | 16 | yes |
| M60 TEAM | M16, M60, M16 | 20 | no |
| MORTAR TEAM | M16, mortar, M16 | 30 | no |
| ENGINEERS | 2 x M3, 2 x M16 | 9 | yes |
| **SNIPER TEAM** (new) | M40, M16 | 32 | no |
| GUERRILLA CELL | PPSh, 3 x SKS | 12 | yes |
| NVA SQUAD | 2 x AK, RPD, 2 x AK | 13 | yes |
| RPG TEAM | AK, RPG-7 | 16 | no |
| MARKSMAN (the VC's sniper) | Mosin | 32 | no |
| SAPPER | 3 x PPSh | 6 | yes |

| weapon | range m | a shot every | accuracy x | suppression x | notes |
|---|---|---|---|---|---|
| M16, AK-47 | 20 | 1.0 s | 1.0 | 1.0 / 1.05 | |
| SKS | 21 | 1.3 s | 1.12 | 0.9 | |
| M60 / RPD | 30 / 28 | 0.45 / 0.5 s | 0.42 / 0.45 | 1.5 / 1.4 | bursts |
| M3, PPSh | 15 | 0.35 s | 0.9 | 1.2 | |
| M40, Mosin | 44 | 4 s | 1.8 | 1.6 | keeps 80% at full range; cover is worth two thirds |
| M79 | 7-24 | 5 s | burst 4.0 m | | |
| RPG-7 | 7-24 | 5.5 s | burst 4.5 m | | cover is no help |
| mortar | 16-50 | 5 s | burst 6.0 m | | fires over smoke |

**Distances are the frame's.** The lens at its widest shows 23 m of the near
lane; at the baseline's 28 m the two sides of a firefight were never in it
together. A squad that can see the enemy from beyond its kit's distance closes
to it unless it is beaten down, and stops there (the stand-off of §12.15).
Hand grenades scale with it (12 m). A side raising squads by itself goes round
a list that is mostly riflemen with a company's support.

**Balance** was found, not assumed: first numbers 89.6% US; with the sniper,
the M79 and the squad lists adjusted, 51.0% over 96 seeds (§10a).


### 12.17 Where it stands, and what is next (2026-10-01, after playtest 4)

**In the build** (commit `90a256f`, local; WebGL 29.85 MB; EditMode 50/50,
PlayMode 14/14; real Chrome about 6.5 ms a frame):

| asked for | state |
|---|---|
| soldiers that are not stick figures (playtests 2, 3) | six rebuilt bodies, hair, whole necks and legs; one US man in a boonie hat |
| sandbags too new, flickering (playtests 1 to 3) | built of bags, weathered; the flicker was TAA, now SMAA (frame 0.79 -> 0.08) |
| "headless chickens", crossing, circling (playtests 3, 4) | Fieldcraft (§12.15) and the playtest-4 pass: nobody walks through the enemy, men off each other, posture changes 21 -> 12 a minute, hops 15 -> 3 |
| melee at arm's length; shots through men; blood; impacts felt | in: bayonet clips, rounds that carry on, blood thrown and left, deaths that fall the way they were hit, fear when a man drops |
| Warfare 1944's hold and go | a lever and a plate on every strongpoint (any cover, built or natural); H and G |
| a weapon model for every class; a sniper class; fight distances | Arms (§12.16): eleven kits, twelve built models, ranges that fit the frame |
| points too slow; too many soldiers at the start (playtest 4) | the player's side no longer spends his points; 1.6 a second, 20 in hand; a squad a lane to open; the computer's squad costs it 28 |

**Next, in this order.** Each lands with its measurement, a local commit and a
build the owner can play. Anything that downloads asks first.

1. **Playtest 5.** The owner plays the build above. Three things only he can
   judge: whether the men still read as clunky or random at full speed (if so,
   what they were doing), whether the tempo is right (the dials are
   `GameRoot.CpRate`, `StartCp`, `OpeningStrength`, `MusterCost`; check with
   `tools/simcs/run.sh player 24 <cost>`), and whether the guns now have punch.
2. **Owed:** items 3 and 4 of the playtest-3 list, which were images that never
   arrived. Slot them in at the top when re-sent.
3. **Sound.** Firing sounds for the M79, the RPG-7 and the mortar (a pitched-up
   howitzer stands in): source them, with the owner's OK for the download. A
   launcher's burst the size of its round.
4. **Loose ends of Arms.** The mortar set up on the ground when its team halts,
   not carried like a rifle. Sappers and SMG men measured in play (0.08 to 0.13
   kills a man when last counted); the computer's side given sappers if they
   now work. A difficulty setting made of the tempo dials (the 2D game's
   Recruit / Veteran / Elite).
5. **Soldiers.** More headgear and kit variety (bare heads, the NVA sun helmet
   checked at zoom, ammunition belts on the gunners, dirt and sweat); card
   portraits redrawn from the rebuilt bodies with their weapons.
6. **Strongpoints.** Trench walls revetted and duckboarded; the plates' look;
   a strip in the HUD for strongpoints off screen, if the owner wants it.
7. **Frame time.** 3.6-4.1 ms before the new bodies and sandbags, about 6.5
   now: a far LOD and a shadow proxy for the sandbag walls (0.2 M triangles),
   a 3 k-triangle soldier under 110 px. Target 5 ms with 60 men.
8. **Gore** beyond what is in (the owner: not the priority).
9. **§12.12's remaining steps:** 9a the immersion layer (the Huey and the
   aircraft on screen, flares); 10 a build a stranger can load (push and host
   **when asked**: ten commits are local only); Part 2's elevation, sniper
   glint and duels, napalm against concealment, armour and flares; the ARVN,
   LRRP and RPD-team cards and the M113; the other four operations, each
   needing its own reference frame; modes, campaign, perks; the stranger test.

**How to run it.** `python3 tools/serve.py` from the project folder, then
http://127.0.0.1:8065. Nothing is left running between sessions.


### 12.18 Playtest 5 and the order after it (2026-10-02)

The owner's eight notes, and where each goes:

| note | where |
|---|---|
| strongpoint plates too prominent, too many | **done (phase 0):** 40% opacity until the pointer is within 70 px; craters and banks show a plate only while they hold his men, have a lever set or are hovered |
| heads look down when prone | **done (phase 0):** head and neck turned to the target, or the front, after the Animator's pose (85 degrees at most; not while pinned flat or mid-reaction). `SoldierPreview.RenderLook` is the sheet |
| some VC look too like the US | **done (phase 0):** NVA cotton and VC webbing to dark browns (cloth 0.035 to 0.049, webbing 0.037 linear); three bodies re-exported |
| still too many soldiers at the start | **done (phase 0):** one squad a side to open (`OpeningStrength` 4). `simcs player 24 28`: a player who buys wins 20 of 24, one who does nothing loses in about 55 s |
| unnecessary shooting: fire only at what a squad has spotted | phase 1, Senses |
| **main focus:** awareness and combat instincts (wander, don't see each other, stand around) | phase 1, Senses |
| some guns look too basic | phase 3 |
| start thinking of dynamic weather | phase 5 |

Phase 0 numbers: EditMode 50/50, PlayMode 14/14, WebGL 29.83 MB, real Chrome
5.6 to 6.2 ms a frame (52 to 59 men).

**Phase 1, Senses (`MatchOptions.Senses`).** Sight today is one flag a man,
lopsided and permanent: every American is seen from the moment he spawns, a
VC is unseen until he fires or is walked onto, and nobody is ever lost sight
of. So: both sides spotted the same way (posture, movement, firing, cover,
smoke), each squad keeping what **it** has spotted and for how long; fire only
at what the squad has spotted; a `Contact` event the moment a squad spots or
is fired on, and a reaction to it (to ground, nearest cover, return fire); a
task a squad holds for a minimum time in place of an order re-decided every
tick. Measured before and after in `simcs muddle`: shots at the unspotted,
squad-seconds close and unaware, man-seconds upright in a firefight,
man-seconds idle in the open beside cover, seconds from sight to reaction.

**Phase 1 as built (2026-10-02; `Sim/Senses.cs`, on in the game).** Measured
with `tools/simcs/run.sh aware 3 2400 6 fieldcraft arms tempo [senses]` and
`muddle` (six seeds, twelve minutes of fighting, the game's economy), a minute:

| | before | with Senses |
|---|---|---|
| shots at a squad nobody on the firer's side has in sight | 2.5 | 0.8, of which 0.6 are a machine gun's bursts on cover it has lost sight of (they pin, and kill nobody) |
| men with a seen enemy in range who are standing still | 16% | 2% |
| ... standing and moving (now a rush between cover, at a run) | 19% | 14% |
| squad-seconds two enemy squads are within 15 m and neither fires | 24.0 | 16.6 (most of it smoke) |
| shots across the lanes | 32% | 29% (a squad now stops level with one it is fighting in the other lane) |
| a squad's changes of order | 8.1 | 6.3 |
| a man turning about | 18.3 | 12.9 |
| a man's changes of posture | 10.8 | 12.1 |
| men idle in the open beside cover with a free place (man-seconds) | 91.5 | 83.0 |

What does it: a squad spots a squad (kept three seconds, remembered fifteen,
word passed within 30 m); fire only at a squad in sight, the squad's own
threat first; at contact (inside 28 m, or its weapons' distance and 6 m) its
men drop at once and it takes the nearest cover within 10 m; then one task at
a time, each held two seconds at least: march (up the lane, no waiting in
every piece of cover), contact, firefight, close (a rush to the next cover at
1.45 times the march), assault (man by man, not behind a pinned lead man),
withdraw (to the strongpoint behind, not twelve metres backwards), regroup. A
pinned man drops at once. The Americans open in their position, not in a file
beside it. `Man.Seen` now means "an enemy squad has his in sight", so the
minimap shows only what has been spotted.

Balance, 96 seeds: Americans 52.1% (42.2 to 61.8), mean 180 s (the VC's list
of raised squads swaps one NVA squad for a cell under Senses; with the old
list it was 36.5%). The computer's squad now costs it 32 (at 28 a player who
only buys rifle squads won 9 of 24 as the Americans; at 32, 18, and 20 as the
VC; one who buys nothing loses in about a minute). EditMode 54/54 (two new
pins, every shot checked against its squad's sight, every task reached),
PlayMode 14/14, WebGL 29.84 MB. Frame time: 15 ms read with the owner's own
browser running the game on the same GPU; taken again alone with the next
build (§12.19), 6.1 to 6.5 ms.

`tools/strip.sh seed=3,tick=470,x=-2,n=8,every=1.5` is the way to look at a
moment: eight frames as one image. `tools/simcs/run.sh watch <seed> <from>
<to> [every] ... senses` prints each squad's task, threat and men tick by tick.

**Still open in phase 1:** (the motion audit is done, §12.19;) a mark the player can see when a squad makes contact; men pinned
in the open three metres in front of a wall they closed past; squads
stringing out at the map's edge when they arrive (half of what is left of
"idle in the open").

**Then:** 2 a match a new player can read (difficulty, squad tags, cards that
say what they buy); 3 guns (model detail, launcher sounds); 4 every class its
job (mortar emplaced, satchel charges, the computer's lever); 5 weather (a
schedule from the seed, sight scaled through Senses, sky, rain, a rain bed);
6 look, speed, content.


### 12.19 Playtest 6: the Senses build (2026-10-02, evening)

The owner played commit `643230f`. His notes, and where each goes. This
section is worked through before phase 2 of §12.18; phases 2 to 6 follow it
unchanged.

| his note | what it is, and the change | where |
|---|---|---|
| "still too many random shots shooting up, down and in circles" | on a side-on camera a shot across the lanes is a shot straight up or down the screen (29% of all shots), and a man whose nearest target changes lane swings round to it. **Gunnery, lanes:** a rifle, a machine gun and a scope fire down their own lane only; a squad's threat is a squad in its lane; only what is thrown or lobbed (grenades, the M79, the RPG, the mortar) crosses, and a man at arm's length | `Sim/Gunnery.cs` (`MatchOptions.Gunnery`), `Senses.PickTarget`, `Senses.Look` |
| "the gunners sometimes shoot and walk, they should only shoot prone or kneeling. Shooting while standing should be very inaccurate (and happen 10% of the time or less)" | **Gunnery, stance:** nobody fires while he is moving. A man at rest on a knee or flat fires as now. A man at rest on his feet fires one time in ten and hits a third as often. The count to hold: shots by standing men under 10% of all, shots by moving men none | `Combat.Fire`, `Combat.HitChance` |
| "lots of US soldiers sliding and not walking" | men moving while the simulation has them kneeling or flat (a kneeling man shuffled up to four metres to his place; a broken man crawled away), drawn with no gait to match. **Sim:** a man who has to move gets up and runs, then goes down; a squad falling back runs. **View:** a motion audit (PlayMode, category `Motion`) that measures foot skate, so it is a number and not an impression | `Match.MoveMan`, `Tests/PlayMode/MotionAuditTests.cs`, `View/ArmyView.cs`, `SoldierFigure.cs` |
| "US soldiers clipping when prone next to the US spawn (sandbags)" | a prone man is 1.8 m long and the firebase's walls are drawn where the files walk. Keep men's places and files clear of the drawn walls; a man with a wall inside his own length kneels | `View/CoverView.cs`, `WorldDressing.cs`, `Sim/Fieldcraft.cs` places |
| "some trees could have some sway, or they need to be higher quality" (the bamboo) | wind in the plants' vertex shader (cheap on WebGL: no extra draw calls), stronger at the tips, phased by position; then the bamboo's leaf cards looked at close | plant shader and `Art/` |
| "want to improve the shooting mechanics. See real impacts, where bullets go" | every round goes somewhere: a hit is on the man; a miss lands on the ground, the cover or the foliage beyond him (dust, a puff off a sandbag, splinters, leaves), on a line from the muzzle. Tracers one round in a few, along that line and no further. The simulation says where (so a replay shows the same); the view draws it | `Gunnery` (miss point on the `Fire` event), `View/CombatView.cs` |
| "better muzzle flashes" | a flash at the muzzle of the model in the man's hands, shaped by weapon (a star for the M16's birdcage, a long tongue for the M60, a ball for the M79), a frame or two, lighting the man | `CombatView`, `SoldierFigure` (muzzle empties are already on the weapon models) |
| "sounds for sniper" | the sniper's own report: a single heavy crack and its echo, apart from the rifles' set. From recordings already licensed and on disk if one fits; otherwise named, with source and size, for the owner's yes before any download | `tools/audio/slice_shots.py`, `AudioView.Shot` |

Order: Gunnery (lanes, stance) with its counts and balance; the sliding (sim
side, then the audit); impacts, tracers and flashes; the sandbag clipping; the
sway; the sniper's sound.

**As built (2026-10-02, night).** `MatchOptions.Gunnery` (`Sim/Gunnery.cs`),
on in the game; off, every earlier pin holds. Six seeds, a minute of fighting
(`simcs aware ... senses [gunnery]`), and the motion audit (PlayMode,
category `Motion`, 75 s of a fight, `Logs/motion.txt`):

| | the Senses build | with Gunnery |
|---|---|---|
| bullets more across the lane than along it (straight up or down the screen) | 14% | 0% |
| shots across the two lanes at all | 29% | 5% (a machine gun's or a sniper's long diagonal, and what is lobbed) |
| bullets fired by a man on the move | 28% | 0% |
| bullets fired by a man on his feet | 18% | 2% (one chance in ten, a third as accurate) |
| man-seconds travelling on a knee or flat (the sim) | 174 | 0 |
| ... as drawn: share of travelling time gliding (the audit) | about 24% | 0.1% |
| ... travelling sideways or backwards to his facing | about 5% | 2.6% |
| men at rest whose legs are still walking | (0.45 of a walk, on average) | 1.5% of the time |
| bullets a minute | 528 | 224 |
| a man setting off | 11.2 | 6.6 |
| enemy squads within 15 m, neither firing (squad-seconds) | 16.6 | 6.2 |

What does it:
- **Lanes.** A bullet is fired at a man no further across than half as far as
  he is along (`Gunnery.InArc`), or at arm's length; a squad's threat is a
  squad in its own lane. Grenades, the M79, the RPG and the mortar still cross.
  The two lanes are now two fights: squads abreast in different lanes do not
  shoot at each other. If that reads as "don't see each other" it is one
  constant (`Tune.ArcSlope`) and one line in `Senses.Look` to give back.
- **Stance.** A man fires only when he has been still a fifth of a second; on
  his feet one chance in ten and a third as accurate. Inside ten metres fear
  no longer stops him firing or spoils his aim much, and nobody throws smoke at
  an enemy nearer than twelve: two squads that ran into each other used to lie
  seven metres apart, pinned, behind a cloud, for twelve seconds.
- **Movement.** A man on a knee or flat stays put; one with further than 1.5 m
  to go gets up (0.6 s from a knee, 1.2 s from flat, the length of the view's
  clips), runs, and goes down 0.4 s after he stops. In contact a squad runs
  (4 m/s, the run clip's own speed; it was a walk at double time); on the march
  it walks, and a man up with it keeps its pace instead of walking a second and
  standing two thirds of one. A squad falling back runs. A squad its lever
  holds does not assault out of the position.
- **The view.** One gait at a time at the rate that matches the man's speed
  (half way between the kneeling idle and the crouched walk he was a kneeling
  man gliding); his speed follows the sim's in a tenth of a second and stops
  when it stops; a man who is moving faces where he is going and does not have
  his rifle in his shoulder.
- **Rounds.** The `Fire` event carries where the round came down
  (`Gunnery.Miss`: past the man on its own line, a little wide). The view draws
  every round's path as a thin pale line gone in a blink, one in five from an
  automatic weapon as a tracer; a miss kicks up earth where it landed, or dust
  off the sandbags in front of a man behind a wall; the flash at the muzzle is
  shaped by the weapon (the M16's star, the long tongue of the M60 and the bolt
  rifles, a submachine gun's small ball).
- **The sniper.** His own recordings (`sniper_*`: the library's Tikka, a .308
  bolt action, already on disk; nothing downloaded), never rationed out behind
  the nearer rifles as it always was, heavier, with a slap back off the
  treeline a third of a second later.
- **Clipping.** Sandbag walls are drawn behind the band the lane's three files
  walk in (2.75 m behind its middle; the perimeter wall and the bunker's stood
  in it, and the third squad into a lane walked the length of the wall inside
  it). The bunker's roof is on posts at 2.05 m: a man can stand under it. The
  firebase's east wall has a gate where the far lane runs through it, and the
  watchtower stands behind that lane, not on it.
- **The plants.** Sway by species (the bamboo's tops move 40 cm, the palms'
  28, the trees' 16, ground plants 6) in gusts that cross the valley; the
  broadleaf textures read sharper near the lens (bias 0.5 to 0.15: that was a
  TAA setting, and the camera is on SMAA).

Balance, 96 seeds: Americans 54.2% (44.2 to 63.8), with the VC's first list of
squads again. A match is shorter: 128 s mean (180 under Senses), which the
length setting has to answer (§12.18 phase 2). The computer's squad costs it 28
(a player buying only line squads wins 16 of 24 as the Americans, 22 as the
VC). EditMode 57/57, PlayMode 15/15 (the audit is one of them), WebGL 29.84 MB,
real Chrome 6.1 to 6.5 ms a frame with nothing else on the GPU (the 15 ms of
§12.18 was the owner's browser running the game at the same time).

**Not done, or not sure of:** the gaits' true speed on these bodies could not
be measured by the feet (a running stride is mostly in the air, a walking toe
skims the ground), so the clips' own root travel is trusted, as before; a
kneeling man has no reload clip of his own and a kneeling man throwing a
grenade stands to do it; the bamboo's leaf cards themselves are unchanged
(sharper sampling and sway only); the mark at contact and the launcher sounds
of §12.18 are still open.

### 12.13 Corrections to earlier sections

Fold each of these in when its section is next touched.

- **§0 / §2:** the project runs 6000.6.3f1 (tech stream), not "Unity 6 LTS",
  until §12.9 decides otherwise.
- **§5.4:** movement is Mixamo clips on a Humanoid Animator, driven by the
  sim's speed and posture. The 0.20 s and 0.62 s timings stay, as gates.
- **§6:** physics stays out *on purpose* (§12.9); the module list grows;
  Addressables may become raw AssetBundles; "Decompression Fallback off"
  depends on the host (§12.11).
- **§8 rule 4 and the `ASSETS.md` "Audio" paragraph:** replaced by §12.6. The
  owner's note ("YOU SHOULD SOURCE IT!") is implemented there; tidy the
  sentence it was appended to.
- **§8 sources:** the additions and the rejection in §12.10.
- **§10:** replaced by §12.12.
- **README:** "Steps 2–10 — not started" is stale.

### 12.14 Owner questions and actions

1. **Mixamo downloads** need the owner's Adobe sign-in in a browser, and
   there is no public API. The agent prepares the exact clip list and
   settings (§12.3). Then either the owner downloads them, or the owner signs
   in on a browser the agent can drive.
2. **Music:** (a) a free licensed score (CC-BY or royalty-free, logged),
   (b) a commissioned one, or (c) no music, ambience only.
3. **Voices:** who records the radio and squad lines? Vietnamese lines need a
   native speaker, or they are left out.
4. **A reference frame for each Part 2 operation:** concept art in the spirit
   of `TARGET.jpg` (as the three.js project had `concept art.jpeg`), or bands
   derived from `TARGET.jpg`'s principles. §4's table exists only for
   `TARGET.jpg`.
5. **GitHub:** public or private repo? GitHub Pages like the 2D game, or a
   host that sets headers? (§12.11)
6. **Mobile:** confirm it is out of scope for the photoreal build (§12.8).
7. **Adobe installs, if the owner's plan includes them:** Audition (audio
   cleanup, batch loudness), Illustrator (line-art call-in icons), Substance
   3D Painter (soldier texturing). Only Photoshop, Premiere Pro, Media Encoder
   and Lightroom are installed now.
8. **The Unity LTS editor:** install it alongside the current one (several GB,
   through the Hub) for §12.9.
9. **Real-device numbers:** §11 Q4, still open.
