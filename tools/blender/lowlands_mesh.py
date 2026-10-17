"""
Rice Fields' built things as geometry (PLAN §12.59, the plan's R4): the lowlands' watchtower in bamboo with a thatched
roof, a sluice gate in a paddy's dike, and an ox cart.

    Blender -b --factory-startup --python tools/blender/lowlands_mesh.py
    Blender -b --factory-startup --python tools/blender/lowlands_mesh.py -- --look   # build and render, bake nothing

Built on firebase_mesh.py's kit (its parts, its bake, its weathering), and laid out as it lays out the firebase's
timber tower, so that the game cuts it into the same pieces and brings it down the same way (WorldDressing, TowerView):

  bamboo tower   in the timber tower's members: four bamboo legs, splayed, their joints showing, lashed where they
                 meet; X-braces of thinner bamboo in three bays a face with their girts; a floor of split bamboo on
                 bamboo joists with the hatch the ladder comes up through; corner posts and rails; a hipped roof
                 thatched in courses on bamboo rafters; a bamboo ladder up the face toward the lens. Its deck at
                 6 m, not the timber tower's 8.4: the paddies' top step stands 5.5 m over the river already, and a
                 lower tower keeps its men clear of the minimap (PLAN §12.55). Where the men stand on it and where its
                 ladder is are the timber tower's (STAND, LADDER), so the game's numbers hold for both.
  sluice gate    two posts in a dike, a beam across their heads, a plank gate between them in its grooves, a pole to
                 lift it by
  ox cart        the two-wheeled cart of the lowlands: a plank bed with stake sides, two big spoked wheels on an axle,
                 shafts out in front for the buffalo

Made of what is already in SourceArt/ (ASSETS.md), nothing downloaded: ambientCG Bamboo001A (CC0) for the bamboo,
weathered dry; Grass003 (CC0), dried to straw, for the thatch; Poly Haven weathered_planks (CC0) for the gate and the
cart; a dark fibre for the lashings.

Outputs, in Assets/_Project/Art/Structures/:
    lowlands.fbx          bamboo_tower_legs, bamboo_tower_braces, bamboo_tower_deck, bamboo_tower_cabin,
                          bamboo_tower_roof, bamboo_tower_ladder, sluice_gate, ox_cart (origins on the ground under
                          their middles)
    lowlands_albedo.png (1024), lowlands_normal.png, lowlands_mask.png
    lowlands.json         the deck's height, the roof's, where the men stand and the ladder is; each mesh's size
"""
import json
import math
import os
import sys

import bmesh
import bpy
import mathutils
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import firebase_mesh as fm  # noqa: E402  (its kit, its bake, its weathering)
from firebase_mesh import Kit  # noqa: E402

ROOT = fm.ROOT
SRC = fm.SRC
OUT = fm.OUT

DECK, TOP = 6.0, 7.7
STAND = fm.STAND
LADDER = fm.LADDER
FOOT, HEAD = 1.32, 1.12          # the legs' half-spread at the ground and at the deck


def log(msg):
    print(f"[lowlands] {msg}", flush=True)


# --- what things are made of -----------------------------------------------------------------------

def ambient(name, folder, scale, tint=(1, 1, 1), rough=0.8, strength=1.0, mono=False):
    """An ambientCG surface laid on by the object's own space (from three sides, blended), as fm.scanned lays Poly Haven's."""
    mat, nt, bsdf = fm.nodes(name)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(os.path.join(SRC, "ambientcg", folder, f"{folder}_2K-PNG_Color.png"))
    img.projection = "BOX"
    img.projection_blend = 0.3
    nt.links.new(mp.outputs["Vector"], img.inputs["Vector"])
    mul = nt.nodes.new("ShaderNodeMixRGB"); mul.blend_type = "MULTIPLY"; mul.inputs["Fac"].default_value = 1.0
    mul.inputs["Color2"].default_value = (*tint, 1)
    if mono:
        # (Its light and dark only, coloured: green grass made straw.)
        bw = nt.nodes.new("ShaderNodeRGBToBW")
        nt.links.new(img.outputs["Color"], bw.inputs["Color"])
        nt.links.new(bw.outputs["Val"], mul.inputs["Color1"])
    else:
        nt.links.new(img.outputs["Color"], mul.inputs["Color1"])
    # (Bleached: the colour drawn toward its own grey, as a dried culm or old straw is.)
    grey = nt.nodes.new("ShaderNodeRGBToBW")
    nt.links.new(mul.outputs["Color"], grey.inputs["Color"])
    mix = nt.nodes.new("ShaderNodeMixRGB"); mix.inputs["Fac"].default_value = 0.35
    nt.links.new(mul.outputs["Color"], mix.inputs["Color1"])
    nt.links.new(grey.outputs["Val"], mix.inputs["Color2"])
    nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    lum = nt.nodes.new("ShaderNodeRGBToBW")
    nt.links.new(img.outputs["Color"], lum.inputs["Color"])
    fm.bump(nt, bsdf, lum.outputs["Val"], 0.4 * strength, 0.012)
    bsdf.inputs["Roughness"].default_value = rough
    return mat


