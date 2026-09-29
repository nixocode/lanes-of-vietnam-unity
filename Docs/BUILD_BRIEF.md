# LANES OF VIETNAM '65 — build brief

*Paste this whole file as the first message of a fresh session. It is the only
context the build needs.*

> ### ⬛ LOOK AT `reference/TARGET.jpg` FIRST
>
> It is in this folder (`concept art.jpeg` at the project root is the same image
> at full size — 2772×1504). **It is the specification for the look, the camera,
> the composition and the entire interface.** Every section below describes some
> part of that image. When this text and that image disagree, the image wins.
>
> Open it before you read further, and open it again before every visual
> decision you make.

---

## 0. What you are building

A single-map, real-time lane-tactics game set in Vietnam, 1965. The player
commands one side of a battle fought along a jungle track in front of a US
firebase — the ground in `reference/TARGET.jpg`.

It is the successor to a finished 2D canvas game. **This is a new project in a
new folder. Take the ideas and the hard-won lessons in this brief; take no code,
no textures, no models, no audio.** Not one file.

**One map. Built as well as a map can be built.** Everything that would have
gone into four more maps goes into this one: the light, the vegetation, the
dust, the way a man kneels, the way an AK sounds at three hundred metres against
the way it sounds at thirty. The test of success is the reference image — if a
frame of the running game can sit next to `TARGET.jpg` and hold its own, the
project has succeeded.

**Tone: photoreal, lush, and hot.** Not gritty, not desaturated, not stylised.
The reference is bright hazy daylight over dense green jungle, with real
vehicles, real webbing, real razor wire. It looks like a very good photograph of
a diorama, and that is the target.

**The feel the owner asks for, in his words: "sandboxy".** A place that behaves
consistently and rewards watching, over a checklist of mechanics.

---

## 1. Rules that do not bend

1. **Never deploy or publish without explicit approval.** A push to a public
   remote is a deploy. Ask, every time.
2. **Assets: CC0 only, from reputable sources only.** Verify and record the
   licence of every asset in `ASSETS.md` — source URL, licence, date, and what
   it is used for. If the only thing that fits is not CC0, stop and ask; do not
   "probably fine" your way past it. See §6 for the honest assessment.
3. **Never commit a test hack.** Muted audio, a skipped intro, a forced spawn, a
   debug camera: fine in a session, never in a commit. Grep the diff before
   every commit.
4. **Commit locally as you go, with the reasoning in the message**, carrying the
   measurement that justified the change.
5. **Do not break or quietly remove something that works.** If a change costs
   something, say what it costs.
6. **Work power-efficiently.** Kill every dev server, background job and watcher
   when you stop. Never leave anything polling.
7. **Report faithfully.** If a thing is not done, say so. If a measurement
   disagrees with what you hoped, report the measurement.

---

## 2. The stack

**Decided, with reasons. Deviate only with a better reason, stated in writing
first — and never for familiarity alone.**

| | |
|---|---|
| Language | **TypeScript**, strict |
| Build | **Vite** |
| Render | **Three.js**, `WebGPURenderer` with automatic WebGL2 fallback |
| Assets | **glTF 2.0** (`.glb`), Meshopt-compressed, KTX2/Basis textures |
| Audio | **Web Audio API**, hand-built graph — no wrapper library |
| Authoring | **Blender** (headless Python for anything repeatable) |
| Tests | **Vitest** for the sim; a headless harness that runs it in Node |
| Physics | **None by default.** Rapier (wasm) only if ragdolls or debris justify it, behind a flag |

### Why 3D, when the last one was 2D

The old game used pre-rendered sprite atlases, and that one decision produced
most of its pain. Measured:

- **97 MB of decoded atlas** for 13 unit types × 135 frames against a 200 MB
  budget, so every new animation was a memory negotiation.
- A retargeting pipeline that silently shipped **legs smeared into ribbons on 11
  of 13 body types**.
- An outline post-pass that **double-stroked an entire tree** when its ledger
  went missing, and had already shipped three clips with no outline at all.
- **No real animation blending** — clip selection with a dwell timer, because
  cross-fading baked frames is all you can do.
- A hard ceiling on figure detail at ~84 px tall.

