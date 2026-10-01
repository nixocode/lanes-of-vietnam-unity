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

import bmesh
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
                           blades=["Foliage001", "Foliage008"], count=(170, 260), height=(2.0, 3.1),
                           spread=0.35, lean=(0.05, 0.5), droop=(0.9, 2.1), width=0.5,
                           plumes="Foliage002", plume_count=(3, 9), plume_height=(2.4, 3.5)),
    "grass_tuft": dict(build="grass", variants=6, ppm=240, ground="zero",
                       # Not Foliage006: its lime green (albedo G 0.27) is a lawn's, not
                       # the reference's olive field grass.
                       blades=["Foliage001", "Foliage008", "Foliage005"], count=(90, 150), height=(0.45, 1.0),
                       spread=0.16, lean=(0.1, 0.6), droop=(0.5, 1.5), width=0.5),
}

GRASS_SRC = "ambientcg"

# The coconut palm, built: nothing on Poly Haven is a palm, and the Sketchfab
# ones (ASSETS.md) need the owner's login. A curved, ringed trunk in Poly
# Haven's palm_tree_bark scan (CC0) and a crown of arching fronds whose
# leaflets are ambientCG Foliage008's scanned blades (CC0).
RECIPES["coconut_palm"] = dict(build="palm", variants=4, ppm=48, ground="zero",
                               height=(13.0, 19.0), fronds=(20, 27), dead=(2, 5), frond_length=(4.2, 5.6),
                               leaflets=(80, 110), bark="polyhaven/palm_tree_bark/palm_tree_bark", blades="Foliage008")

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


RECIPES["bamboo"] = dict(build="bamboo", variants=4, ppm=56, ground="zero",
                         culms=(26, 42), height=(8.0, 12.0), spread=0.8, culm="Bamboo001A", leaves="LeafSet013")


# Soldiers, baked the same way (interim, until the Mixamo-driven 3D men of
# PLAN §12.3): the earlier build's rigged MPFB2 soldiers (our own work on a
# CC0 base; SourceArt/soldiers), posed per frame and frozen.
SOLDIER_FRAMES = ["stand", "kneel", "prone", "dead0", "dead1", "stand_aim", "kneel_aim", "prone_aim"]
SOLDIER_OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Soldiers")
# fatigue_to: the old palette's 0.118 was checked under the three.js build's
# dimmer light rig; in this scene's measured sun and sky it read pale beside
# grass of albedo 0.064, so 0.085.
# Motion: CMU Graphics Lab motion capture (free for any use; the raw data may
# not be resold), in B. Hahne's BVH conversion, retargeted onto our rig's legs,
# hips and spine; the arms keep the rifle. One gait cycle per clip, from the
# steady middle of the take; "stride" is measured, so the game steps frames by
# distance and the feet do not skate.
MOCAP = [
    dict(name="walk", file="16_15.bvh", frames=16, cycle=True),     # walk
    # CMU's jogs are short takes (1.1-1.6 s against a 0.7 s cycle): the first
    # of these that holds a whole cycle is used.
    dict(name="run", file=["16_36.bvh", "16_35.bvh", "02_03.bvh", "35_17.bvh", "35_22.bvh"], frames=12, cycle=True),
    # The performer hunches far forward; with a rifle in the hands that aims
    # it at the ground, so the torso keeps 45% of his lean and the legs all of it.
    dict(name="crouch", file="136_09.bvh", frames=12, cycle=True, torso=0.45),  # walk crouched
    # The stillest 3 s of the take (least foot travel): a man waiting, not stepping.
    dict(name="idle", file="137_28.bvh", frames=8, cycle=False, window=3.0, torso=0.6),   # normal wait
]
RECIPES["soldier_us"] = dict(build="soldier", src="soldiers/us_rifleman.glb", ppm=280, ground="min", out=SOLDIER_OUT,
                             fatigue_to=0.085)
RECIPES["soldier_vc"] = dict(build="soldier", src="soldiers/vc_guerrilla.glb", ppm=280, ground="min", out=SOLDIER_OUT)


# (Sandbag walls are meshes now: tools/blender/sandbag_mesh.py.)


# The firebase's structures, built: the watchtower and the vehicle park.
# Timber is Poly Haven weathered_planks, the roof corrugated_iron_02, the
# bags and the truck canvas ambientCG Fabric066 (all CC0); paint and rubber
# are flat materials at measured albedos.
RECIPES["firebase"] = dict(build="firebase", ppm=72, ground="min", out=os.path.join(ROOT, "Assets", "_Project", "Art", "Props"),
                           timber="polyhaven/weathered_planks/weathered_planks",
                           roof="polyhaven/corrugated_iron_02/corrugated_iron_02", canvas="Fabric066")


def import_sources(recipe):
    if recipe.get("build") == "firebase":
        return build_firebase(recipe)
    if recipe.get("build") == "soldier":
        return build_soldier(recipe)
    if recipe.get("build") == "grass":
        return build_grass(recipe)
    if recipe.get("build") == "palm":
        return build_palm(recipe)
    if recipe.get("build") == "bamboo":
        return build_bamboo(recipe)
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
            # Blades as scanned are ribbon-broad for a field grass; the width
            # factor narrows them so a clump reads as grass, not a rosette.
            width = length / max(rect["aspect"], 4) * rng.uniform(0.9, 1.3) * recipe.get("width", 1.0)
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


def bark_material(stem, name="bark"):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    col = nt.nodes.new("ShaderNodeTexImage"); col.image = bpy.data.images.load(os.path.join(SRC, f"{stem}_diff_2k.png"))
    nm = nt.nodes.new("ShaderNodeTexImage"); nm.image = bpy.data.images.load(os.path.join(SRC, f"{stem}_nor_gl_2k.png"))
    nm.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.85
    return mat


def flat_material(name, rgb, rough=0.6):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    return mat