def materials():
    # Bamboo a season cut: tan going grey, the green gone out of it.
    ambient("bamboo", "Bamboo001A", 2.2, (0.78, 0.66, 0.44), 0.7)
    ambient("split", "Bamboo001A", 3.0, (0.82, 0.70, 0.48), 0.75, 0.6)
    ambient("thatch", "Grass003", 1.6, (3.9, 3.0, 1.65), 0.95, 1.8, mono=True)
    fm.flat("rope", (0.09, 0.07, 0.045), 0.9)
    fm.scanned("timber", "polyhaven/weathered_planks", "weathered_planks", 0.9, (0.86, 0.78, 0.66))
    fm.flat("iron", (0.05, 0.045, 0.04), 0.6, 0.5)


# --- bamboo -------------------------------------------------------------------------------------------

def culm(k, a, b, r, joint=0.42, mat="bamboo", segs=10):
    """A bamboo pole from one point to another: the culm, a little swollen at each of its joints."""
    a, b = Vector(a), Vector(b)
    k.cyl(a, b, r, mat, segs)
    d = b - a
    n = int(d.length / joint)
    for i in range(1, n + 1):
        p = a + d * (i / (n + 1))
        k.cyl(p - d.normalized() * 0.018, p + d.normalized() * 0.018, r * 1.13, mat, segs)


def lash(k, at, along, r):
    """A lashing of fibre round a joint: a few turns, proud of the bamboo."""
    at, along = Vector(at), Vector(along).normalized()
    k.cyl(at - along * 0.05, at + along * 0.05, r * 1.25, "rope", 8)


