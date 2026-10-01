# Assets

**Free, highest quality, reputable sources.** Changed by the owner on
2026-09-29 from the brief's CC0-only rule — see PLAN.md §8. CC0 is preferred
when two candidates are equal; anything else is logged here with the
obligations its licence carries, and those obligations are met.

Every file used in this project is logged here with its licence, source URL,
the date it was fetched and what it is used for. Asset selection is delegated
to me; an asset whose licence is unclear is not used.

**Adobe tools are in scope** (owner, 2026-09-29): Mixamo through the owner's
Adobe ID, Photoshop 2026 and the rest of the installed Creative Cloud apps may
be used to create and process assets and textures.

## In the project

### Fonts — `Assets/_Project/UI/Fonts/`

All **SIL Open Font License 1.1**, from the official Google Fonts repository
(`github.com/google/fonts`), fetched **2026-09-30**. Each ships unmodified with
its `OFL-*.txt` beside it, and the licence travels with any build that uses it
(PLAN §12.4). TARGET.jpg's interface uses three faces, not one: a stencil for
the faction labels and group headers, a typewriter for the orders line, and a
condensed sans for card names — brief §7 says the image wins over the text.

| file | family | used for | sha256 |
|---|---|---|---|
| `StardosStencil-Regular.ttf`, `-Bold.ttf` | Stardos Stencil (Vernon Adams) | faction labels, headers — owner-approved OFL face, 2026-09-28 | `208b13d15387c282a1c0c439a8e4c38809243d15c361b31da440b25a7e4f39ae`<br>`6b15f50b1b358512d922b5f11937af17e90704587e1d7fb009f1715d2d5dfa74` |
| `BlackOpsOne-Regular.ttf` | Black Ops One | bold military stencil, candidate for `US / ARVN` | `282a825b5f294377387e3969f765408157dbea8da0f5d0aae68c6bc704b145b3` |
| `CourierPrime-Regular.ttf`, `-Bold.ttf` | Courier Prime | the orders line (typewritten field orders) | `72f793376f8e2841656bf21d77a5de010f2929bd6956a22ee848ad0c7eb978af`<br>`ff1f38786c849d1c41fa8e447960abdb2bd75fdfb0cfcdeb524fad65a5af3638` |
| `BebasNeue-Regular.ttf` | Bebas Neue (Dharma Type) | card names and numbers | `08e4623805102d819f58601e46e345648846075e363b2ceb23313c2d1c83ec73` |

### Sky — `Assets/_Project/Art/Sky/`

**Sunflowers (Pure Sky)**, Poly Haven, **CC0**. Original by Sergej Majboroda,
sky edits by Jarod Guest; 6550 K white balance, 23 EV captured, midday, partly
cloudy. Fetched **2026-09-30** from
`https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/16k/sunflowers_puresky_16k.hdr`
(page: `https://polyhaven.com/a/sunflowers_puresky`). The raw HDRIs are kept in
`SourceArt/polyhaven/` (gitignored, re-fetchable); what ships is baked from them
by `tools/blender/sky_bake.py` (sun placed at azimuth −125°, behind the camera's
left shoulder; image rotated −161.2°; exposure k 0.840).

Chosen because its clouds are cumulus in a
hazy blue, the reference's sky, and the sun is high (43°) so the light is
tropical midday rather than golden hour.

| file | what | sha256 |
|---|---|---|
| `sunflowers_puresky_16k.hdr` (source) | 16384 × 8192 HDR, 266 MB | `5ed829797dc1c5c555591135bb01e1ce2339f13b1627dcabf39616434bfc090d` |
| `sunflowers_puresky_1k.hdr` (source) | 1024 × 512, for previews | `39a18be788fda30e1b1929d4ebd78b5da14433a6e2271eff1928a35e481c5111` |
| `sky_window.png` | the visible window, az −32..32°, el −2..24°, 2560 × 1040 at 40 px/degree | `f7dd88246ced2275db0d6fc2a9c9c09e48c44bfe21961e6e9959e7ab3618929e` |
| `sky_full.png` | whole sphere, 1024 × 512, sun clamped | `5ade63bd00120a05de5d63043baf43fc7226f54cfd0d4f3b923c7c4dbc57fb87` |
| `sky.json` | k, window, sun direction and irradiance, 64 × 32 radiance grid | `8c177aede64e05e36c5f6f7116565f1640038f657948565fca88c0ade4452f30` |

### Ground — `Assets/_Project/Art/Terrain/`

Four scanned layers, all **CC0**, fetched **2026-09-30** as 2K PNG, packed by
`tools/art/terrain_pack.py` into one albedo (RGB + height in alpha) and one
normal per layer at 1024², crunched (quality 75). Raw downloads are in
`SourceArt/` (gitignored). In the WebGL build the eight maps are **3.7 MB**.

| slice | layer | source | scan | laid | albedo Y | URL |
|---|---|---|---|---|---|---|
| 0 | grass floor | ambientCG **Grass003** | 1.4 m | 1.6 m | 0.064 | `https://ambientcg.com/get?file=Grass003_2K-PNG.zip` |
| 1 | verge | ambientCG **Ground047** | 3.0 m | 3.0 m | 0.061 | `https://ambientcg.com/get?file=Ground047_2K-PNG.zip` |
| 2 | track | ambientCG **Ground103** | ~2 m (not published) | 2.2 m | 0.154 | `https://ambientcg.com/get?file=Ground103_2K-PNG.zip` |
| 3 | disturbed earth | Poly Haven **brown_mud_03** (Rob Tuytel) | 1.3 m | 1.5 m | 0.077 | `https://dl.polyhaven.org/file/ph-assets/Textures/png/2k/brown_mud_03/` (`_diff`, `_nor_gl`, `_disp`) |

