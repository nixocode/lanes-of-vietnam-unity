"""
Sandbag walls as geometry, weathered (the owner, three playtests running:
"sandbags need more ruggedness, too clean", "flicker", "they look too new").

    Blender -b --factory-startup --python tools/blender/sandbag_mesh.py

They were baked cards (plant_bake.py): smooth pillows under one clean cloth,
and wherever two cards met at the same depth they fought for the pixel, which
was the flicker. Here a wall is what it is: bags, each its own shape, laid in
running bond, with the weather on them.

  bags      a sack filled and thrown down: boxy where the fill packs it, the
            tied neck drawn in at one end, lumps, a sag over the gap below;
            each a little turned and out of line
  cloth     Poly Haven `hessian_230` (burlap; CC0), most bags its own jute
            tan, some dyed olive, each sun-bleached or dark by its own amount.
            The cloth's unevenness tiles over each bag as a detail map (a
            bag has ~200 texels of atlas), so the atlas carries what differs
            from bag to bag and the detail what does not
  folds     each bag's own: gathered where the neck was tied, slack creases
            across it, the seam down each side; a height field per bag,
            written as a normal map and as dirt in its hollows
  weather   baked from the wall itself: earth packed into every crease (the
            occlusion), tops bleached and dusty (what faces the sky), the
            bottom courses damp, splashed and going green (height), and
            stains across each bag

Outputs, in Assets/_Project/Art/Structures/:
    sandbags.fbx   meshes tall_a, tall_b (7 courses, ~1 m), low_a, low_b (3
                   courses), each a 2.4 m run with its origin at the middle
                   of its foot
    sandbags_albedo.png, sandbags_normal.png, sandbags_mask.png (2048)
    sandbags_weave.png (1024; the tiling detail)
    sandbags.json  each mesh's length and height, and the atlas's grid
"""
import json
import math
import os

import bmesh
import bpy
import mathutils
import numpy as np
import OpenImageIO as oiio

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt", "polyhaven", "hessian_230")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Structures")
ATLAS = 2048
RUN = 2.4
PAD = 5                                          # texels of margin round each bag's cell
SIDES = 0.5                                      # how far the unwrap favours a bag's sides (0: evenly)
PER_COURSE = 5                                   # the most bags a 2.4 m course takes
SEGMENTS = [("tall_a", 7), ("tall_b", 7), ("low_a", 3), ("low_b", 3)]
EARTH = np.array([0.150, 0.105, 0.070])          # the lanes' packed soil, for what works into the cloth
MOSS = np.array([0.050, 0.070, 0.030])


def log(msg):
    print(f"[sandbags] {msg}", flush=True)


def bag(rng, exposed):
    """One bag as a bmesh in its own space (x along it, the tied neck at +x), and its size."""
    L, W, H = rng.uniform(0.50, 0.60), rng.uniform(0.36, 0.44), rng.uniform(0.165, 0.195)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")         # the sphere's UVs are only written into a layer that already exists
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=7, radius=1.0, calc_uvs=True)
    # The sphere's poles go to the bag's ends, where the next bag hides them,
    # and its seam underneath: u then runs round the bag, v along it, and the
    # cloth is one unbroken sheet over the front, the top and the back.
    seam = next(math.atan2(l.vert.co.y, l.vert.co.x) for f in bm.faces for l in f.loops
                if l[uv].uv.x < 1e-6 and abs(l.vert.co.z) < 0.9)
    ph = rng.uniform(0, 6.28, 4)
    for v in bm.verts:
        r, a = math.hypot(v.co.x, v.co.y), math.atan2(v.co.y, v.co.x) - seam - math.pi / 2
        x, y, z = v.co.z, r * math.cos(a), r * math.sin(a)
        # A sphere pushed toward a box: the fill packs the cloth out flat.
        bx = math.copysign(abs(x) ** 0.45, x)
        by = math.copysign(abs(y) ** 0.62, y)
        bz = math.copysign(abs(z) ** 0.36, z)
        # The neck: drawn in over the last sixth, where it was tied off.
        t = max(0.0, (bx - 0.66) / 0.34)
        pinch = 1.0 - 0.5 * t * t
        lump = 1.0 + 0.07 * math.sin(3.1 * x + ph[0]) * math.sin(2.7 * y + ph[1]) + 0.04 * math.sin(5.3 * x + 4.1 * z + ph[2])
        co = mathutils.Vector((bx * L / 2, by * W / 2 * pinch * lump, bz * H / 2 * (0.75 + 0.25 * pinch) * lump))
        if exposed:
            co.z -= 0.02 * bx * bx                                   # the ends droop where nothing presses them
        v.co = co
    return bm, L, W, H


