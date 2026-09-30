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

The previous build synthesised all audio rather than sourcing it, which means
there was nothing to license. §11's requirement is that a gun sound different
at 30 m and 300 m, and that is a propagation problem — travel delay, air
absorption, spreading, scattered tail — to account for all of these you should source it.