def dry_blade_material(atlas):
    """The blade atlas, browned: an old frond hanging dead against the trunk."""
    name = f"blade {atlas} dry"
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = blade_material(atlas).copy()
    mat.name = name
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    col = [n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image.colorspace_settings.name != "Non-Color"][0]
    # Keep the scan's light and dark, lose its green: nearly grey, then
    # tinted the grey-brown of a coconut frond that has died on the tree.
    hsv = nt.nodes.new("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 0.12
    hsv.inputs["Value"].default_value = 1.5
    tint = nt.nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs["Factor"].default_value = 1.0
    tint.inputs[7].default_value = (1.55, 1.12, 0.62, 1)
    nt.links.new(col.outputs["Color"], hsv.inputs["Color"])
    nt.links.new(hsv.outputs["Color"], tint.inputs[6])
    nt.links.new(tint.outputs[2], bsdf.inputs["Base Color"])
    return mat


def tinted(base, name, hue=0.5, sat=1.0, val=1.0):
    """A copy of a material with its colour texture run through hue, saturation
    and value: a scan's colour corrected toward what the plant should be."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = base.copy()
    mat.name = name
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    col = [n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image.colorspace_settings.name != "Non-Color"][0]
    hsv = nt.nodes.new("ShaderNodeHueSaturation")
    hsv.inputs["Hue"].default_value = hue
    hsv.inputs["Saturation"].default_value = sat
    hsv.inputs["Value"].default_value = val
    nt.links.new(col.outputs["Color"], hsv.inputs["Color"])
    nt.links.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def tube(name, pts, radii, ring=12, uv_scale=(1.0, 1.0)):
    """A tube through points with a radius at each; u around, v along (metres / uv_scale)."""
    verts, faces, uvs = [], [], []
    n = len(pts)
    along = 0.0
    frames = []
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        side = t.cross(mathutils.Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = mathutils.Vector((1, 0, 0))
        side.normalize()
        up = side.cross(t).normalized()
        frames.append((side, up))
    vs = []
    for i in range(n):
        if i:
            along += (pts[i] - pts[i - 1]).length
        side, up = frames[i]
        for k in range(ring + 1):
            a = 2 * math.pi * k / ring
            verts.append(pts[i] + (side * math.cos(a) + up * math.sin(a)) * radii[i])
            vs.append((k / ring * 2 * math.pi * radii[i] / uv_scale[0], along / uv_scale[1]))
    for i in range(n - 1):
        for k in range(ring):
            a = i * (ring + 1) + k
            faces.append((a, a + 1, a + ring + 2, a + ring + 1))
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    uvl = me.uv_layers.new(name="UVMap")
    for f in me.polygons:
        for li in f.loop_indices:
            uvl.data[li].uv = vs[me.loops[li].vertex_index]
    return me


def link(me, mat):
    me.materials.append(mat)
    o = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def build_palm(recipe):
    """Coconut palms. The trunk leans and curves, as coconut palms do, swollen
    at the foot and ringed by its leaf scars (the bark scan); the crown is
    fronds arching up and out and drooping at the ends, each a rachis with
    leaflets hanging from both sides in a V; under the crown, a few dead
    fronds and a bunch of nuts."""
    rng = np.random.default_rng(1965)
    blades = [r for r in blade_rects(recipe["blades"]) if r["aspect"] > 5]
    bark = bark_material(recipe["bark"])
    leaf = blade_material(recipe["blades"])
    dry = dry_blade_material(recipe["blades"])
    rachis_mat = flat_material("rachis", (0.30, 0.27, 0.12), 0.5)
    nut_mat = flat_material("nut", (0.16, 0.13, 0.05), 0.45)
    groups = {}
    for vi in range(recipe["variants"]):
        key = "abcdefghij"[vi]
        ox = vi * 40.0
        objs = []
        H = rng.uniform(*recipe["height"])
        # Lean mostly across the view, so the curve reads from the camera.
        lean_az = rng.choice([0.0, math.pi]) + rng.uniform(-0.6, 0.6)
        lean = rng.uniform(0.08, 0.28)
        curve = rng.uniform(-0.12, 0.15)
        pts, radii = [], []
        segs = 28
        for i in range(segs + 1):
            t = i / segs
            off = (lean * t + curve * t * t) * H
            pts.append(mathutils.Vector((ox + math.cos(lean_az) * off, math.sin(lean_az) * off * 0.5, t * H)))
            radii.append(0.17 + 0.16 * math.exp(-t * 14) + 0.02 * (1 - t))
        objs.append(link(tube(f"trunk {key}", pts, radii, ring=14, uv_scale=(1.3, 1.3)), bark))
        top = pts[-1]
        axis = (pts[-1] - pts[-3]).normalized()

        # The crown.
        nf = int(rng.integers(*recipe["fronds"]))
        for f in range(nf + int(rng.integers(*recipe["dead"]))):
            dead = f >= nf
            az = rng.uniform(0, 2 * math.pi) if dead else (f * 2.39996 + rng.uniform(-0.2, 0.2))   # golden angle
            young = (not dead) and rng.uniform() < 0.18
            L = rng.uniform(*recipe["frond_length"]) * (0.7 if young else 1.0)
            rise = (1.25 if young else rng.uniform(0.35, 1.0)) if not dead else -1.2
            droop = 0.4 if young else rng.uniform(1.2, 2.2)
            d = mathutils.Vector((math.cos(az), math.sin(az), 0))
            rp = [top.copy()]
            p = top.copy()
            n_r = 16
            for i in range(n_r):
                t = (i + 0.5) / n_r
                ang = rise - droop * t * t                     # elevation of the rachis, falling along it
                p = p + (d * math.cos(ang) + mathutils.Vector((0, 0, 1)) * math.sin(ang)) * (L / n_r)
                rp.append(p.copy())
            objs.append(link(tube(f"rachis {key}{f}", rp, [0.035 * (1 - i / (n_r + 1)) + 0.008 for i in range(n_r + 1)], ring=5),
                             rachis_mat))
            # Leaflets: along the outer 85%, both sides, hanging in a V.
            nl = int(rng.integers(*recipe["leaflets"]))
            for j in range(nl):
                t = 0.15 + 0.85 * (j + rng.uniform()) / nl
                i = min(int(t * n_r), n_r - 1)
                base = rp[i].lerp(rp[i + 1], t * n_r - i)
                tang = (rp[i + 1] - rp[i]).normalized()
                side = tang.cross(mathutils.Vector((0, 0, 1)))
                if side.length < 1e-3:
                    side = mathutils.Vector((1, 0, 0))
                side.normalize()
                for sgn in (-1, 1):
                    length = (0.25 + 0.85 * math.sin(math.pi * min(1, t * 1.05))) * rng.uniform(0.85, 1.1)
                    # Out to the side and forward along the rachis, hanging down.
                    out = (side * sgn * 0.75 + tang * 0.55)
                    out.z -= 0.55 if not young else 0.15
                    out.normalize()
                    az_l = math.atan2(out.y, out.x)
                    lean_l = math.acos(max(-1, min(1, out.z)))          # from vertical
                    rect = blades[int(rng.integers(len(blades)))]
                    me = blade_mesh(f"leaflet {key}{f}_{j}{sgn}", rect, length, length / max(rect["aspect"], 8) * 1.2,
                                    lean_l, rng.uniform(0.1, 0.5), az_l, tuple(base), segments=4,
                                    twist=rng.uniform(-0.5, 0.5))
                    objs.append(link(me, dry if dead else leaf))
        # Nuts, in a bunch under the crown.
        for k in range(int(rng.integers(4, 11))):
            a = rng.uniform(0, 2 * math.pi)
            c = top + mathutils.Vector((math.cos(a) * 0.32, math.sin(a) * 0.32, -0.35 - rng.uniform(0, 0.3)))
            bpy.ops.mesh.primitive_uv_sphere_add(radius=rng.uniform(0.11, 0.14), location=c, segments=10, ring_count=6)
            nut = bpy.context.active_object
            nut.data.materials.append(nut_mat)
            objs.append(nut)
        bpy.context.view_layer.update()
        groups[key] = objs
        print(f"[plants] palm {key}: {H:.1f} m, {nf} fronds, {len(objs)} parts", flush=True)
    return groups


def culm_material(atlas):
    """The scanned wall of culms, one culm's lit middle wrapped round a tube:
    the scan's own nodes (about 32 cm apart) ring it at the right spacing."""
    name = f"culm {atlas}"
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    d = os.path.join(SRC, GRASS_SRC, atlas)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    col = nt.nodes.new("ShaderNodeTexImage"); col.image = bpy.data.images.load(os.path.join(d, f"{atlas}_2K-PNG_Color.png"))
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.4
    return mat


def build_bamboo(recipe):
    """Clumping bamboo (Bambusa, the kind that walls a Vietnamese village):
    culms rising from a clump a metre or so across, leaning out and arching
    over at the top under their leaves; from the nodes of the upper part,
    short branches carrying fans of drooping leaves."""
    rng = np.random.default_rng(1968)
    leaves = blade_rects(recipe["leaves"])
    for r in leaves:
        r["base"] = "bottom" if r["vertical"] else "left"         # the scan's leaves stand tip up
    leaves = [r for r in leaves if r["aspect"] > 3]
    # LeafSet013's leaves and the culm scan are both yellowish (albedo 0.25
    # baked, where every other plant here is 0.07-0.12): darker, and toward
    # the fresh green of a living clump.
    culm_mat = tinted(culm_material(recipe["culm"]), "culm tinted", hue=0.53, sat=0.9, val=0.55)
    leaf_mat = tinted(blade_material(recipe["leaves"]), "bamboo leaf", hue=0.54, sat=1.0, val=0.45)
    twig_mat = flat_material("twig", (0.22, 0.24, 0.08), 0.5)
    groups = {}
    for vi in range(recipe["variants"]):
        key = "abcdefghij"[vi]
        ox = vi * 40.0
        objs = []
        n = int(rng.integers(*recipe["culms"]))
        H0 = rng.uniform(*recipe["height"])
        for c in range(n):
            r = recipe["spread"] * math.sqrt(rng.uniform())
            az = rng.uniform(0, 2 * math.pi)
            base = mathutils.Vector((ox + r * math.cos(az), r * math.sin(az), 0))
            out = mathutils.Vector((math.cos(az), math.sin(az), 0))
            H = H0 * rng.uniform(0.6, 1.1)
            lean0 = rng.uniform(0.03, 0.12) + 0.25 * r / recipe["spread"]
            bend = rng.uniform(0.35, 1.3)
            segs = 22
            pts, radii = [base.copy()], []
            p = base.copy()
            for i in range(segs):
                t = (i + 0.5) / segs
                ang = lean0 + bend * t ** 2.2
                p = p + (out * math.sin(ang) + mathutils.Vector((0, 0, 1)) * math.cos(ang)) * (H / segs)
                pts.append(p.copy())
            rad = rng.uniform(0.035, 0.055)
            radii = [rad * (1 - 0.6 * (i / segs)) for i in range(segs + 1)]
            # One culm's lit middle of the scan: u in a narrow strip, v along.
            u0 = (int(rng.integers(0, 14)) + 0.35) / 16.0
            me = tube(f"culm {key}{c}", pts, radii, ring=7, uv_scale=(1.0, 1.3))
            uvl = me.uv_layers[0]
            for loop in me.loops:
                uv = uvl.data[loop.index].uv
                uvl.data[loop.index].uv = (u0 + (uv[0] % 1.0) * 0.3 / 16.0 * 6, uv[1])
            objs.append(link(me, culm_mat))

            # Branches and their leaves, from the upper nodes.
            nb = int(rng.integers(24, 40))
            for b in range(nb):
                t = rng.uniform(0.3, 0.99) ** 0.7
                i = min(int(t * segs), segs - 1)
                bp = pts[i].lerp(pts[i + 1], t * segs - i)
                baz = rng.uniform(0, 2 * math.pi)
                bd = mathutils.Vector((math.cos(baz), math.sin(baz), 0)) * 0.7 + out * 0.5
                bd.z = rng.uniform(-0.1, 0.35)
                bd.normalize()
                bl = rng.uniform(0.6, 1.6) * (1.2 - 0.4 * t)
                tp = [bp, bp + bd * bl * 0.5, bp + bd * bl + mathutils.Vector((0, 0, -0.08 * bl))]
                objs.append(link(tube(f"twig {key}{c}_{b}", tp, [0.009, 0.006, 0.003], ring=4), twig_mat))
                # A fan of leaves along the twig's outer half, drooping.
                for k in range(int(rng.integers(10, 18))):
                    s_ = rng.uniform(0.35, 1.0)
                    lp = tp[1].lerp(tp[2], s_) if s_ > 0.5 else tp[0].lerp(tp[1], s_ * 2)
                    rect = leaves[int(rng.integers(len(leaves)))]
                    length = rng.uniform(0.2, 0.32)
                    laz = baz + rng.uniform(-1.3, 1.3)
                    me = blade_mesh(f"leaf {key}{c}_{b}_{k}", rect, length, length / max(rect["aspect"], 4) * 1.1,
                                    rng.uniform(1.3, 2.3), rng.uniform(0.0, 0.5), laz, tuple(lp), segments=3,
                                    twist=rng.uniform(-0.6, 0.6))
                    objs.append(link(me, leaf_mat))
        bpy.context.view_layer.update()
        groups[key] = objs
        print(f"[plants] bamboo {key}: {n} culms, {len(objs)} parts", flush=True)
    return groups


def bag_mesh(name, rng):
    """One filled sandbag: a UV sphere pressed into a pillow — flat on top
    and bottom where the weight of the course above squeezes it, bulging at
    the sides — with a little of its own sag and lumpiness."""
    L, W, H = rng.uniform(0.50, 0.58), rng.uniform(0.28, 0.33), rng.uniform(0.12, 0.15)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=1.0, calc_uvs=True)
    for v in bm.verts:
        x, y, z = v.co
        z = math.copysign(min(abs(z), 0.62) / 0.62, z) ** 1.0      # pressed flat
        bulge = 1.0 + 0.10 * (1 - z * z)
        lump = 1.0 + 0.04 * math.sin(x * 7 + y * 5 + rng.uniform(0, 6))
        v.co = mathutils.Vector((x * L / 2 * bulge * lump, y * W / 2 * bulge, z * H / 2))
        v.co.z -= 0.012 * (1 - (x * x))                            # sag in the middle
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return me, L, W, H