The reference image needs soldiers rendered at roughly **16% of frame height**
with visible webbing, helmet straps and weapon detail — nearly double the old
game's figures, at many times the fidelity. Sprites cannot get there. Real
skeletal animation deletes every problem above and is the only route to the ask.

### Performance budget — non-negotiable

- **60 fps locked at 1920×1080** on an Apple-silicon laptop, with 60+ soldiers
  on screen.
- **Never below 45 fps** in the worst case (artillery, smoke, 80 men).
- Adaptive quality steps down automatically under load, in this order:
  resolution scale → shadow resolution → post passes → particle budget.
- Frame budget stated per system in `PERF.md` and **measured, not assumed**.

Heavy use of **instancing** for vegetation and **skinned-mesh instancing** or
GPU skinning for crowds is expected from the start, not retrofitted.

---

## 3. Architecture: the simulation is not the renderer

**The single most important structural decision, and the previous project got it
right — copy it.**

```
src/
  sim/          pure TypeScript. Zero imports from three, zero DOM, zero audio.
  render/       everything Three.js. Reads sim state, never writes it.
  audio/        reads sim events, never writes state.
  ui/           reads sim state, sends intents.
  assets/       loaders, LOD, streaming.
tools/
  simnode.ts    runs the whole sim headless in Node. No browser, no GPU.
  balance.ts    plays fixed seed sets, reports win rates with intervals.
  capture.ts    drives the renderer to a deterministic frame and saves it.
```

**Rules:**

- The sim runs at a **fixed timestep** (60 Hz). The renderer interpolates
  between the two most recent sim states. Never step the sim from the render
  loop's variable `dt`.
- The sim is **fully deterministic**: same seed, same inputs, same result,
  bit-for-bit, in Node and the browser. Assert it in CI.
- The sim **may not read the clock, `Math.random`, or the DOM.** All randomness
  comes from an injected generator (§8, finding 9 — subtler than it sounds).
- The renderer keeps its own cosmetic randomness from a **different** generator,
  or a browser frame consumes draws a headless run does not and the two diverge.

The payoff is immediate: the previous project played 10 full matches across 5
maps in **17 seconds of CPU** with no browser. That is what made balance
measurable at all.

---

## 4. The look

**Everything in this section is measured off `reference/TARGET.jpg`. These are
targets to hit, not vibes to approximate.**

### Camera and composition

- **Side-on, slightly elevated, near-orthographic but with real perspective.**
  It pans along the line and dollies in and out. It does **not** orbit. The
  composition is authored.
- Play area is a **wide letterbox, about 2.7:1**, framed by the UI above and
  below.
- Four clear depth bands, and the staging must keep them separate:
  **sky and mountains → treeline → far lane → near lane.**
- Near-lane men overlap and are partly occluded by foreground vegetation. Far
  lane men are smaller, higher in frame, partly hidden by grass and terrain.

### Tone and atmosphere — the measured targets

Aerial perspective in the reference, by band:

| band | L\* | saturation |
|---|---|---|
| sky + mountains | **69.7** | **11.1** |
| treeline | 40.2 | 16.7 |
| far lane | 32.8 | 21.8 |
| near lane | **31.2** | **33.8** |

**Distance lightens AND desaturates, monotonically across all four bands.
Saturation triples from the mountains to the foreground.** Get both halves. The
old game did the first and not the second — its far ground came out *more*
colourful than its near ground, which is the opposite of air, because the haze
tint it used was itself a saturated colour laid on as a wash.

Whole play area:

- **L\* range 89** (p2 = 4, p50 = 41, p98 = 93) — real blacks *and* real whites.
- **17.8% of pixels below L\*20**, **15.8% above L\*75**. Both ends occupied.
- **Standard deviation 24.7.** For scale, the old game's play band managed 10.6.

If your frame measures much flatter than this, it will look like the old game no
matter how good the models are. Measure it with `tools/capture.ts` and compare.

### Light

- **Bright, hazy, overcast-lit daylight.** High key, soft diffuse shadows, no
  hard raking sun. The sky is the brightest thing in frame by a wide margin.
- Real **volumetric cloud** sky with depth, not a gradient.
- **Exponential height fog** tinted toward the sky, doing the aerial perspective
  above. Mountains sit back as blue-grey silhouettes in several receding ridges.
