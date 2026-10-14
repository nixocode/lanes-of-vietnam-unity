"""
The radio man's set (PLAN §12.48; the owner, 2026-10-14, yes to "a radio-pack model"):
an AN/PRC-77, sourced, with the whip antenna and the handset built here.

    Blender -b --factory-startup --python tools/blender/radio_import.py
    Blender -b --factory-startup --python tools/blender/radio_import.py -- --out Logs/radio/out

The set is "PRC-77 Military radio" by hurraciodurrski (Petr Švenda), Sketchfab, CC BY 4.0
(ASSETS.md: address and hash; credited in the game's settings), in
SourceArt/sketchfab/radio/radio.glb. The PRC-77 is the PRC-25 of 1965 made over in
1968, the same box to look at. The file is in half-centimetres and has neither the
antenna nor the handset, which are what is seen of a radio man first, so they are made
here: a tape antenna a metre long on the set's antenna mount, and an H-189 handset, a
grip with an earpiece cup at one end and a mouthpiece at the other.

The set is stood as it is worn, its control panel up, its back to the man's, its middle
at the origin; the handset lies along +x with its middle at the origin, the earpiece
toward +x and both cups facing -y. Colour is baked, with the shade of its own hollows,
onto one 512 atlas, the set on the left three quarters and the antenna and handset in
the last quarter.

Outputs, in Assets/_Project/Art/Weapons/:
    radio.fbx          radio (the set and its antenna), handset
    radio_albedo.png   512 x 512
    radio.json         the set's size, the antenna's top, the triangles of each
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
import OpenImageIO as oiio
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt", "sketchfab", "radio", "radio.glb")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Weapons")
ATLAS = 512
# Metres a unit of the file: its box is 50.4 x 57 x 21 of them, and the set is 25 x 28.5 x 10.5 cm.
UNIT = 0.005
# The antenna's mount, in the file's units (the base of radioMain_low_5): where the whip stands.
MOUNT = Vector((-3.85, 18.0, 4.2))
ANTENNA = 1.0          # metres of tape antenna above the mount
OD = (0.17, 0.18, 0.13)   # olive drab, as the set's own paint reads
BLACK = (0.035, 0.036, 0.034)


def log(msg):
    print(f"[radio] {msg}", flush=True)


def flat(name, rgb, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    return m


def bring():
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=SRC)
    meshes = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    # The glass of the dial is two triangles that bake to nothing: left out.
    meshes = [o for o in meshes if "glass" not in o.name.lower()]
    for o in meshes:
        uvs = o.data.uv_layers
        keep = next((l.name for l in uvs if l.active_render), uvs[0].name if uvs else None)
        for l in [l for l in uvs if l.name != keep]:
            uvs.remove(l)
        if uvs:
            uvs[0].name = "UVMap"
    for o in bpy.context.view_layer.objects:
        o.select_set(o in meshes)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    for o in [o for o in bpy.data.objects if o not in before and o != obj]:
        bpy.data.objects.remove(o, do_unlink=True)
    obj.name = obj.data.name = "radio"
    return obj


def worn(v):
    """The file's frame to the worn one: its -x (the control panel) up, its y across, its z (its depth) to y."""
    return Vector((v.y, v.z, -v.x)) * UNIT


def stand(obj):
    me = obj.data
    for v in me.vertices:
        v.co = worn(v.co)
    co = np.array([v.co[:] for v in me.vertices])
    mid = (co.max(0) + co.min(0)) / 2
    for v in me.vertices:
        v.co -= Vector(mid)
    me.update()
    return Vector(mid), co.max(0) - co.min(0)


def antenna(mid):
    base = worn(MOUNT) - mid
    bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.006, radius2=0.0025, depth=ANTENNA, location=base + Vector((0, 0, ANTENNA / 2)))
    a = bpy.context.active_object
    a.name = "antenna"
    a.data.materials.append(flat("antenna", BLACK, 0.45))
    # The tape's base: a short black spring.
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.011, depth=0.06, location=base + Vector((0, 0, 0.03)))
    s = bpy.context.active_object
    s.data.materials.append(flat("spring", BLACK, 0.5))
    # Where they stand, into their meshes: each part is laid out apart for the bake and put back at the origin.
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (a, s))
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return [a, s], base + Vector((0, 0, ANTENNA))


def handset():
    """An H-189: a grip 20 cm long, an earpiece and a mouthpiece cup off one side of its ends, a press-to-talk bar."""
    parts = []
    body = flat("handset", (0.05, 0.055, 0.045), 0.55)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.016, depth=0.17, location=(0, 0, 0), rotation=(0, math.pi / 2, 0))
    parts.append(bpy.context.active_object)
    for x, r in ((0.085, 0.032), (-0.085, 0.027)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=r, depth=0.03, location=(x, -0.022, 0), rotation=(math.pi / 2, 0, 0))
        parts.append(bpy.context.active_object)
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.013, depth=0.03, location=(x * 0.93, -0.008, 0), rotation=(math.pi / 2, 0, 0))
        parts.append(bpy.context.active_object)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0.017, 0))
    bar = bpy.context.active_object
    bar.scale = (0.06, 0.008, 0.018)
    parts.append(bar)
    for p in parts:
        p.data.materials.clear()
        p.data.materials.append(body)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in parts)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    h = bpy.context.active_object
    h.name = h.data.name = "handset"
    for p in h.data.polygons:
        p.use_smooth = True
    return h


