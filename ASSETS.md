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