def fabric_material(atlas, dust):
    """A bag's cloth: the fabric scan, dulled toward the earth it was filled
    and dragged through."""
    name = f"bag {atlas}"
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    d = os.path.join(SRC, GRASS_SRC, atlas)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (3, 3, 3)
    col = nt.nodes.new("ShaderNodeTexImage"); col.image = bpy.data.images.load(os.path.join(d, f"{atlas}_2K-PNG_Color.png"))
    nm = nt.nodes.new("ShaderNodeTexImage"); nm.image = bpy.data.images.load(os.path.join(d, f"{atlas}_2K-PNG_NormalGL.png"))
    nm.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    mix.inputs[7].default_value = (*dust, 1)
    nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], col.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], nm.inputs["Vector"])
    nt.links.new(col.outputs["Color"], mix.inputs[6])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.9
    return mat


def box(name, lo, hi, mat, tile=1.0, rot=None, pivot=None):
    """An axis-aligned box from corner lo to corner hi (metres), UVs projected
    per face in metres / tile, so every texture sits at its true scale.
    rot (radians about x, y, z) turns it about pivot (default its centre)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    c = [(lo[i] + hi[i]) / 2 for i in range(3)]
    sz = [hi[i] - lo[i] for i in range(3)]
    for v in bm.verts:
        v.co = mathutils.Vector((v.co.x * sz[0] + c[0], v.co.y * sz[1] + c[1], v.co.z * sz[2] + c[2]))
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for l in f.loops:
            l[uv].uv = (l.vert.co[a] / tile, l.vert.co[b] / tile)
    if rot is not None:
        p = mathutils.Vector(pivot if pivot is not None else c)
        R = mathutils.Euler(rot).to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=mathutils.Matrix.Translation(p) @ R @ mathutils.Matrix.Translation(-p), verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(me, mat)


def strut(name, a, b, w, mat, tile=1.0):
    """A square timber from point a to point b."""
    a, b = mathutils.Vector(a), mathutils.Vector(b)
    L = (b - a).length
    o = box(name, (-w / 2, -w / 2, 0), (w / 2, w / 2, L), mat, tile)
    o.matrix_world = mathutils.Matrix.Translation(a) @ (b - a).to_track_quat("Z", "Y").to_matrix().to_4x4()
    return o


def wheel(name, centre, r, width, tyre, hub):
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=r, depth=width, location=centre, rotation=(math.pi / 2, 0, 0))
    t = bpy.context.active_object; t.name = name; t.data.materials.append(tyre)
    for p in t.data.polygons:
        p.use_smooth = True
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=r * 0.55, depth=width + 0.02, location=centre, rotation=(math.pi / 2, 0, 0))
    h = bpy.context.active_object; h.name = name + " hub"; h.data.materials.append(hub)
    return [t, h]


def build_firebase(recipe):
    rng = np.random.default_rng(1965)
    timber = bark_material(recipe["timber"], "timber")
    roof = bark_material(recipe["roof"], "roof")
    # OD canvas, weathered: the scan darkened toward the trucks' own paint.
    canvas = fabric_material(recipe["canvas"], (0.42, 0.43, 0.33))
    od = flat_material("olive drab paint", (0.075, 0.082, 0.052), 0.55)
    od_dark = flat_material("olive drab shade", (0.05, 0.055, 0.035), 0.6)
    tyre = flat_material("tyre", (0.025, 0.024, 0.022), 0.85)
    glass = flat_material("glass", (0.02, 0.025, 0.03), 0.1)
    crate = flat_material("ammo crate", (0.11, 0.105, 0.065), 0.7)
    bag_mats = [fabric_material("Fabric066", (0.72, 0.68, 0.58))]
    groups = {}

    # --- the watchtower -------------------------------------------------------------
    ox = 0.0
    objs = []
    H, deck = 10.3, 8.3
    for sx in (-1, 1):
        for sy in (-1, 1):
            objs.append(strut("leg", (ox + sx * 1.45, sy * 1.45, 0), (ox + sx * 1.2, sy * 1.2, deck), 0.24, timber))
    for sy in (-1, 1):                                    # X braces, three bays up the front, back and sides
        for k in range(3):
            z0, z1 = k * deck / 3 + 0.3, (k + 1) * deck / 3 - 0.2
            w0, w1 = 1.45 - 0.25 * z0 / deck, 1.45 - 0.25 * z1 / deck
            objs.append(strut("brace", (ox - w0, sy * w0, z0), (ox + w1, sy * w1, z1), 0.12, timber))
            objs.append(strut("brace", (ox + w0, sy * w0, z0), (ox - w1, sy * w1, z1), 0.12, timber))
            objs.append(strut("brace", (ox + sy * w0, -w0, z0), (ox + sy * w1, w1, z1), 0.12, timber))
    objs.append(box("deck", (ox - 1.9, -1.9, deck), (ox + 1.9, 1.9, deck + 0.16), timber, 1.5))
    # The cabin: bags waist-high round the deck, posts up to the roof.
    z = deck + 0.16
    for c in range(3):
        for side in range(4):
            for k in range(6):
                t = -1.6 + k * 0.62 + (0.3 if c % 2 else 0)
                if t > 1.65:
                    continue
                me, L, W, Hh = bag_mesh("tower bag", rng)
                o = link(me, bag_mats[0])
                x, y = [(t, -1.7), (1.7, t), (t, 1.7), (-1.7, t)][side]
                o.location = (ox + x, y, z + c * 0.12 + Hh / 2)
                o.rotation_euler = (0, 0, 0 if side % 2 == 0 else math.pi / 2)
                objs.append(o)
    for sx in (-1, 1):
        for sy in (-1, 1):
            objs.append(strut("post", (ox + sx * 1.75, sy * 1.75, deck), (ox + sx * 1.75, sy * 1.75, H - 0.1), 0.12, timber))
    objs.append(box("roof", (ox - 2.2, -2.2, H - 0.12), (ox + 2.2, 2.2, H - 0.07), roof, 2.0,
                    rot=(0.09, 0, 0)))
    for sy in (-1,):                                       # the ladder, up the front
        for sx in (-0.25, 0.25):
            objs.append(strut("rail", (ox + sx, sy * 1.9, 0), (ox + sx, sy * 1.55, deck), 0.07, timber))
        for k in range(int(deck / 0.35)):
            zz = 0.3 + k * 0.35
            yy = sy * (1.9 - 0.35 * zz / deck)
            objs.append(box("rung", (ox - 0.25, yy - 0.03, zz), (ox + 0.25, yy + 0.03, zz + 0.04), timber))
    groups["tower"] = objs

    # --- M35 2.5-ton trucks, side-on --------------------------------------------------
    def m35(ox, covered):
        o = []
        o.append(box("frame", (ox - 3.2, -0.45, 0.78), (ox + 3.3, 0.45, 1.02), od_dark))
        o.append(box("bumper", (ox + 3.25, -1.15, 0.7), (ox + 3.45, 1.15, 0.95), od_dark))
        o.append(box("hood", (ox + 2.05, -0.8, 1.0), (ox + 3.3, 0.8, 1.72), od))
        o.append(box("grille", (ox + 3.29, -0.62, 1.05), (ox + 3.33, 0.62, 1.65), od_dark))
        for sy in (-1, 1):
            o.append(box("fender", (ox + 1.95, sy * 1.2 - 0.25, 1.15), (ox + 3.35, sy * 1.2 + 0.25, 1.28), od))
        o.append(box("cab", (ox + 0.85, -1.05, 1.0), (ox + 2.1, 1.05, 2.05), od))
        o.append(box("window", (ox + 1.15, -1.06, 1.55), (ox + 1.95, 1.06, 1.95), glass))
        o.append(box("windscreen", (ox + 2.08, -0.95, 1.72), (ox + 2.12, 0.95, 2.35), glass, rot=(0, -0.12, 0), pivot=(ox + 2.1, 0, 1.72)))
        o.append(box("cab top", (ox + 0.85, -1.08, 2.05), (ox + 2.15, 1.08, 2.25), canvas, 1.0))
        o.append(box("bed", (ox - 3.3, -1.2, 1.02), (ox + 0.8, 1.2, 1.18), od_dark))
        for sy in (-1, 1):
            o.append(box("bed side", (ox - 3.3, sy * 1.18 - 0.04, 1.18), (ox + 0.8, sy * 1.18 + 0.04, 1.75), od))
        o.append(box("tailgate", (ox - 3.32, -1.2, 1.18), (ox - 3.25, 1.2, 1.75), od))
        if covered:
            bm = bmesh.new()                                     # the canvas: a half-tube over the bows
            segs = 14
            verts = []
            for i in range(segs + 1):
                a = math.pi * i / segs
                for x in (ox - 3.3, ox + 0.8):
                    verts.append(bm.verts.new((x, -1.22 * math.cos(a), 1.75 + 0.95 * math.sin(a))))
            for i in range(segs):
                bm.faces.new((verts[2 * i], verts[2 * i + 1], verts[2 * i + 3], verts[2 * i + 2]))
            uvl = bm.loops.layers.uv.new("UVMap")
            for f in bm.faces:
                for l in f.loops:
                    l[uvl].uv = (l.vert.co.x / 1.2, (l.vert.co.y + l.vert.co.z) / 1.2)
            me = bpy.data.meshes.new("canvas")
            bm.to_mesh(me); bm.free()
            for p in me.polygons:
                p.use_smooth = True
            o.append(link(me, canvas))
            o.append(box("canvas end", (ox - 3.31, -1.2, 1.75), (ox - 3.27, 1.2, 2.55), canvas))
        else:
            for k in range(5):                                     # bows bare, and a load of crates
                x = ox - 3.1 + k * 0.95
                o.append(box("bow", (x, -1.2, 2.62), (x + 0.05, 1.2, 2.68), od_dark))
                for sy in (-1, 1):
                    o.append(box("bow leg", (x, sy * 1.18 - 0.03, 1.75), (x + 0.05, sy * 1.18 + 0.03, 2.65), od_dark))
            for k in range(7):
                cx = ox - 3.0 + (k % 4) * 0.9 + rng.uniform(-0.05, 0.05)
                cz = 1.18 + (k // 4) * 0.42
                o.append(box("crate", (cx, -0.9 + rng.uniform(-0.1, 0.1), cz), (cx + 0.8, 0.9, cz + 0.4), crate,
                             rot=(0, 0, rng.uniform(-0.06, 0.06))))
        for (x, dual) in ((ox + 2.55, False), (ox - 1.35, True), (ox - 2.55, True)):
            for sy in (-1, 1):
                o += wheel("wheel", (x, sy * 0.95, 0.53), 0.53, 0.32, tyre, od_dark)
                if dual:
                    o += wheel("wheel", (x, sy * 0.62, 0.53), 0.53, 0.3, tyre, od_dark)
        return o
    groups["m35_covered"] = m35(30.0, True)
    groups["m35_open"] = m35(45.0, False)

    # --- M151 jeep -----------------------------------------------------------------------
    ox = 60.0
    o = []
    o.append(box("tub", (ox - 1.65, -0.8, 0.45), (ox + 1.1, 0.8, 1.0), od))
    o.append(box("hood", (ox + 1.1, -0.72, 0.5), (ox + 1.75, 0.72, 0.95), od, rot=(0, 0.08, 0), pivot=(ox + 1.1, 0, 0.95)))
    o.append(box("grille", (ox + 1.74, -0.6, 0.5), (ox + 1.78, 0.6, 0.9), od_dark))
    o.append(box("windscreen frame", (ox + 0.95, -0.78, 1.0), (ox + 1.0, 0.78, 1.45), od_dark, rot=(0, -0.2, 0), pivot=(ox + 0.97, 0, 1.0)))
    o.append(box("windscreen", (ox + 0.96, -0.7, 1.05), (ox + 0.99, 0.7, 1.4), glass, rot=(0, -0.2, 0), pivot=(ox + 0.97, 0, 1.0)))
    for sy in (-1, 1):
        o.append(box("seat", (ox - 0.2, sy * 0.4 - 0.25, 0.9), (ox + 0.35, sy * 0.4 + 0.25, 1.35), canvas))
    o.append(box("spare", (ox - 1.72, -0.35, 0.6), (ox - 1.65, 0.35, 1.3), tyre))
    o.append(strut("antenna", (ox - 1.5, 0.7, 1.0), (ox - 1.6, 0.7, 3.4), 0.015, od_dark))
    for x in (ox + 1.2, ox - 1.1):
        for sy in (-1, 1):
            o += wheel("wheel", (x, sy * 0.72, 0.37), 0.37, 0.24, tyre, od_dark)
    groups["jeep"] = o

    bpy.context.view_layer.update()
    print(f"[plants] firebase: {', '.join(groups)}", flush=True)
    return groups


def build_soldier(recipe):
    """One frozen copy of the soldier per frame in SOLDIER_FRAMES.

    He is turned to face the camera's right and a third of the way toward it
    (a pure side view hides the face and the kit), and posed by rotating bones
    about his own axes — pitch about his side-to-side axis — through each
    bone's head, parents first. That needs nothing from the bones' local axes,
    which the glTF importer re-orients. His bind pose already holds the rifle
    up, so standing, walking and kneeling leave the arms alone."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, recipe["src"]))
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == "ARMATURE"][0]
    # Only what is skinned to the rig is the soldier; the old file also
    # carries a helper sphere.
    meshes = [o for o in new if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers)]
    for o in new:
        if o.type == "MESH" and o not in meshes:
            o.hide_render = True
            print(f"[plants] soldier: leaving out {o.name} ({len(o.data.vertices)} vertices, not skinned)", flush=True)
    top = arm
    while top.parent is not None:                      # the soldier's own root, not the helper
        top = top.parent
    # The glTF importer leaves objects in quaternion mode, where Euler angles are ignored.
    print(f"[plants] soldier root {top.name}: rotation mode {top.rotation_mode}", flush=True)
    top.rotation_mode = "XYZ"
    top.rotation_euler = (0, 0, math.radians(60))       # front (-Y) turned to face right, 30 deg toward the lens
    bpy.context.view_layer.update()
    F = mathutils.Vector((math.sin(math.radians(60)), -math.cos(math.radians(60)), 0))
    U = mathutils.Vector((0, 0, 1))
    P = F.cross(U).normalized()                          # + pitch swings a hanging bone's tail forward

    def rot(bone, axis, angle):
        pb = arm.pose.bones.get(bone)
        if pb is None or angle == 0:
            return
        bpy.context.view_layer.update()
        M = arm.matrix_world.to_3x3()
        a = (M.inverted() @ axis).normalized()
        h = pb.head.copy()
        pb.matrix = mathutils.Matrix.Translation(h) @ mathutils.Matrix.Rotation(angle, 4, a) @ mathutils.Matrix.Translation(-h) @ pb.matrix
        bpy.context.view_layer.update()

    def reset():
        for pb in arm.pose.bones:
            pb.matrix_basis = mathutils.Matrix.Identity(4)
        bpy.context.view_layer.update()

    def head(bone):
        bpy.context.view_layer.update()
        return arm.matrix_world @ arm.pose.bones[bone].head

    def align(bone, v_from, v_to):
        """Turn a bone about its head so world direction v_from becomes v_to."""
        a, b = v_from.normalized(), v_to.normalized()
        ax = a.cross(b)
        if ax.length < 1e-6:
            return
        rot(bone, ax.normalized(), a.angle(b))

    def two_bone(upper, lower, hand, target, pole):
        """Analytic two-bone IK: put the hand's head on target, the elbow toward pole."""
        S, E, W = head(upper), head(lower), head(hand)
        la, lb = (E - S).length, (W - E).length
        d = min((target - S).length, (la + lb) * 0.999)
        axis = (target - S).normalized()
        # Elbow: along the axis by the law of cosines, then out toward the pole.
        x = (la * la - lb * lb + d * d) / (2 * d)
        h = math.sqrt(max(la * la - x * x, 0.0))
        pdir = (pole - axis * pole.dot(axis)).normalized()
        E2 = S + axis * x + pdir * h
        align(upper, E - S, E2 - S)
        E, W = head(lower), head(hand)
        align(lower, W - E, (S + axis * d) - E)
        miss = (head(hand) - target).length
        if miss > 0.03:
            print(f"[plants] ik {upper}: hand {miss * 100:.0f} cm from its target (reach {la + lb:.2f} m, asked {(target - S).length:.2f} m)", flush=True)

    # The rifle's axis in the right hand's own frame, measured once from the
    # bind pose's weapon vertices (everything skinned to hand_r is the rifle).
    gun_axis_local = None
    body = meshes[0]
    hr = body.vertex_groups.get("hand_r")
    if hr is not None:
        pts = [body.matrix_world @ v.co for v in body.data.vertices
               if any(g.group == hr.index and g.weight > 0.9 for g in v.groups)]
        if len(pts) > 50:
            P0 = np.array([tuple(p) for p in pts])
            c = P0.mean(0)
            _, _, vt = np.linalg.svd(P0 - c, full_matrices=False)
            ax = mathutils.Vector(vt[0])
            wrist = head("hand_r")
            # The muzzle is the end further from the wrist.
            far = max(pts, key=lambda p: (p - wrist).length)
            if ax.dot(far - wrist) < 0:
                ax = -ax
            Mh = (arm.matrix_world @ arm.pose.bones["hand_r"].matrix).to_3x3()
            gun_axis_local = Mh.inverted() @ ax

    def lie_prone():
        """Face down, head toward the enemy. A +angle about P swings a hanging
        bone's tail forward (P = F x U), so the body tips forward onto its
        chest by -angle: the spine goes forward, the legs back. The first
        prone frame used +angle and sat the man up with his legs out."""
        rot("pelvis", P, -1.5)
        # Up on the elbows: chest, neck and head lifted to look ahead.
        rot("spine_02", P, 0.15); rot("spine_03", P, 0.2)
        rot("neck_01", P, 0.35); rot("head", P, 0.45)
        rot("thigh_l", F, -0.12); rot("thigh_r", F, 0.12)           # legs a little apart

    def shoulder_rifle(pitch=0.0):
        """Rifle to the shoulder, aimed along the facing, slightly down (at a man, not the sky)."""
        if gun_axis_local is None:
            return
        S = head("upperarm_r")
        aim = (F * math.cos(pitch) - U * math.sin(pitch)).normalized()
        grip = S + aim * 0.30 - U * 0.06 + P * 0.04
        # The pole was chosen by looking, not derived: with this rig and this
        # IK, a pole down and toward the chest raises the firing elbow and
        # brings the stock to the cheek; "down and out", which the textbook
        # says, left the rifle at the chest.
        two_bone("upperarm_r", "lowerarm_r", "hand_r", grip, -U * 0.8 - P * 0.6)
        bpy.context.view_layer.update()
        Mh = (arm.matrix_world @ arm.pose.bones["hand_r"].matrix).to_3x3()
        align("hand_r", Mh @ gun_axis_local, aim)
        # The support hand under the handguard, a forearm's length ahead.
        # A forearm ahead of the firing hand is beyond this arm's 0.48 m reach
        # (measured: 24 cm short, and the arm strained off toward the neck);
        # 0.2 m ahead, under the handguard, it holds.
        grip2 = head("hand_r") + aim * 0.20 - U * 0.04 - P * 0.06
        two_bone("upperarm_l", "lowerarm_l", "hand_l", grip2, -U * 0.8 + P * 0.5)
        rot("head", P, 0.12)

    def pose(name):
        reset()
        if name.startswith("walk"):
            ph = int(name[4:]) / 6 * 2 * math.pi
            s_ = math.sin(ph)
            rot("spine_01", P, 0.06)
            rot("thigh_l", P, 0.42 * s_)
            rot("thigh_r", P, -0.42 * s_)
            rot("calf_l", P, -1.05 * max(0.0, -s_))
            rot("calf_r", P, -1.05 * max(0.0, s_))
        elif name == "kneel":
            rot("spine_01", P, 0.14)
            rot("thigh_r", P, 1.45); rot("calf_r", P, -1.45)
            rot("thigh_l", P, 0.12); rot("calf_l", P, -1.72); rot("foot_l", P, 0.6)
        elif name == "prone":
            lie_prone()
            rot("upperarm_r", P, 0.9); rot("upperarm_l", P, 0.9)       # arms forward, rifle across in front
        elif name == "stand_aim":
            rot("spine_01", P, 0.05)
            shoulder_rifle(0.04)
        elif name == "kneel_aim":
            rot("spine_01", P, 0.14)
            rot("thigh_r", P, 1.45); rot("calf_r", P, -1.45)
            rot("thigh_l", P, 0.12); rot("calf_l", P, -1.72); rot("foot_l", P, 0.6)
            shoulder_rifle(0.03)
        elif name == "prone_aim":
            lie_prone()
            shoulder_rifle(0.0)
        elif name == "dead0":                           # thrown on his back (+ about P: see lie_prone)
            rot("pelvis", P, 1.55)
            rot("thigh_l", F, 0.35); rot("thigh_r", F, -0.2); rot("calf_r", P, -0.5)
            rot("upperarm_l", P, 1.1); rot("upperarm_r", F, 0.6)
            rot("head", U, 0.9)
        elif name == "dead1":                           # face down, where he fell
            rot("pelvis", P, -1.52)
            rot("spine_03", P, 0.15); rot("head", F, 0.5)
            rot("upperarm_l", F, -1.0); rot("upperarm_r", F, 1.0)
            rot("thigh_l", F, 0.25); rot("thigh_r", F, -0.3); rot("calf_l", P, -0.6)

    # --- motion capture -----------------------------------------------------------
    BONES = [("pelvis", "Hips"), ("spine_01", "LowerBack"), ("spine_02", "Spine"), ("spine_03", "Spine1"),
             ("neck_01", "Neck"), ("head", "Head"),
             ("thigh_l", "LeftUpLeg"), ("calf_l", "LeftLeg"), ("foot_l", "LeftFoot"), ("ball_l", "LeftToeBase"),
             ("thigh_r", "RightUpLeg"), ("calf_r", "RightLeg"), ("foot_r", "RightFoot"), ("ball_r", "RightToeBase")]
    reset()
    tb = {b: (arm.matrix_world @ arm.pose.bones[b].matrix).to_quaternion() for b, _ in BONES}
    t_pelvis = arm.matrix_world @ arm.pose.bones["pelvis"].head
    t_foot = min(head("foot_l").z, head("foot_r").z)
    t_hip_h = t_pelvis.z - t_foot

    def yaw_to(a, b):
        """The rotation about U that turns horizontal direction a into b."""
        a = mathutils.Vector((a.x, a.y, 0)).normalized(); b = mathutils.Vector((b.x, b.y, 0)).normalized()
        return mathutils.Quaternion(U, math.atan2(a.cross(b).z, a.dot(b)))

    def load_clip(spec):
        path = os.path.join(SRC, "mocap", "cmu", spec["file"])
        before = set(bpy.data.objects)
        bpy.ops.import_anim.bvh(filepath=path, axis_forward="-Z", axis_up="Y", update_scene_fps=False,
                                use_fps_scale=False, frame_start=1)
        src = [o for o in bpy.data.objects if o not in before][0]
        sc = bpy.context.scene
        n = int(src.animation_data.action.frame_range[1])

        def world(fr):
            sc.frame_set(int(fr), subframe=fr - int(fr))
            bpy.context.view_layer.update()
            M = src.matrix_world
            return {b: (M @ src.pose.bones[b].matrix) for b in ["Hips", "LowerBack", "Spine", "Spine1", "Neck", "Head",
                    "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"]}

        T = world(1)                                     # Hahne's conversion: frame 1 is a T-pose
        rq = {b: T[b].to_quaternion() for b in T}
        right = T["RightUpLeg"].translation - T["LeftUpLeg"].translation
        Fs = U.cross(mathutils.Vector((right.x, right.y, 0))).normalized()   # facing, from the hips
        s_hip = T["Hips"].translation.z - min(T["LeftFoot"].translation.z, T["RightFoot"].translation.z)
        scale = t_hip_h / s_hip
        Y0 = yaw_to(Fs, F)

        def heading(W):
            d = (W["Hips"].to_quaternion() @ rq["Hips"].inverted()) @ Fs
            return mathutils.Vector((d.x, d.y, 0)).normalized()

        # The cycle: left foot furthest ahead of the hips, twice, mid-take.
        if spec["cycle"]:
            lo, hi = (2, n - 1) if spec.get("short") else (max(2, int(n * 0.1)), int(n * 0.95))
            ahead = []
            for fr in range(lo, hi):
                W = world(fr)
                ahead.append((W["LeftFoot"].translation - W["Hips"].translation).dot(heading(W)))
            peaks = [lo + i for i in range(1, len(ahead) - 1) if ahead[i] >= ahead[i - 1] and ahead[i] > ahead[i + 1]
                     and ahead[i] > 0.5 * max(ahead)]
            if len(peaks) < 2:
                raise RuntimeError(f"{spec['file']}: no gait cycle found")
            a, b = peaks[len(peaks) // 2 - 1], peaks[len(peaks) // 2]
            Wa, Wb = world(a), world(b)
            travel = Wb["Hips"].translation - Wa["Hips"].translation
            stride = mathutils.Vector((travel.x, travel.y, 0)).length * scale
            span = [a + (b - a) * k / spec["frames"] for k in range(spec["frames"])]
        else:
            fps = 120.0
            win = int(spec["window"] * fps)
            # Foot positions every 10 frames; the window where they travel least.
            samples = []
            for fr in range(2, n, 10):
                W = world(fr)
                samples.append((fr, W["LeftFoot"].translation.copy(), W["RightFoot"].translation.copy(), heading(W)))
            best, a = None, 2
            for i in range(len(samples)):
                j = i + win // 10
                if j >= len(samples):
                    break
                # Feet that stay put, and hips that stay facing the same way: a
                # man turning to look at the camera is not waiting.
                travel = sum((samples[k + 1][1] - samples[k][1]).length + (samples[k + 1][2] - samples[k][2]).length
                             for k in range(i, j))
                turn = max(samples[i][3].angle(samples[k][3]) for k in range(i, j + 1))
                travel += turn * (s_hip * 2.0)
                if best is None or travel < best:
                    best, a = travel, samples[i][0]
            b = a + win
            span = [a + (b - a) * k / (spec["frames"] - 1) for k in range(spec["frames"])]
            stride = 0.0
        H = mathutils.Vector((0, 0, 0))
        for fr in span:
            H += heading(world(fr))
        Yc = yaw_to(Y0 @ H.normalized(), F)
        z0 = sum(world(fr)["Hips"].translation.z for fr in span) / len(span)
        poses = []
        for fr in span:
            W = world(fr)
            rots = {}
            for tb_name, sb in BONES:
                delta = W[sb].to_quaternion() @ rq[sb].inverted()
                r = Yc @ (Y0 @ delta @ Y0.inverted()) @ tb[tb_name]
                if tb_name in ("pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head") and spec.get("torso", 1) < 1:
                    r = tb[tb_name].slerp(r, spec["torso"])
                rots[tb_name] = r
            poses.append((rots, (W["Hips"].translation.z - z0) * scale))
        bpy.data.objects.remove(src, do_unlink=True)
        return poses, stride, (b - a) / 120.0

    def apply_mocap(rots, bob):
        reset()
        Minv = arm.matrix_world.inverted()
        Rinv = arm.matrix_world.to_quaternion().inverted()
        for b, _ in BONES:
            pb = arm.pose.bones[b]
            bpy.context.view_layer.update()
            h = pb.head.copy()
            if b == "pelvis":
                h = Minv @ (t_pelvis + U * bob)
            pb.matrix = mathutils.Matrix.LocRotScale(h, Rinv @ rots[b], None)
        bpy.context.view_layer.update()

    frames = [(n_, (lambda n_=n_: pose(n_))) for n_ in SOLDIER_FRAMES]
    clips_meta = []
    for spec in MOCAP:
        files = spec["file"] if isinstance(spec["file"], list) else [spec["file"]]
        for fi_, fname in enumerate(files):
            try:
                poses, stride, seconds = load_clip(dict(spec, file=fname, short=len(files) > 1))
                spec = dict(spec, file=fname)
                break
            except RuntimeError as e:
                print(f"[plants] mocap {spec['name']}: {e}", flush=True)
                if fi_ == len(files) - 1:
                    raise
        for k, (rots, bob) in enumerate(poses):
            frames.append((f"{spec['name']}{k:02d}", (lambda r=rots, bb=bob: apply_mocap(r, bb))))
        clips_meta.append(dict(name=spec["name"], frames=len(poses), stride_m=round(stride, 3), seconds=round(seconds, 3),
                               source=f"CMU {spec['file']}"))
        print(f"[plants] mocap {spec['name']}: {len(poses)} frames, stride {stride:.2f} m, cycle {seconds:.2f} s "
              f"({stride / seconds if seconds else 0:.2f} m/s)", flush=True)
    recipe["clips_meta"] = clips_meta

    groups = {}
    dg = bpy.context.evaluated_depsgraph_get()
    for fi, (name, do_pose) in enumerate(frames):
        do_pose()
        dg = bpy.context.evaluated_depsgraph_get()
        pelvis = arm.matrix_world @ arm.pose.bones["pelvis"].head
        snaps = []
        for o in meshes:
            ev = o.evaluated_get(dg)
            me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
            so = bpy.data.objects.new(f"{name} {o.name}", me)
            so.matrix_world = o.matrix_world.copy()
            bpy.context.scene.collection.objects.link(so)
            snaps.append(so)
        bpy.context.view_layer.update()
        zmin = min((so.matrix_world @ v.co).z for so in snaps for v in so.data.vertices)
        off = mathutils.Vector((fi * 6.0, 0, -zmin))
        for so in snaps:
            so.matrix_world = mathutils.Matrix.Translation(off) @ so.matrix_world
            so["root"] = [pelvis.x + off.x, pelvis.y + off.y]
        groups[name] = snaps
    for o in meshes:
        o.hide_render = True
    bpy.context.view_layer.update()
    print(f"[plants] soldier {recipe['src']}: {len(groups)} frames", flush=True)
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
    if "root" in objs[0]:                       # a soldier: his pelvis, so walk frames do not jitter
        root = mathutils.Vector((objs[0]["root"][0], objs[0]["root"][1], ground_z))
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


def lift_fatigue(albedo, a, target):
    """The earlier build's soldiers bake their uniform far darker than their
    own palette says: its OG-107 cotton sateen is albedo 0.118, and the
    texture it was worn with bakes the green at about 0.02 — a black suit in
    this light. Scale the uniform's green (the texels where green leads) so
    their median reaches the palette's value, keeping the fabric's pattern;
    skin, webbing, helmet and steel are left alone."""
    lum = albedo @ np.array([0.2126, 0.7152, 0.0722])
    green = (albedo[..., 1] > albedo[..., 0] * 1.08) & (albedo[..., 1] > albedo[..., 2] * 1.2) & (a > 0.5)
    if green.sum() < 50:
        return albedo
    # Re-coloured, not scaled: the dark texture's green is saturated, and
    # scaling it 15x gave lime. OG-107 is a greyed olive (the palette's
    # 0.118, 0.122, 0.078); the fabric's pattern survives as the texels'
    # brightness relative to the uniform's median.
    med = max(float(np.median(lum[green])), 1e-4)
    od = np.array([0.118, 0.122, 0.078]) * (target / 0.1206)
    pattern = np.clip(lum / med, 0.35, 2.2) ** 0.6
    recol = od[None, None, :] * pattern[..., None]
    # Soft at the uniform's edges so nothing seams where green meets webbing.
    w = np.clip((albedo[..., 1] / np.maximum(albedo[..., 0], 1e-4) - 1.0) / 0.15, 0, 1)[..., None] * green[..., None]
    out = albedo * (1 - w) + recol * w
    print(f"[plants] fatigue: green median {med:.3f} -> OG-107 olive at {target}", flush=True)
    return np.clip(out, 0, 1)


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
        if recipe.get("fatigue_to"):
            albedo = lift_fatigue(albedo, a, recipe["fatigue_to"])
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
    out = recipe.get("out", OUT)
    os.makedirs(out, exist_ok=True)
    write_png(os.path.join(out, f"{name}_albedo.png"), alb[::-1], alpha=True)
    write_png(os.path.join(out, f"{name}_normal.png"), nor[::-1], alpha=True)
    with open(os.path.join(out, f"{name}.json"), "w") as f:
        json.dump(dict(species=name, pitch_deg=BAKE_PITCH, atlas=[AW, AH], ppm=recipe["ppm"], clips=recipe.get("clips_meta", []),
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


if __name__ == "__main__":
    main()