- Post chain: TAA → SSAO → bloom (tight threshold) → slight DOF on the near
  foreground only → filmic tonemap → fine grain. Each subtle enough that turning
  it off is noticeable but turning it on is not obvious.

### Living detail

- **Grass that responds** — parts around men walking through it, flattens under
  prone men, lashes in rotor wash.
- **Dust** kicked by boots, impacts and rotors, and it hangs.
- **Smoke that occludes** — marking smoke, and the grey pall that builds over a
  position that has been firing.
- Birds leaving the treeline when artillery walks in. Insects in the light. The
  place should feel inhabited before anyone shoots.

---

## 5. The map

**The ground in the reference image, built out to a full battlefield.**

### West end — the US firebase

Dug in on the left: a **timber watchtower** with a US flag, sandbag revetments,
earth berms, **coils of concertina wire**, ammo crates, fuel drums, and a
vehicle park — **M35 cargo trucks and an M151 jeep**. This is the anchor of the
US line and the thing the VC player is attacking. It must read as *built*:
occupied, worn, sandbagged, with the tower giving real elevation to whoever
holds it.

### The middle — the track and the clearing

A **dirt track** running the length of the map through scrub and elephant grass,
with folds of ground, low earth banks and scattered rock that men actually fight
from. This is where the fight resolves. Cover here is the tactical vocabulary:
bank, berm, wire, crater, dug position.

### East end and the deep background

Dense **jungle**: banana and elephant-ear broadleaf, bamboo clumps, coconut and
areca palms, a tall broadleaf canopy. Behind it, **layered mountain ridges**
receding into haze, and the volumetric sky.

### The lane structure

Keep the lane idea from the old game, but make lanes **terrain**, not stripes:

- **Near lane** runs along the front of the position, through heavy foreground
  vegetation.
- **Far lane** runs higher, along the track and the treeline.
- Crossing between them happens at **real crossings** — a gap in the wire, a dip
  in the bank — not anywhere along the line.

---

## 6. The soldiers

**"Full new, more realistic and detailed soldiers" is the headline ask. It is
the hardest part of the project. Start it first.**

### The spec

- **8–15k triangles** at LOD0, three LODs, PBR with normal, roughness and AO.
  Readable at 40 px tall and at 400.
- **Two force appearances**, both visible in the reference:
  - **US** — olive drab fatigues, **M1 helmet with cover and band**, flak vest,
    webbing, canteens, M16 and M60.
  - **VC / NVA** — black pyjamas and khaki uniforms, **pith helmets**, bush
    hats, chest rigs, AK-47, SKS, **RPG-7**.
- **One base body, many men.** Height, build, skin tone, uniform wear, sleeves,
  headgear and loadout all vary per soldier. Two identical men should never
  stand next to each other.
- **Gear on sockets**, not baked. Gear sways.

### The animation, which is the whole point

Build a real **blend tree**, not a clip switcher:

- **Locomotion**: idle / walk / run / crouch-walk / crawl, blended on speed,
  with correct foot phase so men do not skate.
- **An additive aim layer** so a man aims anywhere *while* moving. The old game
  could not do this; it was its single biggest limitation.
- **Postures**: standing, kneeling, prone — and **real transitions between all
  three, both directions.** The old game had one transition clip and snapped the
  rest. The reference shows standing, kneeling and prone men side by side; all
  three have to be equally good.
- **Weapon handling**: shoulder, fire with recoil through the whole upper body,
  reload with magazine and bolt, grenade throw, RPG kneel-and-fire with
  backblast.
- **Foot IK** onto uneven ground, **look-at IK** for the head, **hand IK** to
  keep both hands on the weapon throughout.
- **Reactions**: hit by location, suppressed (a flinch and a duck, not a state
  label), and **several deaths that read differently**, with a short ragdoll
  blend at the end if physics is in.

### The asset problem — read before starting

**Realistic, CC0, rigged, animated soldiers do not exist off the shelf.** Do not
burn a day discovering that. The honest paths, best first:

1. **Build them.** A **MakeHuman** base (its output is CC0), clothed and kitted
   in Blender, textured from **ambientCG** (CC0) fabric and leather scans,
   rigged with Rigify. This is the path that reaches the reference. It is days
   of work, not hours.
2. **CC0 stylised bases** (Quaternius, Kenney) heavily reworked — faster, but
   they will not read as the reference does, and you must say so rather than
   quietly lowering the bar.
