"""
Bake plants into the billboards the game draws (PLAN §12.10: vegetation).

    Blender -b --factory-startup --python tools/blender/plant_bake.py -- [species ...] [--preview-only]

Why billboards, and why baked. This camera never rotates — it pans and dollies
— so every plant is only ever seen from within a few degrees of one direction.
A plant rendered once from that direction, with its albedo, normals, coverage
and self-occlusion stored, and then lit in the game by the game's own sun and
sky, looks like the full plant for the price of one quad. So the source can be
as heavy as it likes: a 3.9 M-polygon scanned tree bakes as easily as a fern,
and what the game pays is texture, not triangles.

For each species (RECIPES below; sources in SourceArt/, logged in ASSETS.md)
each variant is rendered by Cycles from the game's view — orthographic, looking
along +Y, pitched down BAKE_PITCH — into a multilayer EXR, and read back:

    coverage    Combined alpha, over a transparent film
    albedo      the Diffuse Color pass, un-premultiplied
    normal      the Normal pass, turned into the bake camera's frame
    occlusion   the AO pass, un-premultiplied
    sunlit      the Diffuse Direct pass under the game's own sun alone, at
                strength pi, so it reads cos(angle) x visibility: how much of
                that sun each leaf actually gets, the crown shading itself

The sun can be baked because it never moves: it is measured off the one sky
photograph (sky.json) and stands at azimuth -125, elevation 43. A billboard
without this is lit as if every leaf were on the crown's surface, which is
why a tree lit from the front looked flat and bright.

Outputs, in Assets/_Project/Art/Plants/, one atlas per species (power-of-two,
so Unity will crunch it; see ArtImport):

    <species>_albedo.png   RGB albedo (sRGB), A coverage
    <species>_normal.png   R, G the normal's x and y in the bake camera's frame
                           (right, up; z is rebuilt, it always faces the
                           camera), B occlusion, A sunlit
    <species>.json         per variant: its rectangle in the atlas, its size in
                           metres, where its root is, the pitch it was baked at

and a lit preview of every variant in captures/plants/<species>.png, to look at.

Coordinates: Blender (x, y, z) is the game's (x, z, y): x right, y away from
the camera, z up.
"""
import json
import math
import os
import re
import sys

import bpy
import mathutils
import numpy as np
import OpenImageIO as oiio

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Plants")
PREVIEW = os.path.join(ROOT, "captures", "plants")

# The game sees the playfield between about 2.4 degrees (120 m) and 12 degrees
# (23 m) below the horizon; 6 is the middle, and a vertical card shows the
# image foreshortened by cos 6 = 0.995 — nothing.
BAKE_PITCH = 6.0

# group: a regex whose first group names the variant an object belongs to
#        (None: everything is one variant).
# exclude: object-name fragments that are not the plant (pots, soil).
# ppm: texels per metre. Set from the nearest the game draws the species:
#      a screen 1440 px tall through a 19 degree lens shows 4300 / d pixels a
#      metre at d metres, so 25 m wants ~170 and the field glasses ~2.75x that.
# ground: "zero" (the scan's own ground plane) or "min" (the lowest included
#      point, for a plant lifted out of its pot).
RECIPES = {
    "fern": dict(src="polyhaven/models/fern_02/fern_02_2k.gltf", group=r"_([a-z])$", ppm=320, ground="zero"),
    "calathea": dict(src="polyhaven/models/calathea_orbifolia_01/calathea_orbifolia_01_2k.gltf",
                     group=r"_([a-z])$", ppm=320, ground="zero"),
    "anthurium": dict(src="polyhaven/models/anthurium_botany_01/anthurium_botany_01_2k.gltf",
                      group=r"_([a-z])$", ppm=320, ground="zero"),
    "pachira": dict(src="polyhaven/models/pachira_aquatica_01/pachira_aquatica_01_2k.gltf",
                    group=r"_([a-z])$", ppm=256, ground="zero"),
    "aroid": dict(src="polyhaven/models/potted_plant_02/potted_plant_02_2k.gltf",
                  group=None, exclude=["_pot", "_dirt"], ppm=320, ground="min"),
    "ficus": dict(src="polyhaven/models/potted_plant_01/potted_plant_01_2k.gltf",
                  group=None, exclude=["_pot", "_pebbles"], ppm=320, ground="min"),
    # island_tree_03 is left out: its mesh carries a patch of sand at its foot.
    "island_tree": dict(src=["polyhaven/models/island_tree_01/island_tree_01_2k.gltf",
                             "polyhaven/models/island_tree_02/island_tree_02_2k.gltf"],
                        group=r"island_tree_(\d+)", ppm=128, ground="zero"),
    "jacaranda": dict(src="polyhaven/models/jacaranda_tree/jacaranda_tree_2k.gltf", group=None, ppm=64, ground="zero"),

    # Built here, not imported: grass clumps from ambientCG's scanned blades
    # (CC0). Nothing scanned is tall enough — Poly Haven's grass tops out at
    # 40 cm; elephant grass stands 2-3 m.
    "elephant_grass": dict(build="grass", variants=6, ppm=150, ground="zero",
                           blades=["Foliage001", "Foliage008"], count=(90, 150), height=(2.0, 3.1),
                           spread=0.28, lean=(0.05, 0.55), droop=(0.6, 1.5),
                           plumes="Foliage002", plume_count=(3, 9), plume_height=(2.4, 3.5)),
    "grass_tuft": dict(build="grass", variants=6, ppm=240, ground="zero",
                       # Not Foliage006: its lime green (albedo G 0.27) is a lawn's, not
                       # the reference's olive field grass.
                       blades=["Foliage001", "Foliage008", "Foliage005"], count=(45, 80), height=(0.45, 1.0),
                       spread=0.12, lean=(0.1, 0.7), droop=(0.3, 1.1)),
}