def build():
    rng = np.random.default_rng(1965)
    total = sum(n for _, n in SEGMENTS) * PER_COURSE
    cols = math.ceil(math.sqrt(total))
    rows = math.ceil(total / cols)
    cell = (1.0 / cols, 1.0 / rows)
    pad = PAD / ATLAS
    objs, index, info = [], 0, {}
    for si, (name, courses) in enumerate(SEGMENTS):
        me = bpy.data.meshes.new(name)
        out = bmesh.new()
        uv_atlas = out.loops.layers.uv.new("atlas")
        tint = out.loops.layers.color.new("tint")
        z, top = 0.0, 0.0
        for c in range(courses):
            x = -RUN / 2 + (0.0 if c % 2 == 0 else 0.26) - 0.02
            course_h = 0.0
            while x < RUN / 2 - 0.12:
                bm, L, W, H = bag(rng, exposed=c == courses - 1)
                L = min(L, RUN / 2 + 0.06 - x)                         # the run ends flush enough to butt the next
                yaw = rng.uniform(-0.09, 0.09) + (math.pi if rng.uniform() < 0.5 else 0.0)   # tied at either end
                M = (mathutils.Matrix.Translation((x + L / 2, rng.uniform(-0.035, 0.035), z + H / 2 * 0.92))
                     @ mathutils.Euler((rng.uniform(-0.06, 0.06), rng.uniform(-0.05, 0.05), yaw)).to_matrix().to_4x4())
                # Jute gone khaki in the sun, or olive drab gone grey-green; each
                # bleached or dark by its own amount (linear).
                olive = rng.uniform() < 0.38
                shade = rng.uniform(0.55, 1.15)
                col = np.clip((np.array([0.062, 0.070, 0.038]) if olive else np.array([0.128, 0.100, 0.060])) * shade, 0, 1)
                u0, v0 = (index % cols) * cell[0], (index // cols) * cell[1]
                src_uv = bm.loops.layers.uv.verify()
                vmap = {}
                for v in bm.verts:
                    vmap[v] = out.verts.new(M @ v.co)
                for f in bm.faces:
                    nf = out.faces.new([vmap[v] for v in f.verts])
                    nf.smooth = True
                    for l_src, l_dst in zip(f.loops, nf.loops):
                        u, vv = l_src[src_uv].uv
                        # The faces a wall shows are a bag's sides, an eighth of the way
                        # round it: they get the texels, its top and its underside give them up.
                        u -= SIDES / (4 * math.pi) * math.sin(4 * math.pi * u)
                        l_dst[uv_atlas].uv = (u0 + pad + u * (cell[0] - 2 * pad), v0 + pad + vv * (cell[1] - 2 * pad))
                        l_dst[tint] = (*to_srgb(col), 1.0)            # a byte colour layer is read as sRGB
                bm.free()
                index += 1
                course_h = max(course_h, H)
                x += L - 0.02
            z += course_h * 0.84                                      # each course settles into the one below
            top = z
        out.to_mesh(me)
        out.free()
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        o.location = (si * 6.0, 0, 0)
        objs.append(o)
        info[name] = dict(length=RUN, height=round(top + 0.02, 3), triangles=sum(len(p.vertices) - 2 for p in me.polygons))
        log(f"{name}: {courses} courses, {info[name]['height']:.2f} m, {info[name]['triangles']} triangles")
    return objs, info, (cols, rows, index)


def material():
    mat = bpy.data.materials.new("sandbags")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tint = nt.nodes.new("ShaderNodeVertexColor"); tint.layer_name = "tint"
    nt.links.new(tint.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.92
    return mat, tint


def bake(objs, mat, kind, img, samples=4):
    nt = mat.node_tree
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = img
    nt.nodes.active = t
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    sc = bpy.context.scene
    sc.cycles.samples = samples
    sc.render.bake.margin = 6
    sc.render.bake.use_clear = True
    if kind == "NORMAL":
        sc.render.bake.normal_space = "TANGENT"
    bpy.ops.object.bake(type=kind)
    nt.nodes.remove(t)
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[::-1].copy()


def emit(mat, socket_or_nodes):
    """Route something to an emission shader for an EMIT bake; returns the undo."""
    nt = mat.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    old = out.inputs["Surface"].links[0].from_socket
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(socket_or_nodes, em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])

    def undo():
        nt.links.new(old, out.inputs["Surface"])
        nt.nodes.remove(em)
    return undo


def to_srgb(l):
    l = np.clip(l, 0, 1)
    return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)


