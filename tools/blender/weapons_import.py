"""
The weapons, sourced: eleven models made by people who model firearms, brought
to this game's measure (the owner, 2026-10-12, of the weapons built here from
prisms and rods: "those guns don't look detailed enough. We want almost
replicas, source them online").

    Blender -b --factory-startup --python tools/blender/weapons_import.py
    Blender -b --factory-startup --python tools/blender/weapons_import.py -- --out Logs/guns/out

The sources are Sketchfab downloads, each CC BY 4.0 (ASSETS.md has every
model's author, address and hash; the credits are in the game's settings), in
SourceArt/sketchfab/<name>/<name>.glb. They come every way up, at any size (the
RPD 19 cm long, the PPSh 564 units), from 8,000 triangles to 115,000, each with
its own textures. Each is:

  kept      the weapon alone: a file's second rifle, its loose cartridges and
            the round leaving its muzzle are left out
  turned    its bore along +x from the butt at 0, its top to +z, the bore on
            z = 0: the frame tools/blender/weapons.py built its weapons in, so
            the men hold these where they held those
  scaled    to the weapon's real length
  thinned   to about 5,000 triangles: a rifle is 190 pixels long at the
            closest zoom
  baked     its own textures' colour, with the shade of its own hollows, onto
            its cell of one 2048 atlas

and given the same five markers as before (butt, muzzle, up, and where the two
wrists go), at the places weapons.py had them.

Outputs, in Assets/_Project/Art/Weapons/:
    guns.fbx           m16, ak, sks, m60, rpd, ppsh, m3, m40, mosin, m79, rpg
    guns_albedo.png    2048
    guns.json          each weapon's length, triangles, source
The mortar's tube stays weapons.py's (nobody models an M19 for nothing).
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
SRC = os.path.join(ROOT, "SourceArt", "sketchfab")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Weapons")
ATLAS = 2048
TRIS = 5000

# name: its real length (m) and where the wrists go (weapons.py's own numbers), then what to do with the file:
#   keep / drop   parts of object names; flip: the muzzle is at the other end from where it was guessed;
#   over: its top was guessed wrong; pitch: degrees its muzzle is to be raised (it came resting on its bipod);
#   tris: its own budget
GUNS = {
    "m16": dict(length=0.986, hold_r=(0.268, -0.100), hold_l=(0.585, -0.062), keep=["Upper_low.003"]),
    "ak": dict(length=0.880, hold_r=(0.250, -0.104), hold_l=(0.535, -0.058)),
    "sks": dict(length=1.020, hold_r=(0.240, -0.086), hold_l=(0.560, -0.060)),
    "m60": dict(length=1.105, hold_r=(0.222, -0.104), hold_l=(0.640, -0.066), drop=["Bullet"], tris=7000),
    "rpd": dict(length=1.037, hold_r=(0.236, -0.100), hold_l=(0.640, -0.062), tris=6500, flip=True, over=True, pitch=9.0),
    "ppsh": dict(length=0.843, hold_r=(0.240, -0.088), hold_l=(0.500, -0.050)),
    "m3": dict(length=0.757, hold_r=(0.286, -0.086), hold_l=(0.450, -0.150)),
    "m40": dict(length=1.117, hold_r=(0.276, -0.092), hold_l=(0.600, -0.060), over=True),
    "mosin": dict(length=1.232, hold_r=(0.244, -0.086), hold_l=(0.620, -0.056), over=True),
    "m79": dict(length=0.731, hold_r=(0.268, -0.090), hold_l=(0.500, -0.066)),
    # (The launcher alone, 95 cm: its rocket comes in the file lying beside it, and is left out. The men's hands
    # are where they were on the one built here, which was drawn loaded.)
    "rpg": dict(length=0.950, hold_r=(0.462, -0.072), hold_l=(0.630, -0.086), drop=["rocket"], flip=True),
}


def log(msg):
    print(f"[guns] {msg}", flush=True)


def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    return lo, hi


def bring(name, cfg):
    """One weapon from its file: the wanted parts joined into one mesh, turned, scaled and thinned."""
    before = list(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, name, name + ".glb"))
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if cfg.get("keep"):
        meshes = [o for o in meshes if any(k in o.name for k in cfg["keep"])]
    meshes = [o for o in meshes if not any(k in o.name for k in cfg.get("drop", []))]
    # What lies loose beside it (cartridges, clips): small, and off to one side of the rest.
    lo, hi = bounds(meshes)
    long = max(range(3), key=lambda i: (hi - lo)[i])
    big = max(meshes, key=lambda o: (bounds([o])[1] - bounds([o])[0])[long])
    blo, bhi = bounds([big])
    keep = []
    for o in meshes:
        olo, ohi = bounds([o])
        mid = (olo + ohi) / 2
        loose = any(mid[i] < blo[i] - 0.02 * (bhi - blo)[long] or mid[i] > bhi[i] + 0.02 * (bhi - blo)[long] for i in range(3) if i != long)
        if loose and (ohi - olo)[long] < 0.3 * (bhi - blo)[long] and "sks" == name:
            continue
        keep.append(o)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in keep)
    bpy.context.view_layer.objects.active = keep[0]
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(keep) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    for o in [o for o in bpy.data.objects if o not in before and o != obj]:
        bpy.data.objects.remove(o, do_unlink=True)
    obj.name = obj.data.name = name
    me = obj.data

    # Turned: longest along x, thinnest along y.
    co = np.array([v.co[:] for v in me.vertices])
    size = co.max(0) - co.min(0)
    long, thin = int(np.argmax(size)), int(np.argmin(size))
    tall = 3 - long - thin
    co = co[:, [long, thin, tall]]
    co -= (co.max(0) + co.min(0)) / 2
    L = co[:, 0].max() - co[:, 0].min()
    # The muzzle is the end that is less tall (a butt is a hand high, a barrel a finger).
    def height(end):
        part = co[(co[:, 0] * end) > 0.38 * L]
        return part[:, 2].max() - part[:, 2].min()
    if (height(1) > height(-1)) != bool(cfg.get("flip")):
        co[:, 0] *= -1
        co[:, 1] *= -1
    if cfg.get("pitch"):
        a = math.radians(cfg["pitch"]) * (1 if np.median(co[:, 2]) < 0 else -1)
        x, z = co[:, 0].copy(), co[:, 2].copy()
        co[:, 0], co[:, 2] = x * math.cos(a) - z * math.sin(a), x * math.sin(a) + z * math.cos(a)
        L = co[:, 0].max() - co[:, 0].min()
        co[:, 0] -= (co[:, 0].max() + co[:, 0].min()) / 2
    # The bore: the middle of the muzzle's last twentieth. Most of a weapon hangs below it.
    tip = co[co[:, 0] > 0.45 * L]
    bore = (tip[:, 2].max() + tip[:, 2].min()) / 2
    if (np.median(co[:, 2]) > bore) != bool(cfg.get("over")):
        co[:, 2] *= -1
        co[:, 1] *= -1
        bore = -bore
    co[:, 2] -= bore
    co[:, 1] -= np.median(tip[:, 1])
    co[:, 0] -= co[:, 0].min()
    co *= cfg["length"] / L
    # (An odd number of the three were mirrored if the axes were not a turn of each other: put right, faces and all.)
    handed = np.linalg.det(np.eye(3)[[long, thin, tall]])
    me.vertices.foreach_set("co", co.astype(np.float32).ravel())
    if handed < 0:
        co[:, 1] *= -1
        me.vertices.foreach_set("co", co.astype(np.float32).ravel())
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.reverse_faces(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    me.update()

    was = sum(len(p.vertices) - 2 for p in me.polygons)
    want = cfg.get("tris", TRIS)
    if was > want:
        mod = obj.modifiers.new("thin", "DECIMATE")
        mod.ratio = want / was
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in obj.data.polygons:
        p.use_smooth = True
    now = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    log(f"{name}: {was} -> {now} triangles, {cfg['length']:.3f} m, {size[long]:.3f} as it came, {len(obj.data.materials)} materials")
    return obj, was


def unwrap(obj, cell, cols, rows):
    """A second set of UVs, into this weapon's cell of the atlas; its own stay, for its materials to read."""
    me = obj.data
    # (Some come with eight sets of UVs, which is all a mesh may have; their materials read the first.)
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[len(me.uv_layers) - 1])
    if me.uv_layers:
        me.uv_layers[0].active_render = True
    layer = me.uv_layers.new(name="atlas")
    me.uv_layers.active = layer
    for o in bpy.context.view_layer.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = obj.data
    layer = me.uv_layers["atlas"]                                # (the one made above is not good after a change of mode)
    uv = np.empty(len(me.loops) * 2, np.float32)
    layer.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    pad = 6 / ATLAS
    uv[:, 0] = (cell % cols + pad + uv[:, 0] * (1 - 2 * pad * cols)) / cols
    uv[:, 1] = (cell // cols + pad + uv[:, 1] * (1 - 2 * pad * rows)) / rows
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
    sc.render.bake.margin = 6
    sc.render.bake.use_clear = True
    bpy.ops.object.bake(type=kind, **({"pass_filter": passes} if passes else {}))
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[::-1].copy()


def to_srgb(l):
    l = np.clip(l, 0, 1)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)