def unwrap(obj, x0, x1):
    me = obj.data
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[len(me.uv_layers) - 1])
    if me.uv_layers:
        me.uv_layers[0].active_render = True
    me.uv_layers.new(name="atlas")
    me.uv_layers.active = me.uv_layers["atlas"]
    for o in bpy.context.view_layer.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=0.0, correct_aspect=False, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    layer = me.uv_layers["atlas"]
    uv = np.empty(len(me.loops) * 2, np.float32)
    layer.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    pad = 4 / ATLAS
    uv[:, 0] = x0 + pad + uv[:, 0] * (x1 - x0 - 2 * pad)
    uv[:, 1] = pad + uv[:, 1] * (1 - 2 * pad)
    layer.data.foreach_set("uv", uv.ravel())


def bake(objs, kind, img, samples=8, passes=None):
    for mat in {m for o in objs for m in o.data.materials if m}:
        nt = mat.node_tree
        t = nt.nodes.get("bake target") or nt.nodes.new("ShaderNodeTexImage")
        t.name = "bake target"
        t.image = img
        nt.nodes.active = t
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    sc = bpy.context.scene
    sc.cycles.samples = samples
    sc.render.bake.margin = 4
    sc.render.bake.use_clear = True
    bpy.ops.object.bake(type=kind, **({"pass_filter": passes} if passes else {}))
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[::-1].copy()


def to_srgb(l):
    l = np.clip(l, 0, 1)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.join(ROOT, argv[argv.index("--out") + 1]) if "--out" in argv else OUT
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = "GPU"
    except Exception as e:
        log(f"GPU unavailable ({e})")
    sc.world = bpy.data.worlds.new("bake")
    sc.world.light_settings.distance = 0.05

    radio = bring()
    mid, size = stand(radio)
    for mat in radio.data.materials:
        if mat and mat.use_nodes:
            for n in mat.node_tree.nodes:
                if n.type == "BSDF_PRINCIPLED":
                    for l in list(n.inputs["Metallic"].links):
                        mat.node_tree.links.remove(l)
                    n.inputs["Metallic"].default_value = 0.0
    whip, top = antenna(mid)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in whip or o is radio)
    bpy.context.view_layer.objects.active = whip[0]
    # The antenna gets its own UVs on its own part of the atlas before it is joined to the set.
    for w in whip:
        w.data.uv_layers.new(name="UVMap")
    hs = handset()
    hs.data.uv_layers.new(name="UVMap")

    unwrap(radio, 0.0, 0.75)
    for w in whip:
        unwrap(w, 0.76, 0.86)
    unwrap(hs, 0.87, 1.0)
    objs = [radio] + whip + [hs]
    for i, o in enumerate(objs):
        o.location = (i * 3.0, 0, 0)
    bpy.context.view_layer.update()
    img = bpy.data.images.new("radio", ATLAS, ATLAS, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    colour = bake(objs, "DIFFUSE", img, passes={"COLOR"})[..., :3]
    shade = bake(objs, "AO", img, samples=64)[..., 0]
    albedo = colour * (0.6 + 0.4 * np.clip(shade * 1.3, 0, 1))[..., None]
    path = os.path.join(out, "radio_albedo.png")
    png = oiio.ImageOutput.create(path)
    png.open(path, oiio.ImageSpec(ATLAS, ATLAS, 3, "uint8"))
    png.write_image((np.clip(to_srgb(albedo), 0, 1) * 255 + 0.5).astype(np.uint8))
    png.close()
    for o in objs:
        o.location = (0, 0, 0)

    final = bpy.data.materials.new("radio")
    final.use_nodes = True
    tex = final.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path)
    bsdf = next(n for n in final.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    final.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    for o in objs:
        me = o.data
        for l in [l for l in me.uv_layers if l.name != "atlas"]:
            me.uv_layers.remove(l)
        me.materials.clear()
        me.materials.append(final)
    for o in bpy.context.view_layer.objects:
        o.select_set(o is radio or o in whip)
    bpy.context.view_layer.objects.active = radio
    bpy.ops.object.join()
    radio = bpy.context.view_layer.objects.active
    radio.name = radio.data.name = "radio"
    info = {"radio": {"size": [round(float(s), 3) for s in size], "antenna_top": [round(float(c), 3) for c in top], "triangles": tris(radio)},
            "handset": {"triangles": tris(hs)}}
    log(json.dumps(info))

    # To look at: the set and the handset beside it.
    hs.location = (0.3, 0, 0.1)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 1.3
    cam.location = (1.4, -1.8, 1.2)
    cam.rotation_euler = (Vector((0.1, 0, 0.4)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.data.energy = 4; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    sc.world.color = (0.5, 0.52, 0.55)
    sc.cycles.samples = 24
    sc.render.resolution_x, sc.render.resolution_y = 1000, 1000
    sc.view_settings.view_transform = "Standard"
    sc.render.filepath = os.path.join(ROOT, "Logs", "radio", "radio.png")
    bpy.ops.render.render(write_still=True)
    hs.location = (0, 0, 0)
    bpy.data.objects.remove(cam, do_unlink=True); bpy.data.objects.remove(sun, do_unlink=True)

    for o in bpy.context.view_layer.objects:
        o.select_set(o is radio or o is hs)
    bpy.context.view_layer.objects.active = radio
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "radio.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="OFF", path_mode="STRIP")
    with open(os.path.join(out, "radio.json"), "w") as f:
        json.dump(info, f, indent=1); f.write("\n")
    log(f"wrote {out}/radio.fbx")


if __name__ == "__main__":
    main()