def write_png(path, px):
    h, w, c = px.shape
    spec = oiio.ImageSpec(w, h, c, "uint8")
    spec.attribute("oiio:UnassociatedAlpha", 1)
    out = oiio.ImageOutput.create(path)
    out.open(path, spec)
    out.write_image((np.clip(px, 0, 1) * 255 + 0.5).astype(np.uint8))
    out.close()


def blotches(rng, n):
    """Soft stains: low-frequency noise, different in every stretch of the atlas."""
    small = rng.uniform(0, 1, (n // 32 + 2, n // 32 + 2))
    big = np.kron(small, np.ones((32, 32)))[:n, :n]
    k = np.ones(48) / 48
    big = np.apply_along_axis(lambda r: np.convolve(r, k, "same"), 1, big)
    big = np.apply_along_axis(lambda r: np.convolve(r, k, "same"), 0, big)
    big = (big - big.min()) / max(big.max() - big.min(), 1e-6)
    return big


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def folds(grid, rng):
    """
    Each bag's cloth as a height field over its cell (u round the bag, the
    seam underneath; v along it, the neck at 1): the gathers at the neck,
    slack creases, the side seams. Returns the tangent-space normal map and
    the hollows (0..1), both top row first.
    """
    cols, rows, count = grid
    normal = np.zeros((ATLAS, ATLAS, 3), np.float32); normal[..., 2] = 1
    hollow = np.zeros((ATLAS, ATLAS), np.float32)
    cw, ch, tau = ATLAS / cols, ATLAS / rows, 2 * math.pi
    for i in range(count):
        x0, x1 = round((i % cols) * cw), round((i % cols + 1) * cw)
        y0, y1 = round((i // cols) * ch), round((i // cols + 1) * ch)          # rows counted up from v = 0
        u = (np.arange(x0, x1) + 0.5 - (i % cols) * cw - PAD) / (cw - 2 * PAD)
        v = (np.arange(y0, y1) + 0.5 - (i // cols) * ch - PAD) / (ch - 2 * PAD)
        U, V = np.meshgrid(u, v)
        gathers = rng.integers(8, 13)
        h = 0.8 * smooth(0.55, 0.98, V) * np.sin(tau * gathers * U + 2.2 * np.sin(tau * (2 * U + 1.3 * V) + rng.uniform(0, tau)))
        for j in range(5):
            h += 0.42 / (j + 1) * np.sin(tau * (rng.integers(1, 5) * U + rng.uniform(0.5, 3.0) * V) + rng.uniform(0, tau))
        h += 0.7 * np.exp(-((np.abs((2 * U) % 1.0 - 0.5)) / 0.014) ** 2)    # the seam down each side
        gy, gx = np.gradient(h)
        n = np.dstack([-gx * 1.6, -gy * 1.6, np.ones_like(h)])
        normal[y0:y1, x0:x1] = n / np.linalg.norm(n, axis=-1, keepdims=True)
        hollow[y0:y1, x0:x1] = np.clip(-h * 0.8, 0, 1)
    return normal[::-1] * 0.5 + 0.5, hollow[::-1].copy()


def weave():
    """
    The cloth, for the game to tile over every bag: its light and dark about
    0.5 (URP's detail albedo doubles it).

    Not its threads. A bag is 110 px long at the closest the lens comes, and
    the weave is 90 threads to a bag: a regular grid under two pixels a
    line, which beat against the pixel grid as streaks and crawled from
    frame to frame (16% of the wall's pixels moved by over 3 L*). What is
    kept is what a wall shows at that size: the cloth's unevenness, thick
    yarns and thin, as everything in the scan between 40 and 400 texels
    long (a Fourier band, so it still tiles).
    """
    px = oiio.ImageBuf(os.path.join(SRC, "hessian_230_diff_2k.jpg")).get_pixels(oiio.FLOAT)[..., :3]
    srgb = px.reshape(px.shape[0] // 2, 2, px.shape[1] // 2, 2, 3).mean((1, 3))
    lin = np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)
    lum = lin @ np.array([0.2126, 0.7152, 0.0722])
    r = np.hypot(*np.meshgrid(np.fft.fftfreq(lum.shape[1]), np.fft.fftfreq(lum.shape[0])))   # cycles a texel
    band = np.exp(-(r * 40) ** 2) * (1 - np.exp(-(r * 400) ** 2))
    grain = np.fft.ifft2(np.fft.fft2(lum) * band).real
    grey = np.clip(0.5 + 0.11 * grain / grain.std(), 0, 1)
    write_png(os.path.join(OUT, "sandbags_weave.png"), np.dstack([grey] * 3))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
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
        log(f"GPU unavailable ({e}); baking on the CPU")
    sc.world = bpy.data.worlds.new("bake")
    sc.world.light_settings.distance = 0.3

    objs, info, grid = build()
    weave()
    mat, tint = material()
    for o in objs:
        o.data.materials.append(mat)
    # The ground the walls stand on, so the bottom course is occluded by it.
    bpy.ops.mesh.primitive_plane_add(size=60, location=(9, 0, 0.0))
    ground = bpy.context.object
    ground.hide_render = False

    img = bpy.data.images.new("bake", ATLAS, ATLAS, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    nt = mat.node_tree
    undo = emit(mat, tint.outputs["Color"])
    base = bake(objs, mat, "EMIT", img)[..., :3]
    undo()
    # What faces the sky, and how high off the ground: one emission bake, two channels.
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep_n = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["Normal"], sep_n.inputs[0])
    sep_p = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["Position"], sep_p.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    half = nt.nodes.new("ShaderNodeMath"); half.operation = "MULTIPLY_ADD"; half.inputs[1].default_value = 0.5; half.inputs[2].default_value = 0.5
    nt.links.new(sep_n.outputs["Z"], half.inputs[0])
    nt.links.new(half.outputs[0], comb.inputs[0])
    nt.links.new(sep_p.outputs["Z"], comb.inputs[1])
    undo = emit(mat, comb.outputs[0])
    where = bake(objs, mat, "EMIT", img)
    undo()
    ao = bake(objs, mat, "AO", img, samples=96)[..., 0]

    up = np.clip(where[..., 0] * 2 - 1, 0, 1)
    height = where[..., 1]
    crease = np.clip((1 - ao) * 1.9 - 0.10, 0, 0.92)[..., None]
    stain = blotches(np.random.default_rng(7), ATLAS)[..., None]
    normal, hollow = folds(grid, np.random.default_rng(11))
    crease = np.clip(crease + 0.45 * hollow[..., None], 0, 0.9)             # and into the cloth's own hollows
    albedo = base * (0.86 + 0.28 * stain)                                   # no two patches of a bag quite the same
    grime = 0.30 * stain ** 2
    albedo = albedo * (1 - grime) + EARTH * grime                           # handled, rained on, dried, for a season
    albedo = albedo * (1 - crease) + EARTH * (0.45 + 0.35 * stain) * crease # earth packed into the creases
    bleach = (up ** 1.5)[..., None] * 0.38
    grey = albedo.mean(-1, keepdims=True)
    albedo = albedo * (1 - bleach) + (grey * 1.15 + np.array([0.035, 0.028, 0.018])) * bleach   # tops sun-bleached, dusty
    damp = np.clip(1 - height / 0.38, 0, 1)[..., None] * (0.35 + 0.45 * stain)
    albedo = albedo * (1 - damp) + (albedo * 0.5 + MOSS * 0.6 + EARTH * 0.25) * damp        # the foot of the wall: damp, splashed, greening
    used = base.sum(-1) > 1e-4
    lum = albedo @ np.array([0.2126, 0.7152, 0.0722])
    log(f"atlas {ATLAS}: {used.mean() * 100:.0f}% used, albedo median {np.median(lum[used]):.3f} "
        f"(cloth alone {np.median((base @ [0.2126, 0.7152, 0.0722])[used]):.3f}), occlusion mean {ao[used].mean():.2f}")
    write_png(os.path.join(OUT, "sandbags_albedo.png"), to_srgb(albedo))
    write_png(os.path.join(OUT, "sandbags_normal.png"), normal)
    write_png(os.path.join(OUT, "sandbags_mask.png"), np.dstack([np.zeros_like(ao), ao, np.zeros_like(ao), np.full_like(ao, 0.08)]))

    bpy.data.objects.remove(ground, do_unlink=True)
    final = bpy.data.materials.new("sandbags_baked")
    for o in objs:
        o.location = (0, 0, 0)
        me = o.data
        me.materials.clear()
        me.materials.append(final)
        if me.color_attributes:
            me.color_attributes.remove(me.color_attributes[0])
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "sandbags.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="OFF", path_mode="STRIP")
    with open(os.path.join(OUT, "sandbags.json"), "w") as f:
        json.dump(dict(run=RUN, cols=grid[0], rows=grid[1], segments=info), f, indent=1)
    log(f"wrote {OUT}/sandbags.fbx")


if __name__ == "__main__":
    main()