def tower():
    """The bamboo tower in the timber tower's members (fm.tower), each a mesh of its own."""
    out = {}
    k = Kit("bamboo_tower_legs")
    for sx in (-1, 1):
        for sy in (-1, 1):
            culm(k, (sx * FOOT, sy * FOOT, -0.35), (sx * HEAD, sy * HEAD, DECK + 0.02), 0.075)
            # Set in the ground on a flat stone, as poles in wet ground are.
            k.box((sx * FOOT - 0.2, sy * FOOT - 0.2, -0.3), (sx * FOOT + 0.2, sy * FOOT + 0.2, 0.04), "timber", 0.03)
    out["tower_legs"] = k
    k = Kit("bamboo_tower_braces")
    for face in range(4):
        c, s = math.cos(face * math.pi / 2), math.sin(face * math.pi / 2)

        def at(u, w, z, c=c, s=s):
            x, y = u, w + 0.1                        # on the outside of the face's legs
            return (x * c - y * s, x * s + y * c, z)
        for bay in range(3):
            z0, z1 = bay * DECK / 3 + 0.2, (bay + 1) * DECK / 3 - 0.12
            w0, w1 = FOOT - (FOOT - HEAD) * z0 / DECK, FOOT - (FOOT - HEAD) * z1 / DECK
            culm(k, at(-w0, w0, z0), at(w1, w1, z1), 0.042, joint=0.38)
            culm(k, at(w0, w0, z0), at(-w1, w1, z1), 0.042, joint=0.38)
            mid = Vector(at(0, (w0 + w1) / 2, (z0 + z1) / 2))
            lash(k, mid, (c, s, 0), 0.05)                                                 # where they cross
        for z in (DECK / 3, 2 * DECK / 3):
            w = FOOT - (FOOT - HEAD) * z / DECK
            culm(k, at(-w - 0.06, w, z), at(w + 0.06, w, z), 0.05, joint=0.5)            # girts
            for u in (-w, w):
                lash(k, at(u, w, z), (0, 0, 1), 0.08)
    out["tower_braces"] = k
    k = Kit("bamboo_tower_deck")
    for y in (-1.5, -0.5, 0.5, 1.28):
        culm(k, (-1.95, y, DECK - 0.08), (1.95, y, DECK - 0.08), 0.07, joint=0.5)         # joists on the legs' heads
    rng = np.random.default_rng(12)
    y = -1.92
    while y < 1.9:
        wide = rng.uniform(0.07, 0.10)               # split bamboo: narrow slats
        y1 = min(1.92, y + wide - 0.01)
        spans = [(-1.95, -0.42), (0.42, 1.95)] if y1 > LADDER - 0.22 else [(-1.95, 1.95)]
        for x0, x1 in spans:
            k.box((x0 + rng.uniform(-0.04, 0.04), y, DECK), (x1 + rng.uniform(-0.04, 0.04), y1, DECK + 0.03), "split", 0.008, 1,
                  rot=(rng.uniform(-0.01, 0.01), 0, rng.uniform(-0.006, 0.006)))
        y += wide
    for x in (-0.44, 0.44):
        culm(k, (x, LADDER - 0.24, DECK - 0.02), (x, 1.93, DECK - 0.02), 0.04)             # the hatch's trimmers
    out["tower_deck"] = k
    k = Kit("bamboo_tower_cabin")
    for sx in (-1, 1):
        for sy in (-1, 1):
            culm(k, (sx * 1.82, sy * 1.82, DECK + 0.02), (sx * 1.82, sy * 1.82, TOP - 0.1), 0.06)
            lash(k, (sx * 1.82, sy * 1.82, DECK + 0.08), (0, 0, 1), 0.07)
    for face in range(4):
        c, s = math.cos(face * math.pi / 2), math.sin(face * math.pi / 2)
        for z in (DECK + 0.5, DECK + 1.0):
            culm(k, (-1.9 * c - 1.82 * s, -1.9 * s + 1.82 * c, z), (1.9 * c - 1.82 * s, 1.9 * s + 1.82 * c, z), 0.035, joint=0.45)
        culm(k, (-1.98 * c - 1.82 * s, -1.98 * s + 1.82 * c, TOP - 0.14), (1.98 * c - 1.82 * s, 1.98 * s + 1.82 * c, TOP - 0.14), 0.055)
    out["tower_cabin"] = k
    k = Kit("bamboo_tower_roof")
    apex = Vector((0, 0, TOP + 1.25))
    eave = 2.35
    corners = [Vector((sx * eave, sy * eave, TOP - 0.25)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    for cpt in corners:
        culm(k, cpt + (apex - cpt) * 0.02, apex, 0.045, joint=0.5)                        # the hips' rafters
    # The thatch: each of the four faces in courses, eave to ridge, each course a little proud of the one above it so
    # the roof is layered as thatch is; its eave ragged.
    rng = np.random.default_rng(31)
    courses = 6
    for f in range(4):
        a, b = corners[f], corners[(f + 1) % 4]
        for cidx in range(courses):
            t0, t1 = cidx / courses, min(1.0, (cidx + 1.25) / courses)
            rows = []
            for t in (t0, t1):
                left, right = a + (apex - a) * t, b + (apex - b) * t
                n = 14
                row = []
                for i in range(n + 1):
                    p = left + (right - left) * (i / n)
                    # proud of the course above, and ragged along its lower edge
                    out_ = (p - Vector((0, 0, p.z))).normalized() * 0.05 * (1 - t)
                    drop = -rng.uniform(0.0, 0.06) if t == t0 else 0.0
                    row.append(p + out_ + Vector((0, 0, drop)))
                rows.append(row)
            k.sheet(rows, "thatch", 0.07, away=(0, 0, TOP - 0.3))
    out["tower_roof"] = k
    k = Kit("bamboo_tower_ladder")
    for sx in (-0.26, 0.26):
        culm(k, (sx, LADDER, -0.1), (sx, LADDER, DECK + 0.95), 0.042, joint=0.45)
    n = int((DECK + 0.6) / 0.32)
    for i in range(n + 1):
        z = 0.3 + i * 0.32
        culm(k, (-0.31, LADDER, z), (0.31, LADDER, z), 0.024, joint=1.0)
        for sx in (-0.26, 0.26):
            lash(k, (sx, LADDER, z), (0, 0, 1), 0.045)
    for z in (DECK / 3, 2 * DECK / 3):
        for sx in (-0.26, 0.26):
            culm(k, (sx, LADDER - 0.24, z + 0.1), (sx, LADDER, z + 0.1), 0.03, joint=1.0)
    out["tower_ladder"] = k
    return out


# --- a sluice gate, an ox cart ------------------------------------------------------------------------

def sluice():
    """A sluice in a dike: two posts, a beam across their heads, a plank gate between them in its grooves, its pole."""
    k = Kit("sluice_gate")
    for sx in (-0.62, 0.62):
        fm.beam(k, (sx, 0, -0.5), (sx, 0, 1.15), 0.16)
        fm.beam(k, (sx * 0.86, -0.07, -0.4), (sx * 0.86, -0.07, 0.9), 0.04, bevel=0.006)    # the grooves' lips
        fm.beam(k, (sx * 0.86, 0.07, -0.4), (sx * 0.86, 0.07, 0.9), 0.04, bevel=0.006)
    fm.beam(k, (-0.82, 0, 1.2), (0.82, 0, 1.2), 0.15)
    for i in range(4):
        z = -0.35 + i * 0.19
        k.box((-0.55, -0.03, z), (0.55, 0.03, z + 0.17), "timber", 0.01, 1)
    fm.beam(k, (0, 0, -0.2), (0, 0, 1.55), 0.05, bevel=0.008)                            # the lifting pole
    k.box((-0.2, -0.03, 1.5), (0.2, 0.03, 1.56), "timber", 0.008, 1)
    return k


def cart():
    """The lowlands' ox cart: a plank bed with stakes along its sides, two big spoked wheels, shafts out in front."""
    k = Kit("ox_cart")
    bed_z, half_w, half_l = 0.78, 0.62, 1.05
    rng = np.random.default_rng(5)
    for i in range(9):
        y = -half_w + i * (2 * half_w / 9)
        k.box((-half_l, y, bed_z), (half_l, y + 2 * half_w / 9 - 0.012, bed_z + 0.045), "timber", 0.006, 1,
              rot=(rng.uniform(-0.01, 0.01), 0, 0))
    for y in (-half_w + 0.05, half_w - 0.05):
        fm.beam(k, (-half_l - 0.1, y, bed_z - 0.06), (half_l + 2.4, y * 0.35, bed_z - 0.02), 0.08)     # the shafts
        for x in np.linspace(-half_l + 0.05, half_l - 0.05, 5):
            culm(k, (x, y, bed_z + 0.04), (x, y, bed_z + 0.62), 0.025, joint=1.0)          # the sides' stakes
        culm(k, (-half_l, y, bed_z + 0.55), (half_l, y, bed_z + 0.55), 0.03, joint=0.6)
    culm(k, (half_l + 2.25, -0.35, bed_z - 0.03), (half_l + 2.25, 0.35, bed_z - 0.03), 0.04)  # the yoke's bar
    # The wheels: a rim of felloes, twelve spokes, a hub, an iron tyre, each side of the bed on one axle.
    r = 0.72
    fm.beam(k, (0, -half_w - 0.25, r), (0, half_w + 0.25, r), 0.09)
    for side in (-1, 1):
        y = side * (half_w + 0.14)
        segs = 24
        for s_ in range(segs):
            a0, a1 = s_ * 2 * math.pi / segs, (s_ + 1.08) * 2 * math.pi / segs
            p0 = ((r - 0.035) * math.cos(a0), y, r + (r - 0.035) * math.sin(a0))
            p1 = ((r - 0.035) * math.cos(a1), y, r + (r - 0.035) * math.sin(a1))
            fm.beam(k, p0, p1, 0.07, bevel=0.008)
            q0 = ((r + 0.005) * math.cos(a0), y, r + (r + 0.005) * math.sin(a0))
            q1 = ((r + 0.005) * math.cos(a1), y, r + (r + 0.005) * math.sin(a1))
            fm.beam(k, q0, q1, 0.022, mat="iron", bevel=0.004)
        k.cyl((0, y - 0.11, r), (0, y + 0.11, r), 0.11, "timber", 16)
        for s_ in range(12):
            a = s_ * math.pi / 6
            fm.beam(k, (0.1 * math.cos(a), y, r + 0.1 * math.sin(a)), ((r - 0.06) * math.cos(a), y, r + (r - 0.06) * math.sin(a)), 0.045, bevel=0.006)
    return k


# --- putting it together ------------------------------------------------------------------------------

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only_look = "--look" in argv
    out = OUT
    os.makedirs(out, exist_ok=True)
    seen = os.path.join(ROOT, "Logs", "lowlands")
    os.makedirs(seen, exist_ok=True)
    sc = fm.setup()
    materials()
    kits = {**{"bamboo_" + n: k for n, k in tower().items()}, "sluice_gate": sluice(), "ox_cart": cart()}
    objs = {n: k.object() for n, k in kits.items()}
    log("triangles: " + ", ".join(f"{n} {sum(len(p.vertices) - 2 for p in o.data.polygons)}" for n, o in objs.items()))

    bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, 0.0))
    ground = bpy.context.object
    ground.name = "ground"
    gm = bpy.data.materials.new("ground")
    gm.use_nodes = True
    next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Base Color"].default_value = (0.12, 0.10, 0.06, 1)
    ground.data.materials.append(gm)
    tower_objs = [o for n, o in objs.items() if n.startswith("bamboo_tower")]
    if only_look:
        fm.look(sc, tower_objs, os.path.join(seen, "tower.png"), (0, 0, 4.0), (8, 13, 5.5), 40)
        fm.look(sc, tower_objs, os.path.join(seen, "tower_far.png"), (0, 0, 4.2), (6, 34, 6.0), 50)
        fm.look(sc, [objs["sluice_gate"]], os.path.join(seen, "sluice.png"), (0, 0, 0.6), (2.5, 3.5, 1.4))
        fm.look(sc, [objs["ox_cart"]], os.path.join(seen, "cart.png"), (1, 0, 0.8), (4.5, 5.0, 2.4))
        return

    # Apart for the bake, so no thing's shade falls on another.
    objs["sluice_gate"].location = (20, 0, 0)
    objs["ox_cart"].location = (30, 0, 0)
    bpy.context.view_layer.update()
    # (The tower is smaller in the frame than the timber one; 1024 is enough for all of it.)
    fm.bake_set("lowlands", list(objs.values()), fm.weather_timber, out, albedo_by=2, rest_by=2)
    bpy.data.objects.remove(ground, do_unlink=True)

    lm = bpy.data.materials.new("lowlands_baked")
    final = []
    for n, o in objs.items():
        c = o.copy()
        c.data = o.data.copy()
        c.location = (0, 0, 0)
        sc.collection.objects.link(c)
        o.name = o.data.name = "kit " + n
        c.name = c.data.name = n
        c.data.materials.clear()
        c.data.materials.append(lm)
        final.append(c)
    info = {}
    for o in final:
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        info[o.name] = dict(length=round(max(v.x for v in bb) - min(v.x for v in bb), 3), width=round(max(v.y for v in bb) - min(v.y for v in bb), 3),
                            height=round(max(v.z for v in bb), 3), triangles=sum(len(p.vertices) - 2 for p in o.data.polygons))
        log(f"{o.name}: {info[o.name]}")
    for o in bpy.context.view_layer.objects:
        o.select_set(o in final)
    bpy.context.view_layer.objects.active = final[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "lowlands.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="OFF", path_mode="STRIP")
    with open(os.path.join(out, "lowlands.json"), "w") as f:
        json.dump(dict(deck=round(DECK + 0.03, 3), top=TOP, stand=STAND, ladder=LADDER, meshes=info), f, indent=1)
        f.write("\n")
    log(f"wrote {out}/lowlands.fbx")

    lm.use_nodes = True
    nt = lm.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    col = nt.nodes.new("ShaderNodeTexImage")
    col.image = bpy.data.images.load(os.path.join(out, "lowlands_albedo.png"))
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, 0.0))
    ground = bpy.context.object
    ground.name = "ground"
    ground.data.materials.append(gm)
    by = {o.name: o for o in final}
    fm.look(sc, [o for o in final if o.name.startswith("bamboo_tower")], os.path.join(seen, "baked_tower.png"), (0, 0, 4.0), (8, 13, 5.5), 40)
    fm.look(sc, [by["sluice_gate"], by["ox_cart"]], os.path.join(seen, "baked_props.png"), (0.8, 0, 0.7), (5.5, 6.0, 2.6))


if __name__ == "__main__":
    main()