GRASS_SRC = "ambientcg"

PAD = 12            # texels of dilated border around every variant in an atlas
SAMPLES = 96


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = [x for x in a if not x.startswith("--")] or list(RECIPES)
    return names, "--preview-only" in a


# --- scene -----------------------------------------------------------------------

def setup_render(scene):
    scene.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    try:
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        scene.cycles.device = "GPU"
    except Exception as e:                                      # CPU is only slower
        print(f"[plants] GPU unavailable ({e}); rendering on the CPU", flush=True)
    scene.cycles.samples = SAMPLES
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 4
    scene.render.film_transparent = True
    scene.render.filter_size = 1.2
    scene.view_settings.view_transform = "Standard"
    vl = scene.view_layers[0]
    vl.use_pass_combined = True
    vl.use_pass_diffuse_color = True
    vl.use_pass_normal = True
    vl.use_pass_ambient_occlusion = True
    vl.use_pass_diffuse_direct = True
    scene.render.image_settings.media_type = "MULTI_LAYER_IMAGE"       # Blender 5: a media type, not a format
    scene.render.image_settings.file_format = "OPEN_EXR_MULTILAYER"
    scene.render.image_settings.color_depth = "32"
    scene.render.image_settings.exr_codec = "ZIP"

    # The game's sun and nothing else: a black world, so Diffuse Direct is the
    # sun alone. Strength pi makes a Lambertian leaf's Diffuse Direct read
    # cos(angle) x visibility, 0..1.
    world = bpy.data.worlds.new("bake")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.0
    scene.world = world
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = math.pi
    sun.angle = math.radians(0.53)          # the sun's own disc
    so = bpy.data.objects.new("sun", sun)
    scene.collection.objects.link(so)
    # The game's sun: azimuth -125 (behind the camera's left shoulder), elevation 43.
    az, el = math.radians(-125), math.radians(43)
    to_sun = mathutils.Vector((math.cos(el) * math.sin(az), math.cos(el) * math.cos(az), math.sin(el)))
    so.rotation_euler = (-to_sun).to_track_quat("-Z", "Y").to_euler()

    cam = bpy.data.cameras.new("bake")
    cam.type = "ORTHO"
    co = bpy.data.objects.new("bake", cam)
    scene.collection.objects.link(co)
    scene.camera = co
    co.rotation_euler = (math.radians(90 - BAKE_PITCH), 0, 0)
    return co


def camera_frame(co):
    m = co.matrix_world.to_3x3()
    right = m @ mathutils.Vector((1, 0, 0))
    up = m @ mathutils.Vector((0, 1, 0))
    back = m @ mathutils.Vector((0, 0, 1))
    return right.normalized(), up.normalized(), back.normalized()