3. **Motion**: hand-key the core set in Blender, or retarget from a CC0 motion
   library. The CMU mocap database is usable but **not CC0** — ask first.

**Resolve this in the first working session and report what you chose and what
it costs.** If everything acceptable is non-CC0, stop and ask.

### Known trap, carried over

The old game retargeted animation between six donor bodies by bone name and
shipped ribboned legs on 11 of 13 units, because bone **rest orientations**
differ between rigs — copying local rotations is only valid when rests match. If
you retarget anything, carry rotations **relative to each rig's rest**
(`C = Rt⁻¹·Rs`, applied as `C·q·C⁻¹`), and **check every body variant at a zoom
where a smear cannot hide.** Checking the representative one is checking the case
that cannot fail.

### Other assets the reference demands

Watchtower · sandbag revetments · earth berms · concertina wire · M35 truck ·
M151 jeep · ammo crates · fuel drums · US flag · banana and elephant-ear
broadleaf · bamboo · coconut and areca palm · tall grass · broadleaf canopy ·
dirt track · scattered rock.

---

## 7. The interface

**The reference specifies the UI as precisely as it specifies the art. Build
this, not something of your own devising.**

Military stencil typography throughout. Olive, brass and amber on dark. A thin
frame around the play area with a subtle vignette.

### Top bar — the command strip

- **Left:** `★ US / ARVN` with a **segmented morale bar** filling green.
- **Right:** `VC / NVA ★` with a segmented morale bar filling red.
- **Centre, top:** two **diamond objective markers**, one per lane, coloured by
  who holds them.
- **Centre:** the **orders line**, amber stencil —
  `ORDERS 2/5 — SET PUNJI STAKES IN THEIR PATH [Q]`. This is a guided-objective
  system: a short ordered list of field orders, each completing on a real event
  in the sim, shown one at a time. **Guidance, never a gate** — a player who
  ignores it must still be able to win.
- **Below the orders line:** a wide **tactical strip** — a schematic top-down of
  both lanes showing friendly and enemy unit pips, the front-line trace for each
  side, and the camera's position along the map.
- **Right:** small square buttons — **pause**, **speed (`1×`)**, **`SND`**,
  **`MUS`**, and a settings button. All must show their own state; a toggle that
  does not visibly toggle is a bug.

### Bottom bar — the deck

- **Left:** a large **CP counter** in amber (`82 / CP`) — the command-point
  economy that funds everything.
- **Centre:** purchasable squads as **cards, grouped under headers**:
  **`LINE` · `SUPPORT` · `SPECIAL`**. Each card carries a **portrait** of that
  unit type, its name in stencil caps, its cost, and small pips for composition.
  Unaffordable cards dim; cooling-down cards show their cooldown.
- **Right:** **call-in cards** with line-art icons and costs —
  `PUNJI PIT 12` · `TRIPWIRE 18` · `SPIDER HOLE` · `TUNNEL 15` for the VC, and
  the equivalent fire-support set for the US.

**The two sides do not share a toolkit and the UI must show that.** The US buys
fire support; the VC buys traps, tunnels and infiltration. This asymmetry is the
game's best idea — protect it.

---

## 8. The game

Carried over from the old design, which worked. Build the systems; the numbers
are yours to find.

- **Squads, not units.** The player buys and commands 3–6 man squads. Individual
  soldiers are simulated but never ordered directly.
- **Formation.** Men hold slots on a squad anchor. *(§9, finding 1 — the worst
  bug in the old game lived here.)*
- **Cover is a decision, not automatic.** Berms, banks, wire, craters and dug
  positions. Capacity is limited and crowding costs. A squad that holds one
  position too long gets **ranged in** — a warning, then accurate fire — so
  holding ground is a timing decision rather than a free win.
- **Suppression is the core mechanic.** Incoming fire builds pin; pinned men
  drop, shoot less and stop advancing. It must be **visible without a label**:
  dust walking the ground in front of them, heads down, a man flattening.
- **Bounding.** Nobody crosses open ground while it is being swept unless
  someone is covering them, or smoke is down. This is the tactical heart.
- **Morale, not hit points, decides it.** Casualties and lost ground drain a
  side's will; when it empties, that side breaks.