def write_png(path, px):
    h, w, c = px.shape
    spec = oiio.ImageSpec(w, h, c, "uint8")
    out = oiio.ImageOutput.create(path)
    out.open(path, spec)
    out.write_image((np.clip(px, 0, 1) * 255 + 0.5).astype(np.uint8))
    out.close()


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

    cols, rows = 3, 4
    objs, info = [], {}
    for i, (name, cfg) in enumerate(GUNS.items()):
        obj, was = bring(name, cfg)
        obj.location = (0, i * 2.0, 0)                           # apart, so no weapon shades another
        unwrap(obj, i, cols, rows)
        # Metal as a weapon's file has it is a mirror with nothing to show in a bake of colour: its colour is kept, its shine is the game's.
        for mat in obj.data.materials:
            if mat and mat.use_nodes:
                for n in mat.node_tree.nodes:
                    if n.type == "BSDF_PRINCIPLED":
                        for l in list(n.inputs["Metallic"].links):
                            mat.node_tree.links.remove(l)
                        n.inputs["Metallic"].default_value = 0.0
        objs.append(obj)
        info[name] = dict(length=cfg["length"], triangles=sum(len(p.vertices) - 2 for p in obj.data.polygons), source_triangles=was)
    bpy.context.view_layer.update()

    img = bpy.data.images.new("guns", ATLAS, ATLAS, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    colour = bake(objs, "DIFFUSE", img, passes={"COLOR"})[..., :3]
    shade = bake(objs, "AO", img, samples=64)[..., 0]
    used = colour.sum(-1) > 1e-5
    albedo = colour * (0.55 + 0.45 * np.clip(shade * 1.3, 0, 1))[..., None]
    lum = albedo @ np.array([0.2126, 0.7152, 0.0722])
    log(f"atlas {ATLAS}: {used.mean() * 100:.0f}% used, albedo median {np.median(lum[used]):.3f}")
    write_png(os.path.join(out, "guns_albedo.png"), to_srgb(albedo))

    final = bpy.data.materials.new("guns")
    final.use_nodes = True
    nt = final.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(out, "guns_albedo.png"))
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.5
    empties = []
    for obj in objs:
        me = obj.data
        for l in [l for l in me.uv_layers if l.name != "atlas"]:
            me.uv_layers.remove(l)
        me.materials.clear()
        me.materials.append(final)
        cfg = GUNS[obj.name]
        (rx, rz), lx = cfg["hold_r"], cfg["hold_l"][0]
        # (The two wrists make a line 3.5 degrees off the bore, as weapons.py has them.)
        pts = dict(butt=(0, 0, 0), muzzle=(cfg["length"], 0, 0), up=(0, 0, 0.1), hold_r=(rx, 0, rz), hold_l=(lx, 0, rz + 0.06 * (lx - rx)))
        for key, at in pts.items():
            e = bpy.data.objects.new(f"{obj.name}.{key}", None)
            sc.collection.objects.link(e)
            e.empty_display_size = 0.02
            e.parent = obj
            e.location = at
            empties.append(e)

    # To look at: all eleven side on, to one scale, wearing what was baked.
    for i, o in enumerate(objs):
        o.location = ((i % 3) * 1.45, 0, -(i // 3) * 0.42)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 4.5
    cam.location = (2.1, -5, -0.68)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(60), 0, math.radians(-20))
    sc.world.color = (0.55, 0.60, 0.64)
    sc.cycles.samples = 24
    sc.render.resolution_x, sc.render.resolution_y = 2000, 760
    sc.view_settings.view_transform = "Standard"
    sc.render.filepath = os.path.join(ROOT, "Logs", "guns", "guns.png")
    bpy.ops.render.render(write_still=True)
    for o in objs:
        o.location = (0, 0, 0)
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.objects.remove(sun, do_unlink=True)

    bpy.context.view_layer.update()
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs or o in empties)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "guns.fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="OFF", path_mode="STRIP")
    with open(os.path.join(out, "guns.json"), "w") as f:
        json.dump(dict(weapons=info), f, indent=1)
        f.write("\n")
    log(f"wrote {out}/guns.fbx")


if __name__ == "__main__":
    main()
