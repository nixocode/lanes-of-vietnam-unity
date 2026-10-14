"""
The mortar set up (PLAN §12.48, MatchOptions.Emplace; the plan the owner approved,
2026-10-12: "the tube on its baseplate and bipod, the crew kneeling by it").

    Blender -b --factory-startup --python tools/blender/mortar_mesh.py
    Blender -b --factory-startup --python tools/blender/mortar_mesh.py -- --preview

An M2 60 mm mortar as it stands when it fires: the tube, 726 mm, at 60 degrees;
the M3 baseplate, a ribbed plate 40 by 30 cm with the tube's socket at its rear;
the M5 bipod, its two legs from a collar on the tube, the elevating screw between
them, the cross-leveling chain from leg to leg, the traversing handwheel and the
M4 sight on its bracket. Built here as the weapons are (tools/blender/weapons.py,
whose shapes and palette it uses): nothing downloaded. The tube the mortar man
carries stays the one that file builds.

It fires toward +x; its baseplate's middle is at the origin on the ground (z = 0);
an empty, mortar_set.muzzle, marks where the bomb leaves it.

Outputs, in Assets/_Project/Art/Weapons/:
    mortar_set.fbx     the set, on the weapons' palette (weapons_palette.png)
    mortar_set.json    its triangles, and the muzzle's place
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import weapons as W  # noqa: E402  (its shapes and its palette)

OUT = W.OUT
ELEVATION = math.radians(60)
TUBE = 0.726


def along(t):
    """A point on the tube's axis, t metres from its base cap."""
    base = Vector((-0.10, 0.0, 0.07))
    return base + Vector((math.cos(ELEVATION), 0, math.sin(ELEVATION))) * t


def build(bm):
    O, S, K = W.OLIVE, W.STEEL, W.BLACK
    # The baseplate: a plate with its edges turned down, three ribs under it and a raised socket at the rear.
    W.prism(bm, [(-0.20, 0.0), (-0.20, 0.03), (0.20, 0.03), (0.20, 0.0)], 0.30, O, chamfer=0.008)
    for y in (-0.09, 0.0, 0.09):
        W.prism(bm, [(-0.18, 0.0), (-0.18, 0.05), (0.06, 0.035), (0.16, 0.0)], 0.018, O, y0=y, chamfer=0.004)
    W.rod(bm, (-0.10, 0, 0.03), (-0.10, 0, 0.085), 0.05, O, sides=12)            # the socket
    W.rod(bm, (-0.02, 0, 0.032), (0.18, 0, 0.032), 0.012, S, sides=6)            # its carrying handle, folded
    # The tube: the base cap, the barrel, the muzzle's lip.
    W.rod(bm, along(-0.02), along(0.06), 0.040, O, sides=14)
    W.rod(bm, along(0.06), along(TUBE - 0.012), 0.0325, O, sides=14)
    W.rod(bm, along(TUBE - 0.012), along(TUBE), 0.036, S, sides=14)
    # The bipod: its collar on the tube, two legs to the ground before the plate, their feet.
    collar = along(0.46)
    W.rod(bm, collar - Vector((math.cos(ELEVATION), 0, math.sin(ELEVATION))) * 0.035,
          collar + Vector((math.cos(ELEVATION), 0, math.sin(ELEVATION))) * 0.035, 0.046, S, sides=12)
    hub = collar + Vector((0.05, 0, -0.10))                                   # the elevating mechanism's head
    W.rod(bm, collar, hub, 0.022, S, sides=8)
    feet = [Vector((0.42, y, 0.0)) for y in (-0.22, 0.22)]
    for f in feet:
        W.rod(bm, hub, f, 0.014, O, sides=8)
        W.rod(bm, f + Vector((-0.03, 0, 0.004)), f + Vector((0.03, 0, 0.004)), 0.016, S, sides=6)   # a foot
    # The elevating screw down between the legs, its crank, the traversing handwheel.
    screw_foot = hub + Vector((0.02, 0, -0.26))
    W.rod(bm, hub, screw_foot, 0.017, S, sides=8)
    W.rod(bm, hub + Vector((0, 0, -0.06)), hub + Vector((0, 0.09, -0.06)), 0.006, S, sides=6)
    W.rod(bm, hub + Vector((0, 0.09, -0.06)), hub + Vector((0.03, 0.09, -0.10)), 0.007, K, sides=6)
    W.rod(bm, hub + Vector((0.0, -0.05, 0.0)), hub + Vector((0.0, -0.075, 0.0)), 0.035, S, sides=12)
    # The cross-leveling chain, leg to leg at a third of their height.
    a = hub.lerp(feet[0], 0.66)
    b = hub.lerp(feet[1], 0.66)
    W.rod(bm, a, b, 0.006, S, sides=5)
    # The M4 sight on its bracket, on the left of the collar.
    bracket = collar + Vector((0, -0.07, 0.02))
    W.rod(bm, collar + Vector((0, -0.04, 0)), bracket, 0.008, S, sides=6)
    W.prism(bm, [(bracket.x - 0.035, bracket.z - 0.02), (bracket.x - 0.035, bracket.z + 0.03),
                 (bracket.x + 0.03, bracket.z + 0.03), (bracket.x + 0.03, bracket.z - 0.02)], 0.035, O, y0=bracket.y - 0.02, chamfer=0.004)
    W.rod(bm, Vector((bracket.x + 0.03, bracket.y - 0.02, bracket.z + 0.02)), Vector((bracket.x + 0.07, bracket.y - 0.02, bracket.z + 0.035)), 0.009, K, sides=8)
    return along(TUBE)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    img = bpy.data.images.load(os.path.join(OUT, "weapons_palette.png"))
    mat = bpy.data.materials.new("weapons")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

    bm = bmesh.new()
    muzzle = build(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = ((f.material_index + 0.5) / len(W.PALETTE), 0.5)
    me = bpy.data.meshes.new("mortar_set")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
    obj = bpy.data.objects.new("mortar_set", me)
    bpy.context.scene.collection.objects.link(obj)
    e = bpy.data.objects.new("mortar_set.muzzle", None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = obj
    e.location = muzzle
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print(f"[mortar] {tris} triangles, muzzle {tuple(round(c, 3) for c in muzzle)}", flush=True)

    if "--preview" in argv:
        sc = bpy.context.scene
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"; cam.data.ortho_scale = 1.4
        cam.location = (0.9, -1.8, 0.9)
        cam.rotation_euler = (Vector((0.1, 0, 0.3)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.camera = cam
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sc.collection.objects.link(sun)
        sun.data.energy = 5; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
        sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.55, 0.58, 0.6)
        sc.render.engine = "CYCLES"; sc.cycles.samples = 24
        sc.render.resolution_x = sc.render.resolution_y = 900
        sc.view_settings.view_transform = "Standard"
        os.makedirs(os.path.join(W.ROOT, "Logs", "mortar"), exist_ok=True)
        sc.render.filepath = os.path.join(W.ROOT, "Logs", "mortar", "mortar_set.png")
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam, do_unlink=True); bpy.data.objects.remove(sun, do_unlink=True)

    bpy.context.view_layer.update()
    for o in bpy.context.view_layer.objects:
        o.select_set(o is obj or o.parent is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "mortar_set.fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="FACE", path_mode="STRIP")
    with open(os.path.join(OUT, "mortar_set.json"), "w") as f:
        json.dump(dict(triangles=tris, muzzle=[round(c, 3) for c in muzzle]), f, indent=1); f.write("\n")
    print(f"[mortar] wrote {OUT}/mortar_set.fbx", flush=True)


if __name__ == "__main__":
    main()