**How they were chosen.** 97 candidates were measured (72 from Poly Haven, 25
from ambientCG) against the ground in TARGET.jpg: the track is tan-olive (hue
81–94, chroma 10–19) and the grass olive (hue 92–96, chroma 15–22). About 55
were compared on contact sheets. The finalists were viewed tiled 2 × 2 for
seams and repeats, then rendered in the frame and through the field glasses.
- **Grass003** is broad-bladed carpet grass, the Southeast Asian kind; it tiles
  seamlessly. It is dark (0.064) because a dense sward shades itself.
- **Ground047** is dirt broken by clumps of grass, which is what a verge is.
- **Ground103** is the closest match to the track (hue 77, chroma 16).
- **brown_mud_03** is dug clods; the craters and trench spoil need freshly
  turned earth.

**Rejected:**
- **Grass004** (from the plan's vetted set): lawn green, hue 114 at chroma 35.
- **Ground037** (from the vetted set): bright moss, albedo 0.30.
- **Poly Haven `grass_ground`**: tried first, as the grass floor. It is dry
  European lawn and rendered as sand.
- **Poly Haven `grass_path_3`**: tried as the verge. A broad bright blob repeats
  every tile.
- **Poly Haven `brown_mud_02`**: a dark grid shows at its tile edges.
- **Red laterite scans** (`red_laterite_soil_stones`, `red_mud_stones`,
  `red_dirt_mud_01`): hue 48–60, redder than anything in the reference.

| file | sha256 |
|---|---|
| `Grass003_2K-PNG.zip` (source) | `82a1db5a546134d06b99c564ef20836828239cd9264dc6995f33cb81242d938f` |
| `Ground047_2K-PNG.zip` (source) | `742a44e6f25e85a2ba135e5bd95c97e15414e79b8e0d8f420a26ce32c61b3bce` |
| `Ground103_2K-PNG.zip` (source) | `91dee59d83707a47d5c641ef193eac2473cffb7952df45bc2916aff63306acf3` |
| `brown_mud_03_diff_2k.png` (source) | `ae08382a69e1e3186e08fbf53d467d8a4e1ca7b700db2e6981e563e2109d1e16` |
| `brown_mud_03_nor_gl_2k.png` (source) | `bdfbef99813e714883271d1d1a92cd5790e22bd9c8bf54318a4d489cdc60aa3c` |
| `brown_mud_03_disp_2k.png` (source) | `fac05fffc977b0b2c522660532aac6cbb541471936caa216e03b409073c6977d` |
| `ground_0_grass_floor_albedo.png` | `50297f88d69bc7c24f54bb5ebe511e90995d2147af58daaafb53e260e7e83652` |
| `ground_0_grass_floor_normal.png` | `3ce82d4a75c41e4b46230a00479e1b16faa6daa0a1523f9ffffc5ed2d24afcf2` |
| `ground_1_verge_albedo.png` | `aa5e4b21f1fa6067baedeaecdb434d6cf669c868a2b608af3573eda70213a943` |
| `ground_1_verge_normal.png` | `67f13de261acb1e3d62c80798e96ce59daf56f2a96e55eb72dede2463e851cdc` |
| `ground_2_track_albedo.png` | `a34d1024ef6092f689020fe9a4fbc71f46bcf0a5ba064424834aa2799bc72993` |
| `ground_2_track_normal.png` | `615abea38cbecac061cb35483a28f0d1f18e0ac03b89408f5e577328f263e9f8` |
| `ground_3_disturbed_earth_albedo.png` | `418a06647f450156440b7a39c1152d5a4399161eb9843f5c9d70d0b712f5b5e6` |
| `ground_3_disturbed_earth_normal.png` | `b8a71bcc585081f49057f900020c5aa3bd673c2f888c7a329c5a2391482ccbda` |

### Plants — `Assets/_Project/Art/Plants/`

Scanned Poly Haven models, all **CC0**, fetched **2026-09-30** as 2K glTF from
`https://api.polyhaven.com/files/<id>`. `tools/blender/plant_bake.py` bakes them
into billboard atlases. Every variant is rendered by Cycles from the game's view
(orthographic, 6° down) into these maps:
- albedo and coverage;
- the normal, in the bake camera's frame;
- ambient occlusion;
- **sunlit**: cos × visibility under the game's own fixed sun, from sky.json.

The game lights them with its own sun and sky, so the triangle count of a source
costs nothing at runtime. A 3.9 M-polygon tree is one quad. All eight atlases
come to **2.3 MB** in the WebGL build (crunched).

| species | Poly Haven id | authors | variants | source polygons | laid in the game as |
|---|---|---|---|---|---|
| fern | `fern_02` | Rob Tuytel, Rico Cilliers | 4 | 784–2,384 | foreground and understory, 0.45–2.4 m (bird's-nest fern) |
| calathea | `calathea_orbifolia_01` | Rob Tuytel, Rico Cilliers | 5 | 1,802–5,904 | foreground, understory |
| anthurium | `anthurium_botany_01` | Rob Tuytel, Rico Cilliers | 6 | 7,664–15,808 | foreground, understory |
| pachira | `pachira_aquatica_01` | Rob Tuytel, Rico Cilliers | 4 | 5,091–28,105 | treeline edge, saplings, 1.2–5.5 m |
| ficus | `potted_plant_01` (pot, pebbles removed) | Rico Cilliers | 1 | 113,908 | treeline edge, scrub |
| aroid | `potted_plant_02` (pot, soil removed) | Rico Cilliers | 1 | 10,692 | occasional scrub plant (a cultivated, variegated form) |
| island_tree | `island_tree_01`, `_02` | Rob Tuytel, Rico Cilliers | 2 | 1.07–1.60 M | the lower storey of the treeline |
| jacaranda | `jacaranda_tree` | Rob Tuytel, Rico Cilliers | 1 | 3.86 M | canopy trees, the broad umbrella crowns |

**Rejected:**
- **`shrub_02`**: sparse and willow-like, with only 4–7% coverage.
- **`island_tree_03`**: its mesh carries a patch of sand at its foot.
- **`weed_plant_02`** (7.5 cm) and **`nettle_plant`** (22 cm): too small to read
  from this camera.
- **`grass_medium_02`**: tufts of 16–40 cm, where elephant grass is 2–4 m.
- Nothing on Poly Haven is a palm, bamboo or banana.

**Grass, built from scanned blades** (nothing scanned is tall enough). The
blades are ambientCG **CC0** foliage atlases, fetched **2026-09-30** as 2K PNG
from `https://ambientcg.com/get?file=<id>_2K-PNG.zip`.
`plant_bake.py` finds each blade in an atlas by labelling its opacity, then
builds clumps of curved strips textured with those blades. They lean and droop
more at a clump's edge than at its heart.
- **elephant_grass**: 6 variants, 2–3.1 m, from Foliage001 and Foliage008
  blades, with Foliage002 seed plumes above.
- **grass_tuft**: 6 variants, 0.45–1 m, from Foliage001, Foliage008 and
  Foliage005 (including its dry blade).
- **Rejected:** Foliage006's lime green (albedo G 0.27) is a lawn's colour.

| source | sha256 |
|---|---|
| `Foliage001_2K-PNG.zip` | `32121763ebc2a1adca7ea64dbeb20ee759ea101bc79f6a066cbc2d39ae1dfdaa` |
| `Foliage002_2K-PNG.zip` | `33cabe1ad1f21d9fcecbc55f80cbe5ae7ce365a0eee240643b628dd26a5e49a1` |
| `Foliage005_2K-PNG.zip` | `0bb1a252473d9d349d5dc14e4cb6895b34f4647617bf88b5540c059855bddb1e` |
| `Foliage008_2K-PNG.zip` | `9d09de2c1cc96b818485984e1f6fcacaf11c4119b28f988689cf6c5d82fad98b` |

**Coconut palm, built** (the fallback below, used while the Sketchfab palms
wait for a login):
- **Trunk:** Poly Haven **`palm_tree_bark`** (Dimitrios Savva, photography;
  Rico Cilliers, processing; **CC0**; 1.3 m scan), fetched 2026-09-30 as 2K PNG.
  Grey, ringed by leaf scars, which is exactly a coconut trunk.
- **Leaflets:** Foliage008's scanned blades.

`plant_bake.py` builds four variants, 13.8–16.1 m of trunk. Each has:
- a trunk that leans and curves, swollen at the foot;
- 20–22 fronds at golden-angle spacing, arching and drooping, with leaflets
  hanging from both sides in a V;
- 2–4 dead fronds, browned from the same scan;
- a bunch of nuts.
They are laid in the game at 10–14 m.

| file | sha256 |
|---|---|
| `palm_tree_bark_diff_2k.png` (source) | `5ecba9e0d2179b5f0fb553f7ddfc8092e0596162aeeeebbe1cc2c4f55f2856d4` |
| `palm_tree_bark_nor_gl_2k.png` (source) | `8aace15f59844d98825b3623edfa22d47f7d14fa772d1df448e1a0d5a9eebeca` |

**Bamboo, built** (the fallback, like the palm). Clumping Bambusa, the kind
that walls a Vietnamese village. The sources are ambientCG **CC0**, fetched
2026-09-30:
- **Culms:** **Bamboo001A** (a scanned wall of culms, 1.3 m). One culm's lit
  middle is wrapped round each tube, and the scan's own nodes ring it about
  32 cm apart.
- **Leaves:** **LeafSet013** (scanned lanceolate leaves).

Both scans are yellowish. As baked they came to albedo 0.25 where every other
plant is 0.07–0.12, so the bake darkens them and shifts them toward green: 0.134.

Four variants, 26–42 culms each, leaning out and arching over, with 24–40
branches per culm carrying drooping fans of 10–18 leaves. They are laid at
8–12.5 m, more of them toward the firebase end.

| source | sha256 |
|---|---|
| `Bamboo001A_2K-PNG.zip` | `fd85855288754f41048d81f92c7ff50d2c67ff8ceee0a3a88f738055b8d3b265` |
| `LeafSet013_2K-PNG.zip` | `51ab47b0e2bb8070ea26fc14e1e47ec3b9a869907302f766c032c0f6583b2752` |

**Palms, bamboo, banana, elephant ear: the preferred sources need a login.**
These were surveyed on Sketchfab through its public API (downloadable,
commercial use allowed, sorted by likes), and the thumbnails were compared.
Downloading needs the owner's Sketchfab account (PLAN §12.14: the owner's
browser). All are **CC-BY 4.0**: attribution in the credits.

| use | model | author | faces | uid |
|---|---|---|---|---|
| coconut palms (a grove of curved trunks) | Realistic Palm Tree 2 Free | Next Spring | 60,764 | `d05c8ffb6ec64a7184c4b673e21f7224` |
| coconut palm | Coconut Palm | evolveduk | 7,432 | `26e787f2ff2e4c0fb004c3b0210805a3` |
| palms, banana, monstera | Tropical Plants Pack M02P | MozzarellaARC | 46,575 | `2f093afb792742438f0f7ba7eaab90f0` |
| bamboo | Free Bamboo Set | JonhGillessen | 37,834 | `e9f9fa5397814f81bf85ad06acf5bf30` |
| elephant ear (Alocasia) | Elephant Ear Plant | BANDANNA | 1,514 | `9cd6cf1553844d4999530f8916991cef` |
| banana | Banana Plant | evolveduk | 4,402 | `85695b82c7ba4b3497a663616cc3bf25` |

The fallback needs no login: build them in Blender from ambientCG's CC0 scanned
leaf atlases. Foliage008's long blades serve as palm leaflets, LeafSet013's
lanceolate leaves as bamboo, Foliage001, 006 and 002 as grass blades and
plumes, and ambientCG `Bamboo001`/`002` as culms. These will be logged here when
they are used.

| source (glTF + bin) | sha256 |
|---|---|
| `fern_02` | `a2b165d3bf54cc93072be481aaff28479cfff6cc662383922baa5e4896848190` |
| `calathea_orbifolia_01` | `4b493b62c6de7498fe5a332d45da59eafc1ce3a41e8b082b717c8fcd38dbe3a0` |
| `anthurium_botany_01` | `6eb0953be25812cf03c07a648fa353c77f3076191563eb27ef148e48886280ab` |
| `pachira_aquatica_01` | `a1e14d1284f1530082be8fd33489aa6c5ff8392da566b94b3dd06243c3ade4ec` |
| `potted_plant_02` | `71261d7f603c9a80ef3ee3f9e3796cdb5f3ba8eadffc625b22f609bdcbe59f59` |
| `potted_plant_01` | `adb58e313bb17c140e58348fc71db0923fdf97d8b1c30b364d57b1e26408d967` |
| `island_tree_01` | `f7992c461094ab8592e556851614cf0cd4ed097e046f762f7797d4b17c623de2` |
| `island_tree_02` | `aa2928da4f9c5b12cb51631ef10fe7546648cfe628d34f4947e5f873a0c74945` |
| `jacaranda_tree` | `c34c3711074f3348f740425ddd4c944326835d5ed9291c760229eb9ca5529539` |

### Mountains — `Assets/_Project/Art/Mountains/`

**The real Chu Pong massif**, from **SRTM elevation data (public domain,
NASA/USGS)**. It was read through the **AWS Open Data "Terrain Tiles"**
(terrarium encoding), fetched **2026-09-30**: zoom 13, tiles x 6541–6552,
y 3778–3788 (132 tiles, 4.2 MB, about 18.6 m a sample), from
`https://s3.amazonaws.com/elevation-tiles-prod/terrarium/13/<x>/<y>.png`.
No login is needed. The raw tiles are in `SourceArt/terrain/` (gitignored).
Attribution kept in the credits: *"Terrain data: SRTM (NASA/USGS), via AWS
Terrain Tiles"*.

`tools/art/mountains.py` renders the view from **LZ X-Ray** (13.567°N,
107.717°E), the landing zone of November 1965 in the Ia Drang valley at the
massif's eastern foot, looking along a heading of **262°**.
- **Choosing the view:** skylines from nine candidate viewpoints and headings
  were rendered and compared. From here the massif stands at 6–8°, where the
  reference's peaks stand at 5–9°; from 4–8 km east it sinks to 2–3°. So there
  is no vertical exaggeration.
- **The grid:** a fan of 240 rings × 720 columns, 0.9–22 km out, ±25° about
  the heading.
- **Detail:** 7 m and 2.5 m of noise below the data's own resolution.
- **What ships:** the heights only, as float16 (`mountains.bytes`, 346 KB). The
  game builds the mesh at load.

| file | sha256 |
|---|---|
| `mountains.bytes` | `9397666d04ed41c86c3194cf9dd879bb7c81e9562b861c0a8506e4fa2aba6028` |

### Soldiers (interim) — `Assets/_Project/Art/Soldiers/`

**Our own work:** the earlier build's rigged soldiers, from its
`pipeline/build_soldier.py`. They are an MPFB2 (MakeHuman) body, **CC0**, on
MPFB2's 53-bone `game_engine` skeleton, with kit and weapons modelled by that
pipeline. They are copied into `SourceArt/soldiers/` (gitignored).
`plant_bake.py` poses the rig per frame and bakes the frames like the plants:
- **Frames:** stand, kneel, prone, two dead, three aims, and the motion-captured walk, run, crouched walk and idle below; three aims (standing, kneeling, prone: rifle to the shoulder by two-bone IK, pointed along the rifle's own measured axis).
- **View:** three-quarter view facing right; the VC are mirrored to face left.
- **Posing:** rotations about the figure's own axes.
- **Uniform correction:** the US uniform texture bakes at albedo 0.008. It is
  re-coloured to OG-107 olive drab (the old palette's hue) at 0.085, keeping
  the fabric's pattern.

**Motion: the CMU Graphics Lab Motion Capture Database**
(`mocap.cs.cmu.edu`), in B. Hahne's BVH conversion from
`github.com/una-dinosauria/cmu-mocap`, fetched 2026-09-30. The terms: *"free
for use in research projects. You may include this data in commercially-sold
products, but you may not resell this data directly, even in converted
form."* That is permitted under the owner's 2026-09-29 rule; PLAN §8 names it
the fallback while Mixamo is blocked. The raw BVH files are in
`SourceArt/mocap/cmu/` (gitignored). What ships is our baked sprites.

`plant_bake.py` retargets them onto the soldier's hips, legs, spine and head;
the arms keep the rifle. For each bone it takes the rotation relative to the
take's T-pose, turned so the performer's average heading becomes the
soldier's, and damps the torso where a clip leans too far for a man with a
rifle. It cuts one gait cycle from the steady middle of the take, left heel
ahead to left heel ahead, and records its stride.

| clip | CMU take | frames | stride | cycle | sha256 |
|---|---|---|---|---|---|
| walk | 16_15 "walk" | 16 | 1.10 m | 1.16 s | `7459cd4be169477b4e2c629075a9cde9436e37a87cdf4e774574400f4ec942d8` |
| run | 16_36 "run/jog" | 12 | 1.84 m | 0.80 s | `bc99c89352df21f9fed601b4529f430e364c174b6e87527144b6143e04005caf` |
| crouch | 136_09 "walk crouched" (torso at 45%) | 12 | 0.98 m | 1.27 s | `f958ad4cfc516b025da94eb1085b7ded2f34e0fad2c0520fb554a29794b28c33` |
| idle | 137_28 "normal wait": its stillest 3 s by foot travel and hip turn (torso at 60%) | 8 | — | 3 s | `10ee250f5676265860a4fa4bad8c5d74af7116c038f9fea998e1bbd7f4d395ad` |

This is an **interim** until the PLAN §12.3 soldiers (textured kit,
Mixamo-driven Humanoid motion) exist. It costs one quad per man.

| file | sha256 |
|---|---|
| `us_rifleman.glb` (source) | `ff0cd3c3388ae6ede5bbea97e0f40fea93043e33831356a61737d9a89d652b40` |
| `vc_guerrilla.glb` (source) | `2722ca1736956df6db650c30dee512d957050798a878125a56c4ec820f6b06a8` |
| `soldier_us_albedo.png` | `1199ae2cfc74685e9d9e4ca01d2d0fabd7472b90761f34e263412ddaf8d7fa77` |
| `soldier_us_normal.png` | `3d857e18777d4af2b63292f5613572c14dd2fb0a39873632dcd3e0baa59cdbb4` |
| `soldier_vc_albedo.png` | `4d6d02154f9ec7062b75dd80210884c59e0052733aa4ef3aa9e6ea54fdceb2b9` |
| `soldier_vc_normal.png` | `5de2e0ad9f6b42620e4660678a6770c8a1b4fb98a0b507202393886c51f9f2a8` |

### Soldiers (3D) — `Assets/_Project/Art/Soldiers3D/`

**Our own work on CC0 parts.** Three men a side, built by
`tools/blender/soldier_body.py` and made into skinned FBX for Unity's Humanoid
avatar by `tools/blender/soldier_rig.py` (PLAN §12.3).

- **Body:** MPFB2 (MakeHuman, CC0) base mesh and `game_engine` skeleton, the
  skeleton **fitted to the body** (the body's shape baked before the rig goes
  on, and the detailed helpers on: without both, every body got the default
  human's skeleton, the head joint 10 cm low).
- **Clothes, footwear, eyes, skin:** the **MakeHuman system assets**, CC0,
  `https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip`
  (fetched 2026-10-01, 281 MB, sha256
  `b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107`; kept in
  the git-ignored `SourceArt/makehuman/`). Used: `male_casualsuit01` (shirt
  and trousers), `shoes03`, `shoes04`, eyes `low-poly`, skins
  `young_caucasian_male`, `young_caucasian_male2`, `young_african_male`,
  `young_asian_male`, `middleage_asian_male`.
- **Colour:** the cloth keeps its own weave and folds (its luminance) under
  the palette's colour: OG-107 at 0.083 (US), black cotton 0.033 (VC), khaki
  green 0.10 (the NVA regular). Skins are gained down to men who live
  outdoors (0.17-0.20; the Black soldier's 0.085).
- **Kit:** helmet, webbing, flak vest and rifle carried over from the earlier
  build's models (`us_rifleman.glb`, `vc_guerrilla.glb`: our own work),
  refitted to each body bone by bone; the conical hat is modelled here.
- **Baked** by Cycles into one 2048 atlas a man (shipped 1024 colour, 512
  normal and mask); **9,000 triangles**; the rifle rigid on the right hand.

| man | what he is |
|---|---|
| `us_a` | rifleman in a flak vest (the calibrated body, 1.87 m with his helmet) |
| `us_b` | a bigger Black soldier, no flak vest |
| `us_c` | slighter and younger, in a vest, his fatigues newer |
| `vc_a` | guerrilla in black cotton and a conical hat |
| `vc_b` | older and stockier, black cotton, sun helmet |
| `vc_c` | NVA regular: khaki green, sun helmet, boots |

**Interim clips** (`soldier_poses.fbx`): the sprite bake's poses and its four
CMU takes (the table above) as keyed actions at 30 fps; kept as the fallback
for any role Mixamo's clips do not fill. Same CMU terms: the converted data
ships inside the game, never on its own.

| file | sha256 |
|---|---|
| `soldier_poses.fbx` | `59ca1195738177895d3cbf7b5dd59b58851b68e8267de8b6b3b7985b9357b5fd` |
| `soldier_us_a.fbx` | `c1e7c4b95078cf4cdcab176523d6224d5bd6131ccd11be0016b8c6d5f571124c` |
| `soldier_us_b.fbx` | `29cd080b19d4d5f6ec4b2957ce16db0ed7efc534bb03ff8325fe3806955865ba` |
| `soldier_us_c.fbx` | `be45c0c0f8d9ad71aacccd220584cfcc097bb35295fe8b774301ebc6dee477f6` |
| `soldier_vc_a.fbx` | `4a45c0ad52da2bdecfaa70d09faa0cb528df112cc3ca1505e4aacd1a15d2db70` |
| `soldier_vc_b.fbx` | `01df73b2e6b9e5b9f083a392c882acaa248acc8d849365031273e305b37df911` |
| `soldier_vc_c.fbx` | `a59d9340325d5ec0c8230a66a1d18ababdec37f43030c0c62199066bd067ea35` |
| `soldier_us_a_albedo.png` | `fa95f402b3ce2af8f7190f2192c578620d2007fb94e29c1d82cc800b16779136` |
| `soldier_us_b_albedo.png` | `110744e722298cf0a0c19bab57eae53c54df3aacdb880ff47d0c5a320ac7cb73` |
| `soldier_us_c_albedo.png` | `87abbf6410c85940fdd985eef30a63b4ba019acca8aa47fdcb02bc6ff7f6a146` |
| `soldier_vc_a_albedo.png` | `ffebc588c564fd93c8274e211186c2518e6f39e5264069deb4d9a4444dd48e3a` |
| `soldier_vc_b_albedo.png` | `fd71fbd9c1df872148240fe726a8bc48d349c4564f989c898c04f4d3e6b41618` |
| `soldier_vc_c_albedo.png` | `fce32e7f25c953d9b013f101ece68794da2322bedf3ad380b4cb2438267699b3` |
| `soldier_us_a_normal.png` | `cf3d43ae104eafadbae026630093751022f9580454c4940c16f94b9eafe0d3cb` |
| `soldier_us_b_normal.png` | `b1f80bae0bc01e8bc815fb922b1897835c174387f3cc9326d8c70db0e2ca8e8b` |
| `soldier_us_c_normal.png` | `b91739856a50003f48b324e50960d7e6c3301fa12bdf877abb2dd6072f5d10fb` |
| `soldier_vc_a_normal.png` | `a8a93d3d7c4797c285ba38b95867735e22d6050cb0626e0da0e89e864684fe50` |
| `soldier_vc_b_normal.png` | `45f9c0a41565cbf5f2285c4161bdda934ff4ed405ca363315b18ce6ed767e369` |
| `soldier_vc_c_normal.png` | `e7e7d7558321a5fab46daab15118dbbbcd5d0afb74e580a0f78ca103e092adea` |
| `soldier_us_a_mask.png` | `c81dcb5b6f9d333edcedc2e8b61c39f2e1d26b8597bcd41413e3933a13bde78f` |
| `soldier_us_b_mask.png` | `04f432c793e77629a0d0caf9ce070d58a4ea1875a2c7e457f5b4d80e613f034c` |
| `soldier_us_c_mask.png` | `21dc2079d40484a0b9cb22c88ac417e337f6018b86fc567753622fc63d429e07` |
| `soldier_vc_a_mask.png` | `843d8de04d1b1561f2e1e4bc8d51f3be34229316f575edf97dadb31d4bbb61e7` |
| `soldier_vc_b_mask.png` | `058c45c504e073a113408f777833c7d2aa9e2b8d97a3675d103ef39c61d4a6e6` |
| `soldier_vc_c_mask.png` | `565c41b43cf4353e0c41a600496aa47faadeeb52aba5835e21b09565d2079365` |

### Mixamo — `Assets/_Licensed/Mixamo/` (git-ignored)

**Adobe Mixamo**, through the owner's Adobe account in their browser (owner's
decision 2026-09-30; the files approved by name 2026-09-30). Mixamo's terms
allow its animations in games, royalty-free; the files themselves are not to
be redistributed, so they live in the ignored `Assets/_Licensed/` (their
`.meta` files, which hold only import settings, are committed). Exported as
*FBX for Unity*, 30 fps, without skin, no keyframe reduction, on the account's
primary character (Remy: Mixamo's standard skeleton, retargeted by Humanoid
onto our soldiers). Each file is named by its role in `SoldierBuilder.Mixamo`.

All 44 clips the game uses are downloaded (2026-09-30 and 2026-10-01): 27
singles, and 17 of the Pro Rifle Pack's 49 motions, exported clip by clip
(the pack's own export fails; its turns, jumps and strafes were left).

| group | roles (Mixamo clip) |
|---|---|
| standing | `rifle_idle`, `rifle_idle_aim`, `rifle_walk` (1.60 m/s on our man), `rifle_run` (4.00), `rifle_sprint` (6.04), `rifle_walk_back`, `rifle_run_back` (Pro Rifle Pack: idle, idle aiming, walk/run/sprint forward, walk/run backward) |
| crouched | `kneel_idle` (Rifle Kneel Idle), `rifle_crouch_idle`, `rifle_crouch_aim`, `rifle_crouch_walk` (1.73), `rifle_crouch_walk_back` (Pack), `kneel_aim` (Rifle Kneel To Aim) |
| prone | `prone_idle`, `crawl` (Prone Forward, 0.20), `prone_fire`, `prone_reload` |
| transitions | `stand_to_kneel`, `kneel_to_stand`, `kneel_to_prone`, `prone_to_kneel` (Rifle ...), `crouch_to_prone` |
| fire | `fire`, `fire_b` (Firing Rifle, standing x2), `fire_walk`, `fire_crouch_walk` (Firing Rifle while walking / crouch walking) |
| reactions | `flinch` (Rifle Shielding Face), `hit_back` (Rifle Hit To Back), `kneel_hit` (Rifle Kneel Hit To Back), `prone_hit` (Rifle Prone Hit Reaction), `reload` (Reloading), `grenade` (Toss Grenade) |
| deaths | `death_front_head`, `death_right`, `death_back_head`, `death_back`, `death_front`, `death_crouch_head` (Pack); `death_rifle` (Rifle Death), `death_fall_back` (Falling Back Death), `death_backwards` (Dying Backwards), `death_blast` (Flying Back Death), `death_run` (Rifle Run To Dying), `prone_death` |

### Props — `Assets/_Project/Art/Props/`

**Sandbag walls, built** (nothing scanned exists). The bag cloth is ambientCG
**CC0**, fetched 2026-09-30 as 2K PNG:
- **Fabric066**, weathered olive, on 62% of bags;
- **Fabric044**, rough tan, darkened by earth from albedo 0.47 to about 0.17.

`plant_bake.py` presses each bag from a sphere: flat top and bottom, bulging
sides, sagging in the middle, with its own lumps. They are laid in running
bond, two deep, each with its own tilt, and the courses settle into each
other. The result bakes to five 3.2 m segments: three wall height (1.03 m),
two parapet (0.52 m). The game repeats them along every sandbag wall, the
bunker, the trench parapets and the firebase revetments.

| file | sha256 |
|---|---|
| `Fabric066_2K-PNG.zip` (source) | `40782dab549e9d67a7681a65eaebbc1fb4a8e82972a490722da23daec86eb08f` |
| `Fabric044_2K-PNG.zip` (source) | `8fb94e4cdc44794970cc36a96e89b024f16773f8ddacf3876c80d976f64ce9e1` |
| `sandbags_albedo.png` | `8abbf8f5f55387bac05eee3832cad329cbae1db50e21adc49b6f65dc47a0e0a3` |
| `sandbags_normal.png` | `65f3e1f2150003df53aed071a831fdb8a4613a19a927bcdae6cc543d49eef385` |

**The firebase's structures, built.** They are baked as one atlas,
`firebase_*.png`, with four props. Each texture is laid in metres, so it sits
at true scale.
- **Watchtower:** 10.3 m. Splayed timber legs with X-braces in three bays, a
  plank deck, a sandbagged cabin, corner posts, a tilted corrugated roof, and
  a ladder up the front.
- **M35 2½-ton trucks:** 6×6 with dual rear wheels. One has its OD canvas
  cover on, the other has its bows bare and ammunition crates in the bed.
- **M151 jeep:** open, with an antenna and a spare.

Paint is olive drab at albedo 0.075, tyres 0.025.

The textures are **CC0**, fetched 2026-09-30 as 2K PNG:
- timber: Poly Haven **weathered_planks** (Dario Barresi, Dimitrios Savva;
  2 m scan);
- roof: Poly Haven **corrugated_iron_02** (Jenelle van Heerden, Sergej
  Majboroda; 2.7 m);
- truck canvas and seats: Fabric066, as above.

| file | sha256 |
|---|---|
| `weathered_planks` diff + nor_gl (source, concatenated) | `4e35fe6190d7fc5b2edb433efae9a166a4aa1a9bfa1effa7a1d6832ec7f9adba` |
| `corrugated_iron_02` diff + nor_gl (source, concatenated) | `9770915fab95fa365cff42e0bf1132cba8b39ea3e6f37bea3bf8b4b750451128` |
| `firebase_albedo.png` | `c7d2cb305022ab626e48a52d16cfc91efa9cb3873089f975a14b1542dffddeea` |
| `firebase_normal.png` | `2c6056ba53c6d7dcfb74fcc59a109653470b2b454076a431a8aaa2c01968dd62` |

| file | source | URL | licence | sha256 | fetched |
|---|---|---|---|---|---|

## Approved sources

Checked for licence terms, not just for a CC0 badge.

| source | what for | licence | notes |
|---|---|---|---|
| Poly Haven | HDRIs, ground and bark textures, props | CC0 | Resolution tiers size the *textures*, not the mesh — check the geometry |
| ambientCG | PBR materials: earth, sandbag, canvas, metal | CC0 | |
| Mixamo (Adobe) | Humanoid animation and rigging | Adobe royalty-free: use in games, no redistribution of the raw files | Needs the owner's Adobe ID |
| Adobe Photoshop 2026 | Authored textures, atlases, portraits | our own work | Installed, scriptable |
| Quaternius | Low-poly vegetation, props, vehicles | CC0 | Stylised; mixing with scans reads as two games |
| Kenney | UI, prototyping | CC0 | |
| Freesound | Ambience, weapon layers | **CC0 filter required** | Site hosts CC-BY and CC-BY-NC too |
| OpenGameArt | Gap-filling | **CC0 filter required** | Same caveat |

## Rules, learned the hard way

1. **Look at a texture before using it.** The previous build's first pick was
   blue dotted shirting for fatigues and patterned red upholstery leather for
   boots, both chosen by asset ID without opening them.
2. **Check the geometry, not the label.** A Poly Haven `tree_small_02` at "1k"
   had a 90.7 MB `.bin`. Nothing else in that set exceeded 1.7 MB.
3. **Reject on style as well as licence.** Correctly licensed and wrong for
   the project is still wrong.
4. **One visual family: photoreal** (owner, 2026-09-29). Stylised packs are out
   wherever they would stand next to scanned material.

## Explicitly rejected

Carried forward from the three.js build so they are not re-evaluated.

| source | licence | why |
|---|---|---|
| CMU Graphics Lab Motion Capture Database | free for all uses, but "you may not resell this data directly, even in converted form" | was rejected as not CC0; now permissible under the 2026-09-29 rule, kept as the fallback if Mixamo is unavailable |
| Bandai Namco Research motion dataset | CC BY-NC-ND | non-commercial, no derivatives |
| Poly Haven `tree_small_02` | CC0 | correctly licensed, 90.7 MB mesh |

## Fonts

The previous build used **Stardos Stencil (OFL 1.1)** — the one approved
exception to CC0, because §7 requires a military stencil face and every
credible one is OFL. The owner approved it on 2026-09-28. If §7's interface is
carried over, that approval carries with it and `OFL.txt` must ship beside the
font.

## Audio

**Sourced, on the owner's instruction (PLAN §12.6).** The previous build
synthesised all its audio. This one uses recordings: dry, close-miked where
possible. The distance is added by the engine, a Web Audio port of the
three.js build's measured graph (`Assets/_Project/Plugins/WebGL/LovAudio.jslib`),
which handles travel delay, air absorption, 1/d spreading and a scattered
tail. The valley's impulse response is generated there; it is processing, not
a sound. `tools/audio/slice_shots.py` builds everything below into
`Assets/StreamingAssets/Audio/` as AAC 44.1 kHz, mono for effects and stereo
for the bed.

**Gunfire: The Free Firearm Sound Library, CC0.** From OpenGameArt,
`https://opengameart.org/content/the-free-firearm-sound-library`; the page
states CC0. Fetched 2026-09-30 as `Prepared SFX Library.7z`, sha256
`cc1ab5a99a0a365105c7c5dd783f4b0b1fe90938114d3ceec53856bfe005f7d6`. The takes
are 96 kHz, 24-bit stereo, several shots each. Single shots are found by
onset, cut with their tail (up to 1.6 s), folded to mono and peak-normalised
to −1 dBFS.

| set | from | shots | used for |
|---|---|---|---|
| `m16_*` | AR-15 (the M16's civilian twin) | 4 | the US rifles |
| `ak_*` | AK-47 | 8 | the VC and NVA |
| `sks_*` | SKS | 4 | the VC and NVA, mixed with the AK so a volley is not one rifle |

These are CC0, so they are committed.

**Explosions, cracks and the jungle: Sonniss #GameAudioGDC 2017.** The terms
are in the bundle's licence (read 2026-09-30): worldwide, royalty-free,
personal and commercial use, no attribution required. Sounds may be
distributed as part of a production, but **may not be sold as-is**. Fetched
from the official mirror `ftpmirror.your.org/pub/misc/sonniss2017/individual/`
by `tools/audio/fetch_sonniss.sh`, which checks these hashes. Per the owner's
public-repo rule, the processed files live in the **git-ignored**
`Assets/StreamingAssets/Audio/licensed/` (their `.meta` files are committed)
and are rebuilt from `SourceArt/`.

| set | source file (pack, supplier) | sha256 of the source |
|---|---|---|
| `shell_0` | `explosion_large_08.wav` (Explosion Sound Pack, Gamemaster Audio) | `aa26f39d6e9fe6e6942e15fd4260f8da62f201451784995b718b9abfd2af712c` |
| `shell_1` | `explosion_med_long_tail_01.wav` (same) | `fea8024edfe07c5dbb40db53021dcc1c08b88415783bfe27767e3dffd794c08e` |
| `grenade_0` | `explosion_large_no_tail_03.wav` (same) | `7e541b20048559e711d10eb1766e8b4717e6c1feb4529e0adebe0d96fbcaba87` |
| `crack_0` | `bullet_flyby_fast_05.wav` (Bullet Impact Sounds, Gamemaster Audio) | `aaed15696f96ab7e9f6e844eb7b063156720c43abe1fdf88abf3bb50cf0ce7f7` |
| `thump_0` | `bullet_impact_body_thump_02.wav` (same) | `cb84da810687e966e7ed0ae3da269c65b42231bed22dd52696e4619c413e16ab` |
| `ambience_0` | `Jungle quiet insects and birds wide _120407_11.wav` (Thailand sound library, Faunethic / Charlie Atanasyan): a Southeast Asian jungle bed; a 60 s seamless loop from 30 s in, −20 dBFS RMS | `df1da8303fb61f1f8bc371c05a60faf00ab75783f5c3d14126c48244baef8b6e` |

**More of the fighting: Sonniss #GameAudioGDC 2016, 2017, 2019, 2020**, same
licence (2016 and 2020 read 2026-09-30: identical terms), same mirror
(`.../sonniss<year>/individual/`), same ignored folder; fetched and checked by
`fetch_sonniss.sh`. Takes holding many events are cut where the sound falls
quiet (`slice_shots.py` `events`).

| set | source (year, pack) | sha256 of the source | used for |
|---|---|---|---|
| `mg_0`, `mg_1` | `M1919A4_..._5m_behind_ORTF_...Triple_shots_x_1.wav`, `..._200m_left_behind_...` (2016, Pole Position: M1919A4 Browning .30 cal) | `35e04fb9…`, `01ecc109…` | one US man in five fires bursts |
| `mg_2` … `mg_5` | `warfare_t2_mg_firing_close_projectile_tail_large_field_...wav` (2017, Pole Position: The Warfare Library), 4 bursts | `b7cb77aa…` | the same |
| `smg_0`, `smg_1` | `PPSh41, Firing, t1, Burst, Long, MKH416.wav` (2020, Pole Position: PPSh-41), 2 bursts | `238ad768…` | one VC man in five |
| `crack_1` … `crack_10` | `warfare_t3_mg_whizzes_ricochets_bullet_cracks_M10.wav` (2017, Warfare Library), 10 events | `806acf0c…` | rounds passing a pinned man |
| `crack_11` … `crack_16` | `PM_BBI_Bullet_Passby_Whizzby_Airy_5.wav` (2020, PMSFX: Bullet Bys & Impacts), 6 events | `9c697f86…` | the same |
| `thump_1` | `PM_BBI_Bullet_Impact_Hit_Body_Flesh_25.wav` (2020, PMSFX) | `0a7c2e4b…` | a man hit |
| `dirt_0` | `PM_BBI_Bullet_Impact_Dirt_3.wav` (2020, PMSFX) | `ecaa3525…` | misses striking near the listener |
| `bodyfall_0` | `RL_bodyfall_Dirt_M4_Close_Stereo_Hard_Impact_10.wav` (2019, Red Libraries: Bodyfall) | `90d5ae7e…` | a man hitting the ground, a beat after the round |
| `howitzer_0` … `_2` | `Howitzer,M101,C1/C3,105 mm,...` (2019, Airborne Sound: Battlefield Howitzers), 3 reports | `21d5791d…`, `3b5a39a6…`, `86270f9b…` | the battery, far behind the line, once a salvo of a barrage |
| `jet_0`, `jet_1` | `Jet,Fighter,CF-18,Hornet,By,...`, `Jet,Fighter,F-16,...,By,...` (2019, Airborne Sound: Jet Fighter Maneuvers) | `588f6a27…`, `6bce891e…` | the air strike's pass (modern jets standing in for the F-100 and A-4) |
| `radio_0` | `Military Radio Voice A (HHG) Troops In Contact Message.wav` (2019, Apple Hill Studios: Military Radio Voices), whole | `daac19ce…` | first contact, for a US player |

Fetched but not used yet: `warfare_t1b_cannon_firing_forest_distant_...wav`
(2017), `Military Radio Voice D (HI-3) Forty Mike Mike Request.wav` (2019) and
`Asia_Echoes_Tam Coc_Forest_Day.wav` (2020, Spectravelers: Asia Echoes, Laos -
Vietnam; a Vietnamese forest for the bed, to be listened to before use).

Still to source: the UH-1, voices and music.

| committed file | sha256 |
|---|---|
| `ak_0.m4a` … `ak_7.m4a` | `4e779a7d…`, `1511c2fe…`, `9a162434…`, `09a8da50…`, `52fe1a62…`, `fee98695…`, `93809c5c…`, `6aae7906…` |
| `m16_0.m4a` … `m16_3.m4a` | `f948d7bf…`, `c0fd1868…`, `a08c0fb8…`, `47efe7e3…` |
| `sks_0.m4a` … `sks_3.m4a` | `e26df6db…`, `343d3ad1…`, `e0574d50…`, `a3ef3dd8…` |