def import_sources(recipe):
    if recipe.get("build") == "grass":
        return build_grass(recipe)
    srcs = recipe["src"] if isinstance(recipe["src"], list) else [recipe["src"]]
    for s in srcs:
        bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, s))
    # Transmission and emission would change nothing in the passes, but the AO
    # pass needs every surface opaque to rays exactly as the leaves' alpha says.
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    exclude = recipe.get("exclude", [])
    objs = [o for o in objs if not any(x in o.name for x in exclude)]
    for o in bpy.data.objects:
        if o.type == "MESH" and o not in objs:
            o.hide_render = True
    groups = {}
    for o in objs:
        key = "a"
        if recipe.get("group"):
            m = re.search(recipe["group"], o.name)
            key = m.group(1) if m else o.name
        groups.setdefault(key, []).append(o)
    return dict(sorted(groups.items()))


# --- grass, built from scanned blades ----------------------------------------------

def load_rgba(path):
    inp = oiio.ImageInput.open(path)
    spec = inp.spec()
    px = np.asarray(inp.read_image(0, 0, 0, spec.nchannels, "float")).reshape(spec.height, spec.width, spec.nchannels)
    inp.close()
    return px


def blade_rects(atlas):
    """Every blade in a scanned atlas: its bounding box in UV (v up), its long
    axis, and which end is the base (the wider one). Found by labelling the
    opacity mask at quarter resolution."""
    o = load_rgba(os.path.join(SRC, GRASS_SRC, atlas, f"{atlas}_2K-PNG_Opacity.png"))[..., 0]
    H, W = o.shape
    k = 4
    m = o[: H // k * k, : W // k * k].reshape(H // k, k, W // k, k).max((1, 3)) > 0.5
    h, w = m.shape
    label = np.zeros((h, w), np.int32)
    rects = []
    n = 0
    for y0 in range(h):
        for x0 in range(w):
            if not m[y0, x0] or label[y0, x0]:
                continue
            n += 1
            stack = [(y0, x0)]
            label[y0, x0] = n
            ys, xs = [], []
            while stack:
                y, x = stack.pop()
                ys.append(y); xs.append(x)
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < h and 0 <= xx < w and m[yy, xx] and not label[yy, xx]:
                        label[yy, xx] = n
                        stack.append((yy, xx))
            if len(ys) < 40:
                continue
            ya, yb, xa, xb = min(ys), max(ys) + 1, min(xs), max(xs) + 1
            vertical = (yb - ya) >= (xb - xa)
            sub = (label[ya:yb, xa:xb] == n)
            if vertical:
                ends = sub[: max(1, (yb - ya) // 6)].sum(), sub[-max(1, (yb - ya) // 6):].sum()
                base = "bottom" if ends[1] >= ends[0] else "top"
            else:
                ends = sub[:, : max(1, (xb - xa) // 6)].sum(), sub[:, -max(1, (xb - xa) // 6):].sum()
                base = "left" if ends[0] >= ends[1] else "right"
            # UV with v up: image row y is v = 1 - y/h.
            rects.append(dict(u0=xa / w, u1=xb / w, v0=1 - yb / h, v1=1 - ya / h, vertical=vertical, base=base,
                              aspect=max(yb - ya, xb - xa) / max(1, min(yb - ya, xb - xa))))
    return rects


def blade_material(atlas):
    name = f"blade {atlas}"
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    d = os.path.join(SRC, GRASS_SRC, atlas)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    col = nt.nodes.new("ShaderNodeTexImage"); col.image = bpy.data.images.load(os.path.join(d, f"{atlas}_2K-PNG_Color.png"))
    op = nt.nodes.new("ShaderNodeTexImage"); op.image = bpy.data.images.load(os.path.join(d, f"{atlas}_2K-PNG_Opacity.png"))
    op.image.colorspace_settings.name = "Non-Color"
    nm = nt.nodes.new("ShaderNodeTexImage"); nm.image = bpy.data.images.load(os.path.join(d, f"{atlas}_2K-PNG_NormalGL.png"))
    nm.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(op.outputs["Color"], bsdf.inputs["Alpha"])
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.55
    return mat


def blade_mesh(name, rect, length, width, lean, droop, azimuth, base, segments=8, twist=0.0):
    """A blade as a strip of quads rising from `base`: it leaves the ground at
    `lean` radians from vertical and bends further, by `droop` at the tip,
    toward `azimuth`."""
    verts, faces, uvs = [], [], []
    d = mathutils.Vector((math.cos(azimuth), math.sin(azimuth), 0))
    side = mathutils.Vector((-d.y, d.x, 0))
    p = mathutils.Vector(base)
    step = length / segments
    pts = [p.copy()]
    for i in range(segments):
        t = (i + 0.5) / segments
        ang = lean + droop * t * t
        p = p + (d * math.sin(ang) + mathutils.Vector((0, 0, 1)) * math.cos(ang)) * step
        pts.append(p.copy())
    for i, q in enumerate(pts):
        t = i / segments
        tw = side * math.cos(twist * t) + d * math.sin(twist * t) * 0.3
        verts.append(q - tw * width * 0.5)
        verts.append(q + tw * width * 0.5)
    for i in range(segments):
        a = i * 2
        faces.append((a, a + 1, a + 3, a + 2))

    def uv(t, s_):                          # t along the blade (0 base), s_ across (0..1)
        r = rect
        if r["vertical"]:
            v = r["v0"] + t * (r["v1"] - r["v0"]) if r["base"] == "bottom" else r["v1"] - t * (r["v1"] - r["v0"])
            return (r["u0"] + s_ * (r["u1"] - r["u0"]), v)
        u = r["u0"] + t * (r["u1"] - r["u0"]) if r["base"] == "left" else r["u1"] - t * (r["u1"] - r["u0"])
        return (u, r["v0"] + s_ * (r["v1"] - r["v0"]))

    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    uvl = me.uv_layers.new(name="UVMap")
    for f in me.polygons:
        for li in f.loop_indices:
            vi = me.loops[li].vertex_index
            uvl.data[li].uv = uv((vi // 2) / segments, vi % 2)
    return me


def build_grass(recipe):
    """Grass clumps: blades from the scanned atlases, fanned out from a small
    base, leaning and drooping more at the edge of the clump than in its
    heart, with plumes standing above for elephant grass."""
    rng = np.random.default_rng(20260930 + len(recipe["blades"]))
    atlases = {a: blade_rects(a) for a in recipe["blades"]}
    for a, r in atlases.items():
        print(f"[plants] {a}: {len(r)} blades found", flush=True)
    plumes = blade_rects(recipe["plumes"]) if recipe.get("plumes") else []
    groups = {}
    for vi in range(recipe["variants"]):
        key = "abcdefghij"[vi]
        objs = []
        ox = vi * 20.0                                     # variants side by side, far apart
        top = rng.uniform(*recipe["height"])
        n = int(rng.integers(*recipe["count"]))
        for b in range(n):
            atlas = recipe["blades"][int(rng.integers(len(recipe["blades"])))]
            rect = atlas_pick(atlases[atlas], rng)
            r = recipe["spread"] * math.sqrt(rng.uniform())
            az = rng.uniform(0, 2 * math.pi)
            base = (ox + r * math.cos(az), r * math.sin(az), 0.0)
            edge = r / max(recipe["spread"], 1e-3)
            lean = rng.uniform(*recipe["lean"]) * (0.4 + 0.8 * edge)
            droop = rng.uniform(*recipe["droop"]) * (0.5 + 0.7 * edge)
            length = top * rng.uniform(0.55, 1.1)
            width = length / max(rect["aspect"], 4) * rng.uniform(0.9, 1.3)
            me = blade_mesh(f"blade {key}{b}", rect, length, width, lean, droop,
                            az + rng.uniform(-0.6, 0.6), base, twist=rng.uniform(-1.2, 1.2))
            me.materials.append(blade_material(atlas))
            o = bpy.data.objects.new(me.name, me)
            bpy.context.scene.collection.objects.link(o)
            objs.append(o)
        if plumes:
            for b in range(int(rng.integers(*recipe["plume_count"]))):
                rect = atlas_pick(plumes, rng)
                r = recipe["spread"] * 0.6 * math.sqrt(rng.uniform())
                az = rng.uniform(0, 2 * math.pi)
                length = rng.uniform(*recipe["plume_height"])
                width = length / max(rect["aspect"], 6)
                me = blade_mesh(f"plume {key}{b}", rect, length, width, rng.uniform(0.02, 0.2), rng.uniform(0.05, 0.35),
                                az, (ox + r * math.cos(az), r * math.sin(az), 0.0), segments=10)
                me.materials.append(blade_material(recipe["plumes"]))
                o = bpy.data.objects.new(me.name, me)
                bpy.context.scene.collection.objects.link(o)
                objs.append(o)
        bpy.context.view_layer.update()
        groups[key] = objs
    return groups


def atlas_pick(rects, rng):
    # Long blades are the ones worth drawing; the stubs are offcuts.
    long_ = [r for r in rects if r["aspect"] > 5] or rects
    return long_[int(rng.integers(len(long_)))]


def bounds(objs):
    pts = [o.matrix_world @ mathutils.Vector(c) for o in objs for c in o.bound_box]
    mn = mathutils.Vector([min(p[i] for p in pts) for i in range(3)])
    mx = mathutils.Vector([max(p[i] for p in pts) for i in range(3)])
    return mn, mx, pts


# --- one variant --------------------------------------------------------------------

def render_variant(scene, co, objs, recipe, tmp):
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in objs
    mn, mx, pts = bounds(objs)
    ground_z = 0.0 if recipe["ground"] == "zero" else mn.z
    right, up, back = camera_frame(co)

    # Frame the variant: its corners (above the ground) in the camera's plane.
    corners = [mathutils.Vector((p.x, p.y, max(p.z, ground_z))) for p in pts]
    xs = [c.dot(right) for c in corners]
    ys = [c.dot(up) for c in corners]
    root = mathutils.Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, ground_z))
    x0, x1 = min(xs), max(xs)
    y0 = min(min(ys), root.dot(up))
    y1 = max(ys)
    w_m, h_m = (x1 - x0) * 1.02, (y1 - y0) * 1.02
    ppm = recipe["ppm"]
    W = max(8, int(math.ceil(w_m * ppm / 4)) * 4)
    H = max(8, int(math.ceil(h_m * ppm / 4)) * 4)
    w_m, h_m = W / ppm, H / ppm

    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    depth = max((c - root).length for c in corners) + 5
    co.location = right * cx + up * cy + back * depth
    co.data.ortho_scale = max(w_m, h_m)
    co.data.clip_start = 0.01
    co.data.clip_end = depth * 2 + 10
    scene.render.resolution_x, scene.render.resolution_y = W, H
    scene.render.resolution_percentage = 100
    # AO reaches about a fifth of the plant: enough to darken the heart of a
    # crown, not so far that every leaf reads as shadowed.
    scene.world.light_settings.distance = max(0.05, 0.2 * max(mx.z - ground_z, w_m))

    scene.render.filepath = tmp
    bpy.ops.render.render(write_still=True)

    # Where the root lands, in texels from the image's bottom-left.
    root_u = (root.dot(right) - (cx - w_m / 2)) / w_m
    root_v = (root.dot(up) - (cy - h_m / 2)) / h_m
    return W, H, w_m, h_m, (root_u, root_v), (mx.z - ground_z)


def read_passes(path, W, H):
    """Every channel of a multilayer EXR. Blender 5 writes each pass as its own
    part, so all parts are read, not just the first."""
    inp = oiio.ImageInput.open(path)
    if inp is None:
        raise RuntimeError(f"cannot read {path}: {oiio.geterror()}")
    chans = {}
    part = 0
    while inp.seek_subimage(part, 0):
        spec = inp.spec()
        px = np.asarray(inp.read_image(part, 0, 0, spec.nchannels, "float"))
        px = px.reshape(spec.height, spec.width, spec.nchannels)[::-1]          # row 0 = bottom
        for i, n in enumerate(spec.channelnames):
            chans[n] = px[..., i]
        part += 1
    inp.close()

    def ch(suffix):
        for n, v in chans.items():
            if n.endswith(suffix):
                return v
        raise KeyError(f"no channel ending {suffix} in {sorted(chans)}")

    a = ch("Combined.A")
    col = np.stack([ch("Diffuse Color.R"), ch("Diffuse Color.G"), ch("Diffuse Color.B")], -1)
    nrm = np.stack([ch("Normal.X"), ch("Normal.Y"), ch("Normal.Z")], -1)
    ao = ch("Ambient Occlusion.R")
    sun = ch("Diffuse Direct.G")
    return a, col, nrm, ao, sun


def unpremultiply(x, a):
    return np.where(a[..., None] > 1e-4, x / np.maximum(a[..., None], 1e-4), 0)


def push_pull(rgb, w):
    """Fill empty texels from their neighbours (weighted pyramid), so neither
    bilinear filtering nor mips pull the background into a leaf's edge."""
    levels = [(rgb * w[..., None], w)]
    while min(levels[-1][1].shape) > 1:
        c, ww = levels[-1]
        h, wd = ww.shape
        h2, w2 = (h + 1) // 2, (wd + 1) // 2
        cp = np.zeros((h2 * 2, w2 * 2, 3)); cp[:h, :wd] = c
        wp = np.zeros((h2 * 2, w2 * 2)); wp[:h, :wd] = ww
        levels.append((cp.reshape(h2, 2, w2, 2, 3).sum((1, 3)), wp.reshape(h2, 2, w2, 2).sum((1, 3))))
    filled = levels[-1][0] / np.maximum(levels[-1][1][..., None], 1e-6)
    for c, ww in reversed(levels[:-1]):
        h, wd = ww.shape
        up = np.repeat(np.repeat(filled, 2, 0), 2, 1)[:h, :wd]
        own = c / np.maximum(ww[..., None], 1e-6)
        k = np.clip(ww, 0, 1)[..., None]
        filled = own * k + up * (1 - k)
    return filled


def to_srgb(l):
    l = np.clip(l, 0, 1)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)


# --- atlas ---------------------------------------------------------------------------

def pack(sizes):
    """Shelf-pack rectangles (w, h) into the smallest power-of-two square-ish atlas."""
    order = sorted(range(len(sizes)), key=lambda i: -sizes[i][1])
    area = sum((w + 2 * PAD) * (h + 2 * PAD) for w, h in sizes)
    side = 64
    while True:
        for W, H in ((side, side), (side * 2, side)):
            if W * H < area:
                continue
            pos, x, y, shelf = {}, 0, 0, 0
            ok = True
            for i in order:
                w, h = sizes[i][0] + 2 * PAD, sizes[i][1] + 2 * PAD
                if w > W:
                    ok = False; break
                if x + w > W:
                    x, y, shelf = 0, y + shelf, 0
                if y + h > H:
                    ok = False; break
                pos[i] = (x + PAD, y + PAD)
                x += w
                shelf = max(shelf, h)
            if ok:
                return W, H, pos
        side *= 2


def bake_species(name, recipe, scene, co, preview_only):
    bpy.ops.object.select_all(action="DESELECT")
    for o in list(bpy.data.objects):
        if o.type in ("MESH", "EMPTY"):
            bpy.data.objects.remove(o, do_unlink=True)
    groups = import_sources(recipe)
    right, up, back = camera_frame(co)
    tmp = os.path.join(bpy.app.tempdir, f"plant_{name}.exr")
    variants = []
    for key, objs in groups.items():
        polys = sum(len(o.data.polygons) for o in objs)
        W, H, w_m, h_m, root_uv, height = render_variant(scene, co, objs, recipe, tmp)
        a, col, nrm, ao, sun = read_passes(tmp, W, H)
        albedo = unpremultiply(col, a)
        n = unpremultiply(nrm, a)
        # Into the bake camera's frame: x right, y up, z toward the camera.
        R, U, B = np.array(right), np.array(up), np.array(back)
        n = np.stack([n @ R, n @ U, n @ B], -1)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        n[..., 2] = np.abs(n[..., 2])                      # a leaf's back faces the camera too
        occ = np.clip(unpremultiply(ao[..., None], a)[..., 0], 0, 1)
        sunlit = np.clip(unpremultiply(sun[..., None], a)[..., 0], 0, 1)
        # The preview: the game's light, roughly — its sun and a sky ambient
        # of about a third of it, through the baked maps.
        lit = albedo * (sunlit[..., None] * np.array([1.15, 1.0, 0.71]) + 0.35 * occ[..., None] * np.array([0.8, 0.95, 1.2]))
        beauty = np.concatenate([lit * a[..., None], a[..., None]], -1)
        variants.append(dict(key=key, W=W, H=H, w_m=w_m, h_m=h_m, root=root_uv, height=height, polys=polys,
                             a=a, albedo=albedo, n=n, occ=occ, sunlit=sunlit, beauty=beauty))
        cover = float((a > 0.5).mean())
        mean_alb = albedo[a > 0.5].mean(0) if (a > 0.5).any() else np.zeros(3)
        print(f"[plants] {name}/{key}: {polys:,} polygons, {h_m:.2f} x {w_m:.2f} m, {W}x{H} texels, "
              f"coverage {cover:.2f}, albedo {np.round(mean_alb, 3).tolist()}, occlusion {occ[a > 0.5].mean():.2f}, "
              f"sunlit {sunlit[a > 0.5].mean():.2f}",
              flush=True)

    # Lit preview: every variant side by side on grey, to look at.
    os.makedirs(PREVIEW, exist_ok=True)
    Hmax = max(v["H"] for v in variants)
    sheet = np.full((Hmax, sum(v["W"] + 16 for v in variants), 3), 0.35)
    x = 0
    for v in variants:
        b = v["beauty"]
        sheet[:v["H"], x:x + v["W"]] = b[..., :3] + sheet[:v["H"], x:x + v["W"]] * (1 - b[..., 3:4])
        x += v["W"] + 16
    write_png(os.path.join(PREVIEW, f"{name}.png"), to_srgb(sheet[::-1]))
    if preview_only:
        return

    AW, AH, pos = pack([(v["W"], v["H"]) for v in variants])
    alb = np.zeros((AH, AW, 4))
    nor = np.zeros((AH, AW, 4))
    meta = []
    for i, v in enumerate(variants):
        x0, y0 = pos[i]
        W, H = v["W"], v["H"]
        # Dilate each variant into its own padded cell, so mips never mix two plants.
        cw, ch = W + 2 * PAD, H + 2 * PAD
        wt = np.zeros((ch, cw)); wt[PAD:PAD + H, PAD:PAD + W] = (v["a"] > 0.02).astype(float)
        cell_rgb = np.zeros((ch, cw, 3)); cell_rgb[PAD:PAD + H, PAD:PAD + W] = v["albedo"]
        cell_n = np.zeros((ch, cw, 3)); cell_n[PAD:PAD + H, PAD:PAD + W] = np.concatenate(
            [v["n"][..., :2], v["occ"][..., None]], -1)
        cell_s = np.zeros((ch, cw, 3)); cell_s[PAD:PAD + H, PAD:PAD + W, 0] = v["sunlit"]
        cell_rgb = push_pull(cell_rgb, wt)
        cell_n = push_pull(cell_n, wt)
        cell_s = push_pull(cell_s, wt)
        cell_a = np.zeros((ch, cw)); cell_a[PAD:PAD + H, PAD:PAD + W] = v["a"]
        ys, xs = slice(y0 - PAD, y0 - PAD + ch), slice(x0 - PAD, x0 - PAD + cw)
        alb[ys, xs, :3] = to_srgb(cell_rgb)
        alb[ys, xs, 3] = np.clip(cell_a, 0, 1)
        nor[ys, xs, :2] = cell_n[..., :2] * 0.5 + 0.5
        nor[ys, xs, 2] = np.clip(cell_n[..., 2], 0, 1)
        nor[ys, xs, 3] = np.clip(cell_s[..., 0], 0, 1)
        meta.append(dict(key=v["key"], rect=[x0 / AW, y0 / AH, (x0 + W) / AW, (y0 + H) / AH],
                         size_m=[round(v["w_m"], 4), round(v["h_m"], 4)],
                         root=[round(v["root"][0], 4), round(v["root"][1], 4)],
                         height_m=round(v["height"], 3), source_polygons=v["polys"]))
    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, f"{name}_albedo.png"), alb[::-1], alpha=True)
    write_png(os.path.join(OUT, f"{name}_normal.png"), nor[::-1], alpha=True)
    with open(os.path.join(OUT, f"{name}.json"), "w") as f:
        json.dump(dict(species=name, pitch_deg=BAKE_PITCH, atlas=[AW, AH], ppm=recipe["ppm"],
                       source=recipe.get("src") or {"built": recipe.get("build"), "blades": recipe.get("blades"),
                                                    "plumes": recipe.get("plumes")},
                       variants=meta), f, indent=1)
    print(f"[plants] {name}: {len(variants)} variants in a {AW}x{AH} atlas", flush=True)


def write_png(path, rgb, alpha=False):
    h, w, c = rgb.shape
    spec = oiio.ImageSpec(w, h, c, "uint8")
    # Our alpha is straight, as PNG's is. Without this OpenImageIO assumes it is
    # premultiplied and divides by it again on the way out: every leaf edge white.
    spec.attribute("oiio:UnassociatedAlpha", 1)
    out = oiio.ImageOutput.create(path)
    out.open(path, spec)
    out.write_image((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8))
    out.close()


def main():
    names, preview_only = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    co = setup_render(scene)
    for n in names:
        bake_species(n, RECIPES[n], scene, co, preview_only)


main()
