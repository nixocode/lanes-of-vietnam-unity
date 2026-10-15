"""
The air strike's jet and the medevac's helicopter, sourced (PLAN §12.45; the
owner, 2026-10-13: "Source the jet and Huey for stage 4").

    Blender -b --factory-startup --python tools/blender/aircraft_import.py

Two Sketchfab models, each CC BY 4.0 (ASSETS.md: author, address, hash; credited
in the game's settings), in SourceArt/sketchfab/<name>/<name>.glb: an F-100
Super Sabre and a UH-1B. Each is joined into one mesh, turned so that its nose
is along +x and its top to +z with its middle over the origin and its lowest
point on z = 0, scaled to its real length, and its colour baked with the shade
of its own hollows onto its half of one 2048 x 1024 atlas. The helicopter's
main rotor is cut off it as a mesh of its own, about its mast, so that the game
can turn it.

Outputs, in Assets/_Project/Art/Structures/:
    aircraft.fbx          jet, huey, huey_rotor
    aircraft_albedo.png   2048 x 1024
    aircraft.json         each one's length, span, height, triangles; the rotor's hub
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
import OpenImageIO as oiio
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt", "sketchfab")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Structures")
W, H = 2048, 1024

# length: nose to tail of the airframe, m (the helicopter's without its main rotor). flip: its nose was guessed at the wrong end.
# roll: degrees it is to be turned about its length to stand the right way up (its file has it on its side).
CRAFT = {
    "jet": dict(length=15.2),
    # doors: the cargo doors (their bones' vertex groups in the file), slid open; pilots: the cockpit's doors, by
    # which the pilots sit. (A dustoff flew with its cargo doors open; the owner, 2026-10-15: "Med evac has no
    # soldiers in it".)
    "huey": dict(length=12.7, rotor=True, flip=True, roll=-90, doors=("L.side_door_04", "R.side_door_06"), pilots=("L.door_03", "R.door_05")),
}
# How far a cargo door slides aft to stand open, and how far it stands off the side it slides along, m.
SLIDE, STAND_OFF = 1.05, 0.06


def log(msg):
    print(f"[aircraft] {msg}", flush=True)


def bring(name, cfg):
    before = list(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, name, name + ".glb"))
    meshes = [o for o in bpy.data.objects if o not in before and o.type == "MESH" and "Icosphere" not in o.name]
    for o in meshes:
        # Each part's own UVs under one name, and no others: joined, a part's materials then read their own.
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
    obj.name = obj.data.name = name
    me = obj.data
    co = np.array([v.co[:] for v in me.vertices], np.float64)
    co -= (co.max(0) + co.min(0)) / 2
    if cfg.get("roll"):
        # (Whichever of its two level axes is the longer is its length; the roll is about that.)
        r = math.radians(cfg["roll"])
        k = 1 if np.ptp(co[:, 0]) >= np.ptp(co[:, 1]) else 0
        u, w = co[:, k].copy(), co[:, 2].copy()
        co[:, k], co[:, 2] = u * math.cos(r) - w * math.sin(r), u * math.sin(r) + w * math.cos(r)
    # Its length is whichever way it is longest on the level: a file may hold it at any heading.
    flat = co[:, :2] - co[:, :2].mean(0)
    _, vec = np.linalg.eigh(flat.T @ flat)
    a = vec[:, 1]
    along, across = flat @ a, flat @ np.array([-a[1], a[0]])
    co = np.column_stack([along, across, co[:, 2]])
    if name == "huey":
        # Its main rotor makes it longest whichever way the blades lie: the airframe's length is what is below them.
        body = co[co[:, 2] < co[:, 2].max() - 0.06 * (co[:, 2].max() - co[:, 2].min())]
    else:
        body = co
    L = body[:, 0].max() - body[:, 0].min()
    mid = (body[:, 0].max() + body[:, 0].min()) / 2
    co[:, 0] -= mid
    # The tail stands tall (a fin); the nose does not.
    def tall(end):
        part = co[(co[:, 0] * end) > 0.3 * L]
        return part[:, 2].max() - part[:, 2].min()
    if (tall(1) > tall(-1)) != bool(cfg.get("flip")):
        co[:, 0] *= -1
        co[:, 1] *= -1
    co[:, 1] -= (np.percentile(co[:, 1], 50))
    co *= cfg["length"] / L
    co[:, 2] -= co[:, 2].min()
    me.vertices.foreach_set("co", co.astype(np.float32).ravel())
    me.update()
    for p in me.polygons:
        p.use_smooth = True
    return obj


def group_box(obj, name):
    """The least and greatest corner of the vertices a group holds (more than half), in the object's space; None if none."""
    g = obj.vertex_groups.get(name)
    if g is None:
        return None
    vs = [v.co for v in obj.data.vertices if any(e.group == g.index and e.weight > 0.5 for e in v.groups)]
    if not vs:
        return None
    return (Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))


