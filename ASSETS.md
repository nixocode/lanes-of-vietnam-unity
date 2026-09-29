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

*(none yet)*

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