- **Command points** fund reinforcement and call-ins.
- **Call-ins feel like calling for something**, not casting a spell: a radio
  exchange, a delay, then rounds walking across the grass.
- **Concealment.** The VC fight from the treeline and the grass and are not
  visible until they fire or are found.
- **Veterancy.** Squads that survive get steadier, not stronger.

**No campaign. One map, both sides playable**, with an authored opening and a
real ending in both directions.

---

## 9. What the last game learned the hard way

**Every one of these cost real time to find. Do not rediscover them.**

### Simulation

1. **Formation slots must be recomputed from the LIVE roster every tick.** The
   old game assigned a slot at spawn and never revised it as the squad took
   casualties, while the anchor was the mean of the living — so lone survivors
   walked off the map (traced to x=12091 in a 2560-wide world) and full squads
   were yanked backwards every tick. Fixing it moved late-game firing from 3.7%
   to 18.1% and kills from 263 to 405.

2. **"Is moving" must mean measured progress over a window**, not "moved this
   tick". The old flag was true on any sub-pixel jitter, which left every stance
   lock permanently bypassed. Sample over ~0.3 s.

3. **Every state that drives animation needs hysteresis and a minimum dwell.**
   Each input that picks a clip is a hard threshold, and in a firefight they all
   sit on their boundary and chatter — 60–80 clip changes a minute on the worst
   man, which no cross-fade can resolve. One dwell at the point they funnel
   through beats debouncing each input.

4. **Rules written for the standing fight break in other modes.** Three separate
   rules silently made two missions unwinnable: attackers bled morale for not
   yet holding objectives they had come to take; defenders collected income for
   ground they never had to take; and defenders "broke through" by walking off
   the attacker's back edge, which was **100%** of the attacker's morale loss.
   Both were lost 0 times in 24 before this was found. **If you add a second win
   condition, audit every rule against it explicitly.**

5. **Feedback that never fires is a bug class this genre breeds.** The old game
   had a dead message (its state was wiped before the message could fire), a
   dead timer, a dead win condition (hardcoded to one side), and a method called
   but never defined (every click threw). **Enumerate every message, sound and
   effect and prove each fires in play.** 27 of 29 did; both exceptions were real
   bugs.

6. **Pacing in a lane game is structural.** Men spend ~77% of the time moving
   and 98% of that with no enemy in range. A march speed-up was written,
   measured and reverted. Design around it; do not tune against it.

7. **The AI must use its own side's toolkit.** The two sides had completely
   different tools and a shared policy meant one side never used its own. Worse
   and still unresolved: running the AI's policy from the player's seat *lost*
   matches the dumb buy-only script won — 11/48 against 27/48.

8. **Do not balance off small samples.** Win rates from 12 matches are noise.
   Use 48+ independent seeds, report a confidence interval, and compare
   **paired by seed** (McNemar) rather than comparing two win rates.

### Method

9. **Seed the sim from day one — then check adjacent seeds are INDEPENDENT.**
   This is the subtle one and it cost the most. The old game used a textbook
   LCG, which made a match reproducible and a *seed set* worthless: over 4000
   consecutive seeds the correlation between adjacent seeds' first draw was
   **0.998**. One mission read 8/12 on seeds 1000–1011 and 0/12 on 2000–2011
   with identical code, and still disagreed 44% vs 15% at N=48. Use a generator
   with real avalanche (**mulberry32**, PCG, splitmix) and **verify the
   adjacent-seed correlation yourself** before trusting any block of seeds.

10. **Measure, or do not claim it.** Every strong visual intuition in the last
    session was wrong until measured: "there are no contact shadows" (there
    were), "the ground is a flat fill" (local sd 2.6–6.3, modelled), "the
    sprites are flat-lit" (32 L\* of modelling), "the particle cap is eating the
    combat FX" (7.5% dropped). **Measure first. Then look. Then change.**

11. **Frame rate cannot be measured inside an automation browser.** Repeated
    pixel readbacks de-accelerate a canvas, and a hidden pane throttles rAF and
    ResizeObserver. A "172 ms frame" and a "software-rasterised canvas" were
    both the instrument. Get real numbers from the owner's machine.

12. **Never diagnose art from a screenshot crop.** Render it twice and diff the
    pixels. Three wrong diagnoses in one session came from exactly that.