def open_doors(obj, cfg):
    """
    The cargo doors slid aft along the sides to stand open, and where the men sit: the doorways (their middle at
    the floor, and their length), and the pilots' seats (inboard of the cockpit's doors, at their foot). In the
    space it is built in: nose to +x, top to +z.
    """
    seats = {}
    doors = []
    for name in cfg.get("doors", ()):
        box = group_box(obj, name)
        if box is None:
            log(f"{obj.name}: no door {name}")
            continue
        lo, hi = box
        out = 1.0 if (lo.y + hi.y) > 0 else -1.0
        doors.append([round((lo.x + hi.x) / 2, 3), round(hi.y if out > 0 else lo.y, 3), round(lo.z, 3), round(hi.x - lo.x, 3)])
        g = obj.vertex_groups[name]
        for v in obj.data.vertices:
            if any(e.group == g.index and e.weight > 0.5 for e in v.groups):
                v.co.x -= SLIDE
                v.co.y += out * STAND_OFF
    pilots = []
    for name in cfg.get("pilots", ()):
        box = group_box(obj, name)
        if box is None:
            continue
        lo, hi = box
        side = 1.0 if (lo.y + hi.y) > 0 else -1.0
        pilots.append([round((lo.x + hi.x) / 2, 3), round((hi.y if side > 0 else lo.y) - side * 0.45, 3), round(lo.z, 3)])
    obj.data.update()
    seats["doors"] = doors
    seats["pilots"] = pilots
    log(f"{obj.name}: doors {doors}, pilots {pilots}")
    return seats


def cut_rotor(obj):
    """The main rotor off the helicopter: every face in the top twentieth of it that reaches out from the mast."""
    me = obj.data
    top = max(v.co.z for v in me.vertices)
    low = min(v.co.z for v in me.vertices)
    cut = top - 0.045 * (top - low)
    bm = bmesh.new()
    bm.from_mesh(me)
    faces = [f for f in bm.faces if all(v.co.z > cut for v in f.verts)]
    hub = sum((f.calc_center_median() for f in faces), Vector()) / max(1, len(faces))
    rot = bmesh.new()
    vmap = {}
    uv_src = bm.loops.layers.uv.active
    uv_dst = rot.loops.layers.uv.new("atlas")
    for f in faces:
        nf = rot.faces.new([vmap.setdefault(v, rot.verts.new(v.co - Vector((hub.x, hub.y, 0)))) for v in f.verts])
        nf.smooth = True
        for a, b in zip(f.loops, nf.loops):
            b[uv_dst].uv = a[uv_src].uv
    bmesh.ops.delete(bm, geom=faces, context="FACES")
    bm.to_mesh(me)
    bm.free()
    rme = bpy.data.meshes.new(obj.name + "_rotor")
    rot.to_mesh(rme)
    rot.free()
    r = bpy.data.objects.new(obj.name + "_rotor", rme)
    bpy.context.scene.collection.objects.link(r)
    return r, (round(hub.x, 3), round(hub.y, 3))


def unwrap(obj, cell):
    me = obj.data
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[len(me.uv_layers) - 1])
    if me.uv_layers:
        me.uv_layers[0].active_render = True
    me.uv_layers.new(name="atlas")
    me.uv_layers.active = me.uv_layers["atlas"]
    bpy.context.view_layer.update()
    for o in [o for o in bpy.context.view_layer.objects if o]:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003, area_weight=0.0, correct_aspect=False, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = obj.data
    layer = me.uv_layers["atlas"]
    uv = np.empty(len(me.loops) * 2, np.float32)
    layer.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    pad = 6 / H
    uv[:, 0] = (cell + pad + uv[:, 0] * (1 - 2 * pad)) / 2
    uv[:, 1] = pad + uv[:, 1] * (1 - 2 * pad)
    layer.data.foreach_set("uv", uv.ravel())


def bake(objs, kind, img, samples=8, passes=None):
    for mat in {m for o in objs for m in o.data.materials if m}:
        nt = mat.node_tree
        t = nt.nodes.get("bake target") or nt.nodes.new("ShaderNodeTexImage")
        t.name = "bake target"
        t.image = img
        nt.nodes.active = t
    for o in [o for o in bpy.context.view_layer.objects if o]:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    sc = bpy.context.scene
    sc.cycles.samples = samples
    sc.render.bake.margin = 6
    sc.render.bake.use_clear = True
    bpy.ops.object.bake(type=kind, **({"pass_filter": passes} if passes else {}))
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[::-1].copy()


