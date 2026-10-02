"""
The soldiers' bodies, rebuilt (the owner's second playtest: "legs are all
janked out of place, does not look natural").

    Blender -b --python tools/blender/soldier_body.py -- us [vc] [--preview]

(not --factory-startup: it needs the MPFB2 extension.)

The earlier build's soldiers were a bare MakeHuman body with its skin painted
as a uniform: garments grown as shells a centimetre off the skin, the "boots"
the feet themselves, toes and all, on the tiptoe the base mesh rests in.
Measured, no bone was out of place; the bodies were. Here the same body (the
same MPFB2 macros, so the same skeleton to the millimetre) is dressed:

  body     MPFB2's base mesh and `game_engine` rig, as before, with a
           photographed skin and eyes (MakeHuman system assets, CC0)
  clothes  `male_casualsuit01`: a buttoned shirt and trousers modelled as
           cloth, folds and all; `shoes03` (boots) or `shoes04`. The body
           under them is cut away, as MPFB's delete groups say.
  colour   the cloth's own weave and folds (its luminance) under the
           palette's colour: OG-107 for the US, black cotton for the VC
  kit      helmet, webbing, flak vest and rifle carried over from the
           earlier build's model, which were its good parts, bound to the
           same bones

Output: SourceArt/soldiers/<side>_v2.glb (and a preview render), in the form
tools/blender/soldier_rig.py takes: one armature `rig`, one skinned mesh
`soldier`, the rifle in materials gun_steel and gun_furniture.
"""
import math
import os
import sys

import bpy
import mathutils
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt")
PACK = os.path.join(SRC, "makehuman", "pack")
OUT = os.path.join(SRC, "soldiers")

# Three men a side, so that two the same seldom stand together (PLAN §12.3,
# "one body, many men"; the owner's second playtest: "more variety"). The
# first of each side is the earlier build's calibrated body; the others move
# its macros, so their skeletons differ and the kit is refitted to them
# (refit_kit). kit: which of the earlier model's materials are carried over.
US = dict(old="soldiers/us_rifleman.glb", eyes="brown", shoes="shoes03", cloth=(0.083, 0.086, 0.055),   # OG-107 at 0.085
          hair=(0.045, 0.032, 0.022), helmet_up=0.032)
VC = dict(old="soldiers/vc_guerrilla.glb", eyes="brown", shoes="shoes04", cloth=(0.034, 0.033, 0.035),  # black cotton, sun-faded
          hair=(0.014, 0.013, 0.013), helmet_up=0.0)