13. **Relative luminance in linear light is the wrong ruler for a dark scene** —
    it crushes everything below mid-grey and once made a perfectly good night
    map look illegible. Use **CIELAB L\***.

14. **Verify a patch landed, in a separate command.** `grep -c` exits 1 on zero
    matches and silently skips the rest of an `&&` chain — a whole change was
    never applied and two "conditions" were measured against identical code.

15. **`pgrep -f "foo.js"` matches the shell running the grep.** A wait loop built
    on it never exits. Two did.

### Asset pipeline

16. **Anything applied in place to an asset needs an idempotence marker inside
    the file**, not a side-ledger. The old outline pass kept mtimes in a
    gitignored ledger; a tree with assets but no ledger read as "nothing
    processed" and processed everything twice — +14% footprint and a visibly
    heavier image across every frame.

17. **A partial rebuild is not a subset of a full one.** Rendering one clip gave
    different pixels depending on which clips ran before it in the same session,
    because the rig carries state across clips. Preview with partial builds;
    rebuild whole before shipping.

---

## 10. Measurement discipline

Build these three harnesses **early**, before they are needed. They turn opinion
into fact.

- **`tools/simnode.ts`** — the whole sim in Node, no browser. Must run a full
  match in well under a second.
- **`tools/balance.ts`** — fixed seed sets, Wilson intervals, paired
  seed-by-seed comparison against a saved baseline. Two scripted player profiles
  that bracket a real one: a floor that does almost nothing, and a ceiling that
  uses the tools.
- **`tools/capture.ts`** — drives the renderer to a deterministic frame and
  saves it, so any visual change is A/B'd from an **identical frozen frame**.
  Half the visual arguments in the old project were lost to comparing two
  different moments.

**Every visual claim gets a number as well as a picture**, in the units used in
§4: per-band L\* and saturation, play-area L\* range, dark% and light%, standard
deviation. Compare them against the reference table. That table is the
acceptance test for the look.

---

## 11. Build order

**In order. Do not start the next until the previous is genuinely done, and show
the owner each one.**

1. **Vertical slice, graphics only.** One soldier, correctly lit, standing in a
   patch of the real ground, at a locked 60 fps, full post chain, measured
   against the §4 table. No gameplay. *This de-risks the entire project.* If it
   does not hold up beside `TARGET.jpg`, nothing later will.
2. **The soldier moves.** Blend tree, additive aim, foot IK, three postures and
   every transition. One man, on the keyboard.
3. **The map, finished.** Firebase, track, jungle, mountains, sky, vegetation,
   the whole light rig — still no gameplay.
4. **The sim, headless.** Squads, movement, cover, suppression, morale, LOS.
   `simnode` green and determinism asserted before one line of it is rendered.
5. **Join them.** Sim state drives the renderer through interpolation.
6. **The interface.** §7, completely — it is a third of the reference image.
7. **Audio.** Positional, distance-filtered, reverb by environment, real
   occlusion. Guns sound different at 30 m and 300 m.
8. **Balance**, with the harness, the seeds and the intervals.
9. **Polish, measured.**

---

## 12. Done means

Per change:

- `simnode` green; determinism asserted in Node **and** browser.
- 60 fps at 1080p with 60 men, measured on the owner's machine.
- Memory reported against budget.
- Every asset in `ASSETS.md` with its licence.
- Visual changes shown as an A/B from an identical frozen frame, with the §4
  numbers beside the picture.

At the end: a `README.md` a stranger can run, a `PLAN.md` kept current and
carrying findings the way §9 does, and an honest list of what was not finished.

---

## 13. Ask the owner

Batched, early. Keep working on what is not blocked while you wait.

1. **Soldier assets** — which of the three paths in §6, once costed. This
   decides the look of the whole game.
2. **Non-CC0 motion data** — allowed, or not?
3. **How long a match should run.** The old game's standard battles resolved in
   2–3 minutes; its siege ran to 10.
4. **Real device numbers** — frame rate, frame time and thermals on his machine.
   You cannot measure this from your side.

---

## 14. A note on ambition

The previous game is 21,878 lines and genuinely good at what it does. It is not
the bar. The bar is `reference/TARGET.jpg`.

Spend the time on the light, the vegetation and the men. When the choice is
between one more mechanic and one more day on how a soldier goes to ground, take
the soldier.