def to_srgb(l):
    l = np.clip(l, 0, 1)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)


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
    sc.world.light_settings.distance = 0.4

    objs, info = [], {}
    seats = {}
    for i, (name, cfg) in enumerate(CRAFT.items()):
        obj = bring(name, cfg)
        if cfg.get("doors"):
            seats[name] = open_doors(obj, cfg)
        obj.location = (0, i * 40.0, 0)
        unwrap(obj, i)
        for mat in obj.data.materials:
            if mat and mat.use_nodes:
                for n in mat.node_tree.nodes:
                    if n.type == "BSDF_PRINCIPLED":
                        for l in list(n.inputs["Metallic"].links):
                            mat.node_tree.links.remove(l)
                        n.inputs["Metallic"].default_value = 0.0
                        # Glass: its colour is what is seen through it, and baked flat it came out white. The
                        # inside of a cockpit by day is dark.
                        tw = n.inputs.get("Transmission Weight")
                        if ("glass" in mat.name.lower() or "window" in mat.name.lower() or "canopy" in mat.name.lower()
                                or n.inputs["Alpha"].default_value < 0.99 or n.inputs["Alpha"].is_linked
                                or (tw is not None and tw.default_value > 0.01)):
                            for l in list(n.inputs["Base Color"].links):
                                mat.node_tree.links.remove(l)
                            n.inputs["Base Color"].default_value = (0.035, 0.045, 0.05, 1.0)
                            log(f"{name}: {mat.name} baked as dark glass")
        objs.append(obj)
    bpy.context.view_layer.update()
    img = bpy.data.images.new("aircraft", W, H, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    colour = bake(objs, "DIFFUSE", img, passes={"COLOR"})[..., :3]
    shade = bake(objs, "AO", img, samples=64)[..., 0]
    albedo = colour * (0.6 + 0.4 * np.clip(shade * 1.3, 0, 1))[..., None]
    path = os.path.join(out, "aircraft_albedo.png")
    spec = oiio.ImageSpec(W, H, 3, "uint8")
    png = oiio.ImageOutput.create(path); png.open(path, spec); png.write_image((np.clip(to_srgb(albedo), 0, 1) * 255 + 0.5).astype(np.uint8)); png.close()

    final = bpy.data.materials.new("aircraft")
    final.use_nodes = True
    tex = final.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path)
    bsdf = next(n for n in final.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    final.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    out_objs = []
    for obj in objs:
        me = obj.data
        for l in [l for l in me.uv_layers if l.name != "atlas"]:
            me.uv_layers.remove(l)
        me.materials.clear()
        me.materials.append(final)
        obj.location = (0, 0, 0)
        co = np.array([v.co[:] for v in me.vertices])
        info[obj.name] = dict(length=round(float(co[:, 0].max() - co[:, 0].min()), 2), span=round(float(co[:, 1].max() - co[:, 1].min()), 2),
                              height=round(float(co[:, 2].max()), 2))
        out_objs.append(obj)
        if obj.name in seats:
            info[obj.name].update(seats[obj.name])
        if CRAFT[obj.name].get("rotor"):
            rotor, hub = cut_rotor(obj)
            rotor.data.materials.append(final)
            rco = np.array([v.co[:] for v in rotor.data.vertices])
            info[obj.name]["hub"] = hub
            info[rotor.name] = dict(span=round(float(max(np.ptp(rco[:, 0]), np.ptp(rco[:, 1]))), 2), height=round(float(rco[:, 2].min()), 2))
            out_objs.append(rotor)
    for o in out_objs:
        info[o.name]["triangles"] = sum(len(p.vertices) - 2 for p in o.data.polygons)
        log(f"{o.name}: {info[o.name]}")

    # To look at.
    for i, o in enumerate(o for o in out_objs if "rotor" not in o.name):
        o.location = (0, 0, -i * 8.0)
    rot = next((o for o in out_objs if "rotor" in o.name), None)
    if rot:
        rot.location = (info["huey"]["hub"][0], info["huey"]["hub"][1], -8.0)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 20
    cam.location = (0, -60, -2.0); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.data.energy = 4; sun.rotation_euler = (math.radians(60), 0, math.radians(-20))
    sc.world.color = (0.55, 0.60, 0.64)
    sc.cycles.samples = 24
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1280
    sc.view_settings.view_transform = "Standard"
    sc.render.filepath = os.path.join(ROOT, "Logs", "air", "aircraft.png")
    bpy.ops.render.render(write_still=True)
    for o in out_objs:
        o.location = (0, 0, 0)
    bpy.data.objects.remove(cam, do_unlink=True); bpy.data.objects.remove(sun, do_unlink=True)

    bpy.context.view_layer.update()
    for o in [o for o in bpy.context.view_layer.objects if o]:
        o.select_set(o in out_objs)
    bpy.context.view_layer.objects.active = out_objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "aircraft.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="OFF", path_mode="STRIP")
    with open(os.path.join(out, "aircraft.json"), "w") as f:
        json.dump(info, f, indent=1); f.write("\n")
    log(f"wrote {out}/aircraft.fbx")


if __name__ == "__main__":
    main()