SIDES = {
    "us_a": dict(US, skin="young_caucasian_male",
                 macro=dict(gender=0.92, age=0.34, muscle=0.64, weight=0.47, proportions=0.62, height=0.70,
                            race={"asian": 0.10, "caucasian": 0.62, "african": 0.28}),
                 kit=("helmet_steel", "helmet_cover", "webbing", "flakvest", "gun_steel", "gun_furniture")),
    # No flak vest (many went without in the heat), a bigger man, and the boonie hat in
    # place of the steel pot (the owner, playtests 2 to 4: "more variety").
    "us_b": dict(US, skin="young_african_male", hair=(0.012, 0.011, 0.011), hat="boonie",
                 macro=dict(gender=0.95, age=0.30, muscle=0.74, weight=0.52, proportions=0.60, height=0.74,
                            race={"asian": 0.02, "caucasian": 0.08, "african": 0.90}),
                 kit=("webbing", "gun_steel", "gun_furniture")),
    # Slighter and younger, his fatigues newer and greener.
    "us_c": dict(US, skin="young_caucasian_male2", cloth=(0.072, 0.082, 0.050),
                 macro=dict(gender=0.88, age=0.26, muscle=0.50, weight=0.40, proportions=0.58, height=0.66,
                            race={"asian": 0.04, "caucasian": 0.90, "african": 0.06}),
                 kit=("helmet_steel", "helmet_cover", "webbing", "flakvest", "gun_steel", "gun_furniture")),
    # The guerrilla: black cotton and the conical straw hat.
    "vc_a": dict(VC, skin="young_asian_male", hat="conical",
                 macro=dict(gender=0.90, age=0.30, muscle=0.55, weight=0.38, proportions=0.55, height=0.69,
                            race={"asian": 0.94, "caucasian": 0.03, "african": 0.03}),
                 kit=("webbing", "gun_steel", "gun_furniture")),
    # An older, stockier man, in brown-dyed cotton. (Age moves height: 0.42 built a 1.77 m guerrilla.)
    "vc_b": dict(VC, skin="middleage_asian_male", cloth=(0.046, 0.034, 0.024),
                 macro=dict(gender=0.92, age=0.34, muscle=0.62, weight=0.45, proportions=0.52, height=0.66,
                            race={"asian": 0.96, "caucasian": 0.02, "african": 0.02}),
                 kit=("helmet_cover", "webbing", "gun_steel", "gun_furniture")),
    # An NVA regular and the sun helmet. His cotton was khaki-green (0.105, 0.105, 0.062), which at
    # this distance was an American's olive: the owner, 2026-10-02, "some Viet Cong forces' colors look
    # too alike to US, make them darker browns". Earth brown now, well under the US fatigue's 0.085.
    "vc_c": dict(VC, skin="young_asian_male", cloth=(0.066, 0.047, 0.030), shoes="shoes03",
                 macro=dict(gender=0.90, age=0.27, muscle=0.58, weight=0.40, proportions=0.56, height=0.71,
                            race={"asian": 0.95, "caucasian": 0.03, "african": 0.02}),
                 kit=("helmet_cover", "webbing", "gun_steel", "gun_furniture")),
}


def log(msg):
    print(f"[body] {msg}", flush=True)


def activate(o):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for x in bpy.context.view_layer.objects:
        x.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def apply_masks(o):
    """Shape keys baked, then every mask modifier applied: what clothes hide is gone, not hidden."""
    activate(o)
    if o.data.shape_keys is not None:
        bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    for m in [m for m in o.modifiers if m.type == "MASK"]:
        try:
            bpy.ops.object.modifier_apply(modifier=m.name)
        except RuntimeError as e:
            log(f"{o.name}: mask {m.name} not applied ({e})")
    for m in [m for m in o.modifiers if m.type in ("SUBSURF", "MULTIRES")]:
        o.modifiers.remove(m)


def remove_helpers(body):
    groups = {g.index for g in body.vertex_groups if g.name in ("HelperGeometry", "JointCubes")}
    if not groups:
        return
    doomed = [v.index for v in body.data.vertices if any(g.group in groups and g.weight > 0 for g in v.groups)]
    activate(body)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for i in doomed:
        body.data.vertices[i].select = True
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="VERT")
    bpy.ops.object.mode_set(mode="OBJECT")


def recolour(mat, colour, out_png, power=0.75):
    """The cloth's weave and folds (its luminance about its median) under one colour."""
    node = next((n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image and "diffuse" in n.image.name.lower()), None)
    if node is None:
        node = next(n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    img = node.image
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    rgb = px[:, :3]
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    lum = lin @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    solid = px[:, 3] > 0.5
    med = max(float(np.median(lum[solid])) if solid.any() else float(np.median(lum)), 1e-4)
    # Shirt and jeans are different blues: each half of the suit about its own median
    # would need a mask; the 0.75 power pulls them together instead.
    pattern = np.clip(lum / med, 0.25, 2.5) ** power
    out = np.clip(np.array(colour, np.float32)[None, :] * pattern[:, None], 0, 1)
    enc = np.where(out <= 0.0031308, out * 12.92, 1.055 * np.power(out, 1 / 2.4) - 0.055)
    new = bpy.data.images.new(os.path.basename(out_png), w, h, alpha=True)
    new.pixels.foreach_set(np.concatenate([enc, px[:, 3:4]], 1).astype(np.float32).ravel())
    new.filepath_raw = out_png
    new.file_format = "PNG"
    new.save()
    node.image = new
    log(f"{mat.name}: {img.name} median {med:.3f} -> cloth at {np.dot(colour, [0.2126, 0.7152, 0.0722]):.3f}")


def refit_kit(kit, reference, arm):
    """
    Carry the kit from the body it was modelled on to this one: each vertex
    moved as its bones moved between the two fitted skeletons (its own skin
    weights, blended), so a helmet stays on a taller man's head and a belt on
    a wider man's hips. Returns the furthest any vertex went.
    """
    move = {}
    for name, was in reference.items():
        nb = arm.data.bones.get(name)
        if nb is not None:
            move[name] = nb.matrix_local @ was.inverted()
    names = {g.index: g.name for g in kit.vertex_groups}
    worst = 0.0
    for v in kit.data.vertices:
        acc, total = mathutils.Vector((0, 0, 0)), 0.0
        for g in v.groups:
            m = move.get(names.get(g.group))
            if m is None or g.weight <= 0:
                continue
            acc += (m @ v.co) * g.weight
            total += g.weight
        if total > 0:
            new = acc / total
            worst = max(worst, (new - v.co).length)
            v.co = new
    return worst


def conical_hat(arm, head_top, colour=(0.27, 0.22, 0.12)):       # weathered palm leaf, not new straw
    """The non la: a shallow straw cone, its brim wider than his shoulders' half, sat on the head bone."""
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=28, radius1=0.235, radius2=0.004, depth=0.135)
    # An inner face a few millimetres in, so it is not paper-thin from below.
    geom = bmesh.ops.duplicate(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:])["geom"]
    for v in [g for g in geom if isinstance(g, bmesh.types.BMVert)]:
        v.co *= 0.97
        v.co.z -= 0.006
    for f in [g for g in geom if isinstance(g, bmesh.types.BMFace)]:
        f.normal_flip()
    me = bpy.data.meshes.new("hat")
    bm.to_mesh(me)
    bm.free()
    for p_ in me.polygons:
        p_.use_smooth = True
    me.uv_layers.new(name="UVMap")
    mat = bpy.data.materials.new("hat_straw")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    me.materials.append(mat)
    hat = bpy.data.objects.new("hat", me)
    bpy.context.scene.collection.objects.link(hat)
    hat.parent = arm
    head = arm.data.bones["head"]
    # Its rim at the brow: the cone's middle a little below the crown.
    hat.location = (head.head_local.x, head.head_local.y + 0.01, head_top - arm.location.z - 0.022)
    hat.rotation_euler = (math.radians(-6), 0, 0)             # tipped back a little
    hat.vertex_groups.new(name="head").add(range(len(me.vertices)), 1.0, "REPLACE")
    hat.modifiers.new("Armature", "ARMATURE").object = arm
    activate(hat)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return hat


def boonie_hat(arm, head_top, colour=(0.072, 0.080, 0.050)):       # OG-107 poplin, sun-faded like his fatigues
    """
    The jungle hat: a low round crown and a soft brim a hand wide, never
    quite flat. Built round the head bone like the conical hat.
    """
    import bmesh
    bm = bmesh.new()
    seg = 24
    rings = []
    # From the brim's edge in to the crown's foot, up its side and over the top: (radius, height).
    profile = [(0.185, -0.012), (0.150, 0.000), (0.112, 0.006), (0.104, 0.030), (0.100, 0.062), (0.086, 0.082), (0.050, 0.092), (0.0, 0.095)]
    for k, (r, z) in enumerate(profile):
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            # The brim's edge waves a little and droops at the sides.
            wave = (0.010 * math.sin(3 * a + 0.7) - 0.008 * abs(math.sin(a))) if k == 0 else 0.0
            ring.append(bm.verts.new((r * math.cos(a) * 0.94, r * math.sin(a) * 1.04, z + wave)) if r > 0 else None)
        rings.append(ring)
    top = bm.verts.new((0, 0, profile[-1][1]))
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            if b[i] is None:
                bm.faces.new((a[i], a[j], top))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    # The underside of the brim, so it is not paper from below.
    under = [bm.verts.new((v.co.x * 0.985, v.co.y * 0.985, v.co.z - 0.006)) for v in rings[0]]
    inner = [bm.verts.new((v.co.x * 0.96, v.co.y * 0.96, v.co.z - 0.008)) for v in rings[2]]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((rings[0][j], rings[0][i], under[i], under[j]))
        bm.faces.new((under[j], under[i], inner[i], inner[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("hat")
    bm.to_mesh(me)
    bm.free()
    for p_ in me.polygons:
        p_.use_smooth = True
    me.uv_layers.new(name="UVMap")
    mat = bpy.data.materials.new("hat_cloth")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = 0.9
    me.materials.append(mat)
    hat = bpy.data.objects.new("hat", me)
    bpy.context.scene.collection.objects.link(hat)
    hat.parent = arm
    head = arm.data.bones["head"]
    # Its crown over the skull: the brim at the brow.
    hat.location = (head.head_local.x, head.head_local.y + 0.012, head_top - arm.location.z - 0.086)
    hat.rotation_euler = (math.radians(-5), 0, 0)
    hat.vertex_groups.new(name="head").add(range(len(me.vertices)), 1.0, "REPLACE")
    hat.modifiers.new("Armature", "ARMATURE").object = arm
    activate(hat)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return hat


def cut_hair(body, arm, colour):
    """
    Hair, cropped short: the scalp's faces given a dark material of their own.
    The skins are bald (their hair is a separate mesh of alpha cards, which an
    opaque atlas cannot hold), and a soldier seen from behind was a bare skull
    under his helmet. The scalp is the head above the brow and behind the
    temples, down to the nape; the line is as coarse as the mesh there (about
    a centimetre), which is what a field haircut looks like from ten metres.
    """
    mw = body.matrix_world
    h0 = arm.matrix_world @ arm.data.bones["head"].head_local
    top = max((mw @ v.co).z for v in body.data.vertices)
    span = top - h0.z
    mat = bpy.data.materials.new("hair")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = 0.6
    body.data.materials.append(mat)
    slot = len(body.data.materials) - 1
    n = 0
    for p in body.data.polygons:
        c = mw @ p.center
        t = (c.z - h0.z) / span                      # 0 at the base of the skull, 1 at the crown
        front = -(c.y - h0.y)                        # metres toward the face
        side = abs(c.x - h0.x)
        if t < 0.18 or side > 0.2:
            continue
        crown = t > 0.80 and front < 0.085
        back = t > 0.22 and front < -0.005 - 0.05 * max(0.0, 0.6 - t)
        temple = t > 0.62 and front < 0.05
        ear = 0.40 < t < 0.62 and -0.03 < front < 0.03 and side > 0.062     # the ears stay skin
        if (crown or back or temple) and not ear:
            p.material_index = slot
            n += 1
    return n


def human(HumanService, macro):
    """
    A body and its skeleton, the skeleton fitted to the body.

    Two things MPFB needs for that, neither of them its default, and the
    earlier build had neither: the body's shape baked into the mesh before the
    rig goes on (the macros are shape keys, and the rig is fitted to the basis
    under them), and the detailed helpers, whose named joint cubes are what
    each bone is placed by; without them every bone takes its file's default.
    Measured: a 1.61 m man and a 1.80 m man had the same skeleton to the
    millimetre, the default human's, and the US rifleman's head bone sat 10 cm
    below his head: the long hinged necks of the first 3D men.
    """
    body = HumanService.create_human(mask_helpers=True, detailed_helpers=True, extra_vertex_groups=False,
                                     feet_on_ground=False, scale=0.1, macro_detail_dict=macro)
    activate(body)
    if body.data.shape_keys is not None:
        bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    HumanService.add_builtin_rig(body, "game_engine", import_weights=True)
    arm = body.parent if body.parent is not None else next(o for o in bpy.data.objects if o.type == "ARMATURE" and o.name != "rig")
    return body, arm


def build(side, preview):
    spec = SIDES[side]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import addon_utils
    addon_utils.enable("bl_ext.user_default.mpfb", default_set=True, persistent=True)
    from bl_ext.user_default.mpfb.services import HumanService, TargetService

    macro = dict(TargetService.get_default_macro_info_dict())
    macro.update(spec["macro"])
    # The kit was modelled round the earlier build's body, so it moves only as
    # far as this man's skeleton differs from that body's own fitted skeleton.
    family = side.split("_")[0]
    reference = None
    if spec["macro"] != SIDES[family + "_a"]["macro"]:
        ref_macro = dict(TargetService.get_default_macro_info_dict())
        ref_macro.update(SIDES[family + "_a"]["macro"])
        ref_body, ref_arm = human(HumanService, ref_macro)
        reference = {b.name: b.matrix_local.copy() for b in ref_arm.data.bones}
        bpy.data.objects.remove(ref_body, do_unlink=True)
        bpy.data.objects.remove(ref_arm, do_unlink=True)
    body, arm = human(HumanService, macro)
    body.name = "body"
    arm.name = "rig"

    HumanService.set_character_skin(os.path.join(PACK, "skins", spec["skin"], spec["skin"] + ".mhmat"), body, skin_type="MAKESKIN")
    parts = []
    for kind, rel in (("Clothes", "clothes/male_casualsuit01/male_casualsuit01.mhclo"),
                      ("Clothes", f"clothes/{spec['shoes']}/{spec['shoes']}.mhclo"),
                      ("Eyes", "eyes/low-poly/low-poly.mhclo")):
        o = HumanService.add_mhclo_asset(os.path.join(PACK, rel), body, asset_type=kind, subdiv_levels=0,
                                         material_type="MAKESKIN", set_up_rigging=True, interpolate_weights=True,
                                         import_subrig=False, import_weights=True)
        o.name = os.path.basename(rel).split(".")[0]
        parts.append(o)
    suit, shoes, eyes = parts
    for o, name in ((body, "skin"), (suit, "fatigue"), (shoes, "boots"), (eyes, "eyes")):
        for m in o.data.materials:
            if m:
                m.name = name

    remove_helpers(body)
    for o in [body] + parts:
        apply_masks(o)
    scalp = cut_hair(body, arm, spec["hair"])
    recolour(suit.data.materials[0], spec["cloth"], os.path.join(OUT, f"cloth_{side}.png"))
    if spec["shoes"] == "shoes04":
        # These come with white socks: everything about the shoe goes to worn black.
        recolour(shoes.data.materials[0], (0.03, 0.028, 0.026), os.path.join(OUT, f"shoes_{side}.png"), power=0.35)

    # Stand him on the ground: on his soles now, not his bare feet.
    bpy.context.view_layer.update()
    zmin = min((o.matrix_world @ v.co).z for o in [body] + parts for v in o.data.vertices)
    arm.location.z -= zmin
    bpy.context.view_layer.update()

    # --- the kit, from the earlier build's model ---
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, spec["old"]))
    new = [o for o in bpy.data.objects if o not in before]
    old_arm = next(o for o in new if o.type == "ARMATURE")
    old = next(o for o in new if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers))
    worst = max((arm.data.bones[b.name].head_local - b.head_local).length for b in old_arm.data.bones if b.name in arm.data.bones)
    head_was = old_arm.data.bones["head"].head_local.z
    keep = {i for i, m in enumerate(old.data.materials) if m and m.name in spec["kit"]}
    activate(old)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for p in old.data.polygons:
        p.select = p.material_index not in keep
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="FACE")
    bpy.ops.object.mode_set(mode="OBJECT")
    kit = old
    kit.name = "kit"
    moved = refit_kit(kit, reference, arm) if reference else 0.0
    log(f"{side}: head joint {arm.data.bones['head'].head_local.z:.3f} (the earlier build's skeleton: {head_was:.3f}; bones up to "
        f"{worst * 1000:.0f} mm off); kit refitted by up to {moved * 1000:.0f} mm")
    # The vest and the webbing were fitted to a skin-tight shell; over real
    # cloth they sit a centimetre and a half further out, or the shirt shows through.
    # The M1 helmet was set on a head hinged 10 cm low; on this one it sat over the eyes.
    if spec.get("helmet_up"):
        steel = {i for i, m in enumerate(kit.data.materials) if m and m.name in ("helmet_steel", "helmet_cover")}
        for vi in {v for p_ in kit.data.polygons if p_.material_index in steel for v in p_.vertices}:
            kit.data.vertices[vi].co.z += spec["helmet_up"]
    soft = {i for i, m in enumerate(kit.data.materials) if m and m.name in ("flakvest", "webbing")}
    push = {v for p_ in kit.data.polygons if p_.material_index in soft for v in p_.vertices}
    for vi in push:
        v = kit.data.vertices[vi]
        v.co += v.normal * 0.015
    local = kit.matrix_local.copy()
    kit.parent = arm
    kit.matrix_parent_inverse = mathutils.Matrix.Identity(4)
    kit.matrix_local = local
    for m in kit.modifiers:
        if m.type == "ARMATURE":
            m.object = arm
    for o in new:
        if o is not kit:
            bpy.data.objects.remove(o, do_unlink=True)

    hat = None
    if spec.get("hat") == "conical":
        top = max((body.matrix_world @ v.co).z for v in body.data.vertices)
        hat = conical_hat(arm, top)
    elif spec.get("hat") == "boonie":
        top = max((body.matrix_world @ v.co).z for v in body.data.vertices)
        hat = boonie_hat(arm, top)

    # --- one mesh, one UV map ---
    meshes = [body] + parts + [kit] + ([hat] if hat else [])
    for o in meshes:
        for uv in o.data.uv_layers:
            uv.name = "UVMap"
            break
        for extra in list(o.data.uv_layers)[1:]:
            o.data.uv_layers.remove(extra)
        if not any(m.type == "ARMATURE" for m in o.modifiers):
            mod = o.modifiers.new("Armature", "ARMATURE")
            mod.object = arm
    activate(body)
    for o in meshes:
        o.select_set(True)
    bpy.ops.object.join()
    body.name = "soldier"
    used = {p.material_index for p in body.data.polygons}
    for i in reversed(range(len(body.data.materials))):
        if i not in used:
            body.data.materials.pop(index=i)
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    height = max((body.matrix_world @ v.co).z for v in body.data.vertices)
    log(f"{side}: {tris} triangles, {len(body.data.vertices)} vertices, {height:.2f} m tall, {scalp} scalp faces, "
        f"materials {[m.name for m in body.data.materials]}")

    if preview:
        sc = bpy.context.scene
        sc.render.engine = "BLENDER_WORKBENCH"
        sc.display.shading.light = "STUDIO"
        sc.display.shading.color_type = "TEXTURE"
        sc.render.resolution_x, sc.render.resolution_y = 700, 900
        cam = bpy.data.cameras.new("c")
        cam.type = "ORTHO"
        cam.ortho_scale = 2.2
        co = bpy.data.objects.new("c", cam)
        sc.collection.objects.link(co)
        sc.camera = co
        sc.world = bpy.data.worlds.new("w")
        for name, loc, rot in (("side", (-6, 0, 0.95), (math.radians(90), 0, math.radians(-90))),
                               ("front", (0, -6, 0.95), (math.radians(90), 0, 0)),
                               ("back", (0, 6, 0.95), (math.radians(90), 0, math.radians(180)))):
            co.location, co.rotation_euler = loc, rot
            sc.render.filepath = os.path.join(ROOT, "captures", "soldiers", f"body_{side}_{name}.png")
            bpy.ops.render.render(write_still=True)

    path = os.path.join(OUT, f"{side}_v2.glb")
    activate(arm)
    body.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_animations=False,
                              export_skins=True, export_yup=True, export_apply=False)
    log(f"wrote {path}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = "--preview" in argv
    os.makedirs(os.path.join(ROOT, "captures", "soldiers"), exist_ok=True)
    for side in [a for a in argv if not a.startswith("--")] or list(SIDES):
        build(side, preview)


if __name__ == "__main__":
    main()
