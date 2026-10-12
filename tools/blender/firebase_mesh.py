"""
The firebase's built things as geometry: two M35 trucks, an M151 jeep, the
watchtower and the timbers of a bunker's roof (the owner, 2026-10-12, of a
screenshot of the vehicle park: "the truck and the shed are looking so bad
it's dragging the game's look... they need to be looking realistic and not
like polygons").

    Blender -b --factory-startup --python tools/blender/firebase_mesh.py
    Blender -b --factory-startup --python tools/blender/firebase_mesh.py -- --look   # build and render, bake nothing
    Blender -b --factory-startup --python tools/blender/firebase_mesh.py -- --out Logs/firebase

They were cards baked from boxes (plant_bake.py `build_firebase`): a truck was
eleven boxes and ten cylinders photographed side on and stood up facing the
lens, flat from any other angle, and the bunker's roof was one stretched cube.
Here each is what it is, built part by part to its real measurements, every
edge rounded as pressed steel and sawn timber are, then weathered from itself.

  M35       the 2 1/2 ton 6x6 of 1965, 6.7 m long: frame rails, axles on their
            springs, ten wheels with their rims and tread, bumper and brush
            guard, the tapered hood with its louvres, flat fenders, headlights
            in their guards, the open cab with its canvas top and split
            windscreen, fuel tank, steps, mirrors, the exhaust stack, and the
            cargo bed with its steel sides, slatted racks, tailgate and mud
            flaps. Twice: one under its tarpaulin on five bows, one with the
            bows bare and a load of crates
  M151      the quarter-ton "MUTT", 3.4 m: the unit body with its
            horizontal-slot grille and wheel arches, flat hood, folding
            windscreen, four seats, steering wheel, spare and jerrycan on the
            back, the radio's antenna
  tower     in members, so it can come apart when it is brought down (Stage 7):
            four splayed legs, the cross braces, the deck's joists and planks,
            the posts and rail of the cabin, the corrugated roof on its
            rafters, and the ladder up the face toward the lens
  bunker    two posts, a beam, and a 2.4 m run of roof logs laid side by side,
            which the game lays end to end like the sandbag runs

Paint, canvas, rubber and glass are made here (measured albedos: olive drab
0.07, canvas 0.16, rubber 0.03); the timber is Poly Haven `weathered_planks`
and the roof `corrugated_iron_02`, both CC0 and already in SourceArt/.

Weather, baked from the thing itself as the sandbag walls' is: road dust up to
the axles and thinning above them, earth in every crease (the occlusion), the
paint faded and dusty where it faces the sky, the timber bleached on top and
damp at its foot.

Outputs, in Assets/_Project/Art/Structures/:
    firebase.fbx          m35_covered, m35_open, jeep (their fronts toward
                          Blender's +x, which is Unity's -x), tower_legs,
                          tower_braces, tower_deck, tower_cabin, tower_roof,
                          tower_ladder, bunker_post, bunker_beam, bunker_roof;
                          each with its origin on the ground under its middle
    vehicles_albedo.png (2048), vehicles_normal.png, vehicles_mask.png (1024)
    timber_albedo.png, timber_normal.png, timber_mask.png (1024)
    firebase.json         each mesh's size and triangles
"""
import json
import math
import os
import sys

import bmesh
import bpy
import mathutils
import numpy as np
import OpenImageIO as oiio
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Structures")
ATLAS = 2048
EARTH = np.array([0.150, 0.105, 0.070])          # the lanes' packed soil: road dust, mud
DUST = np.array([0.30, 0.24, 0.17])              # the same, dry and fine, on paint
MOSS = np.array([0.050, 0.070, 0.030])


def log(msg):
    print(f"[firebase] {msg}", flush=True)


# --- the kit: a mesh built part by part ----------------------------------------------------------

class Kit:
    """A mesh in the making. Every part is made on its own, its edges rounded, and added with its material."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.layer = self.bm.faces.layers.int.new("mat")
        self.mats = []

    def slot(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def take(self, part, mat, matrix=None):
        """Add a part (a bmesh) and free it."""
        me = bpy.data.meshes.new("part")
        if matrix is not None:
            part.transform(matrix)
        part.to_mesh(me)
        part.free()
        before = len(self.bm.faces)
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        self.bm.faces.ensure_lookup_table()
        k = self.slot(mat)
        for f in self.bm.faces[before:]:
            f[self.layer] = k
            f.smooth = True

    def box(self, lo, hi, mat, bevel=0.012, segs=2, rot=None, pivot=None, taper=None):
        """A box from corner to corner, its edges rounded. rot: Euler angles about pivot (default its middle).
        taper: (axis, scale y, scale z at the +axis end) for a hood or a fender that narrows."""
        lo, hi = Vector(lo), Vector(hi)
        size, mid = hi - lo, (lo + hi) / 2
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * size.x, v.co.y * size.y, v.co.z * size.z))
            if taper:
                axis, sy, sz = taper
                t = v.co[axis] / size[axis] + 0.5
                others = [a for a in range(3) if a != axis]
                v.co[others[0]] *= 1 + (sy - 1) * t
                v.co[others[1]] *= 1 + (sz - 1) * t
        b = min(bevel, 0.45 * min(size))
        if b > 0.0005:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=b, segments=segs, profile=0.5, affect="EDGES", clamp_overlap=True)
        m = mathutils.Matrix.Translation(mid)
        if rot:
            p = Vector(pivot) if pivot else mid
            m = mathutils.Matrix.Translation(p) @ mathutils.Euler(rot).to_matrix().to_4x4() @ mathutils.Matrix.Translation(mid - p)
        self.take(bm, mat, m)

    def cyl(self, a, b, r, mat, segs=16, r2=None, bevel=0.0):
        """A cylinder (or a cone, with r2) from one point to another, capped."""
        a, b = Vector(a), Vector(b)
        d = b - a
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=d.length)
        if bevel > 0:
            rim = [e for e in bm.edges if len(e.link_faces) == 2 and any(len(f.verts) > 4 for f in e.link_faces)]
            bmesh.ops.bevel(bm, geom=rim, offset=bevel, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)
        m = mathutils.Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        self.take(bm, mat, m)

    def tube(self, pts, r, mat, segs=8):
        """A bent bar or pipe through points: a cylinder a leg, a ball at each bend."""
        pts = [Vector(p) for p in pts]
        for p, q in zip(pts, pts[1:]):
            self.cyl(p, q, r, mat, segs)
        for p in pts[1:-1]:
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(4, segs // 2), radius=r)
            self.take(bm, mat, mathutils.Matrix.Translation(p))

    def lathe(self, profile, at, axis, mat, segs=24):
        """A shape turned about an axis through a point: profile is (distance along the axis, radius) pairs."""
        bm = bmesh.new()
        rings = []
        for h, r in profile:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), h)) for k in range(segs)])
        for a, b in zip(rings, rings[1:]):
            for k in range(segs):
                bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
        for ring, flip in ((rings[0], True), (rings[-1], False)):
            if profile[rings.index(ring)][1] > 1e-4:
                bm.faces.new(ring[::-1] if flip else ring)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        m = mathutils.Matrix.Translation(Vector(at)) @ Vector(axis).to_track_quat("Z", "Y").to_matrix().to_4x4()
        self.take(bm, mat, m)

    def sheet(self, rows, mat, thick=0.0, away=None, up=(0, 0, 1)):
        """A sheet through a grid of points (rows of equal length): canvas, a corrugated roof. Its face is
        turned away from a point (`away`: the inside of what it covers) or, without one, toward `up`; with
        `thick`, it has a second face that far behind the first, so that it is there from both sides. (The
        game draws one side of a face: a sheet of one face was a roof from above and nothing from below.)"""
        bm = bmesh.new()
        grid = [[bm.verts.new(p) for p in row] for row in rows]
        faces = []
        for a, b in zip(grid, grid[1:]):
            for k in range(len(a) - 1):
                faces.append(bm.faces.new((a[k], a[k + 1], b[k + 1], b[k])))
        bm.normal_update()
        mid = sum((f.calc_center_median() for f in faces), Vector()) / len(faces)
        mean = sum((f.normal for f in faces), Vector())
        want = (mid - Vector(away)) if away is not None else Vector(up)
        if mean.dot(want) < 0:
            bmesh.ops.reverse_faces(bm, faces=faces)
            bm.normal_update()
        if thick > 0:
            back = {}
            for row in grid:
                for v in row:
                    back[v] = bm.verts.new(v.co - v.normal * thick)
            for f in faces:
                bm.faces.new([back[v] for v in reversed(f.verts)])
            # And its edge, closed: a strip from the face to its back all the way round.
            rim = [e for e in bm.edges if len(e.link_faces) == 1 and e.verts[0] in back and e.verts[1] in back]
            for e in rim:
                f = e.link_faces[0]
                a, b = e.verts
                order = list(f.verts)
                if order[(order.index(a) + 1) % len(order)] != b:
                    a, b = b, a
                bm.faces.new((b, a, back[a], back[b]))
        self.take(bm, mat)

    def object(self, weighted=True):
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        self.bm.to_mesh(me)
        for name in self.mats:
            me.materials.append(MATS[name])
        layer = me.attributes.get("mat")
        for p in me.polygons:
            p.material_index = layer.data[p.index].value
            p.use_smooth = True
        me.attributes.remove(layer)
        o = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(o)
        if weighted:
            # Flat faces stay flat and the rounded edges take the turn: without this a bevelled box is a pillow.
            mod = o.modifiers.new("normals", "WEIGHTED_NORMAL")
            mod.keep_sharp = False
            mod.weight = 100
            bpy.context.view_layer.objects.active = o
            for other in bpy.context.view_layer.objects:
                other.select_set(other is o)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        return o


MATS = {}


# --- what things are made of -----------------------------------------------------------------------

def nodes(name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    MATS[name] = mat
    return mat, nt, bsdf


def noise(nt, scale, detail=4.0, rough=0.6, coords="Object"):
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.inputs["Scale"].default_value = scale
    n.inputs["Detail"].default_value = detail
    n.inputs["Roughness"].default_value = rough
    nt.links.new(tc.outputs[coords], n.inputs["Vector"])
    return n


def ramp(nt, src, stops):
    r = nt.nodes.new("ShaderNodeValToRGB")
    el = r.color_ramp.elements
    while len(el) < len(stops):
        el.new(0.5)
    for e, (pos, col) in zip(el, stops):
        e.position = pos
        e.color = (*col, 1.0) if len(col) == 3 else col
    nt.links.new(src, r.inputs["Fac"])
    return r


def bump(nt, bsdf, height, strength, distance=0.01):
    b = nt.nodes.new("ShaderNodeBump")
    b.inputs["Strength"].default_value = strength
    b.inputs["Distance"].default_value = distance
    nt.links.new(height, b.inputs["Height"])
    nt.links.new(b.outputs["Normal"], bsdf.inputs["Normal"])
    return b


def star_image():
    """The white star of the US Army's vehicles, as an image: five points, one up."""
    if "star" in bpy.data.images:
        return bpy.data.images["star"]
    n = 256
    y, x = np.mgrid[-1:1:n * 1j, -1:1:n * 1j]
    r, th = np.hypot(x, y), np.arctan2(y, x)
    # Folded into one tenth of the turn: 0 at an inner corner, 36 degrees at a point.
    fold = np.abs((th - math.pi / 2) % (2 * math.pi / 5) - math.pi / 5)
    fold = math.pi / 5 - fold
    px, py = r * np.cos(fold), r * np.sin(fold)
    inner, outer = 0.382 * 0.94, 0.94
    ax, ay = inner, 0.0
    bx, by = outer * math.cos(math.pi / 5), outer * math.sin(math.pi / 5)
    inside = (bx - ax) * (py - ay) - (by - ay) * (px - ax) > 0
    soft = np.clip(inside.astype(np.float32), 0, 1)
    img = bpy.data.images.new("star", n, n, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.dstack([soft, soft, soft, np.ones_like(soft)]).astype(np.float32).ravel())
    img.pack()
    return img


def marking(nt, colour_in, centre, axis, size, both=False):
    """A star painted on: laid on along an axis of the object's own space (0 x, 1 y, 2 z), where the surface
    faces that way and lies at the centre's depth; with `both`, on the two sides of the thing alike."""
    def math_(op, a, b=None):
        m = nt.nodes.new("ShaderNodeMath")
        m.operation = op
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                m.inputs[i].default_value = v
            else:
                nt.links.new(v, m.inputs[i])
        return m.outputs[0]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    nrm = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], nrm.inputs[0])
    # Across and up on the surface: for a side (y) x and z; for a top (z) y and x, a point forward.
    ua, va = (0, 2) if axis == 1 else (1, 0)
    u = math_("ADD", math_("DIVIDE", math_("SUBTRACT", sep.outputs[ua], centre[ua]), size), 0.5)
    v = math_("ADD", math_("DIVIDE", math_("SUBTRACT", sep.outputs[va], centre[va]), size), 0.5)
    uv = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(u, uv.inputs[0]); nt.links.new(v, uv.inputs[1])
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = star_image()
    img.extension = "CLIP"
    nt.links.new(uv.outputs[0], img.inputs["Vector"])
    depth = sep.outputs[axis]
    facing = nrm.outputs[axis]
    if both:
        depth, facing = math_("ABSOLUTE", depth), math_("ABSOLUTE", facing)
    near = math_("LESS_THAN", math_("ABSOLUTE", math_("SUBTRACT", depth, abs(centre[axis]) if both else centre[axis])), 0.06)
    on = math_("MULTIPLY", math_("MULTIPLY", img.outputs["Color"], near), math_("GREATER_THAN", facing, 0.6))
    mix = nt.nodes.new("ShaderNodeMixRGB")
    mix.inputs["Color2"].default_value = (0.55, 0.55, 0.50, 1)             # white paint, weathered
    nt.links.new(math_("MULTIPLY", on, 0.9), mix.inputs["Fac"])
    nt.links.new(colour_in, mix.inputs["Color1"])
    return mix.outputs["Color"]


def painted(name, colour, rough=0.6, chips=True, marks=()):
    """Paint on steel: not one flat colour (each panel a little its own, by a slow noise), an orange-peel
    of a surface, and where it has been knocked the primer and the rust under it. marks: stars painted on
    (centre, axis, size, both sides)."""
    mat, nt, bsdf = nodes(name)
    c = np.array(colour)
    slow = noise(nt, 1.3, 3.0)
    base = ramp(nt, slow.outputs["Fac"], [(0.3, tuple(c * 0.82)), (0.7, tuple(c * 1.16))])
    base_out = base.outputs["Color"]
    for centre, axis, size, both in marks:
        base_out = marking(nt, base_out, centre, axis, size, both)
    if chips:
        fine = noise(nt, 46.0, 6.0, 0.75)
        worn = ramp(nt, fine.outputs["Fac"], [(0.69, (0, 0, 0)), (0.73, (1, 1, 1))])
        mix = nt.nodes.new("ShaderNodeMixRGB")
        mix.inputs["Color2"].default_value = (0.11, 0.055, 0.03, 1)          # rust-brown primer
        nt.links.new(worn.outputs["Color"], mix.inputs["Fac"])
        nt.links.new(base_out, mix.inputs["Color1"])
        nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
        bump(nt, bsdf, fine.outputs["Fac"], 0.12, 0.004)
    else:
        nt.links.new(base_out, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    return mat


def canvas(name, colour):
    """Treated cotton duck: a weave too fine to see, slack creases, each sheet sun-faded in patches."""
    mat, nt, bsdf = nodes(name)
    c = np.array(colour)
    slow = noise(nt, 2.2, 4.0)
    base = ramp(nt, slow.outputs["Fac"], [(0.3, tuple(c * 0.78)), (0.72, tuple(c * 1.2))])
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    folds = noise(nt, 5.5, 2.0, 0.5)
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.inputs["Scale"].default_value = 3.2
    wave.inputs["Distortion"].default_value = 3.5
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
    add = nt.nodes.new("ShaderNodeMath"); add.operation = "ADD"
    nt.links.new(folds.outputs["Fac"], add.inputs[0]); nt.links.new(wave.outputs["Fac"], add.inputs[1])
    bump(nt, bsdf, add.outputs[0], 0.5, 0.02)
    bsdf.inputs["Roughness"].default_value = 0.92
    return mat


def rubber(name):
    """A tyre: black gone grey-brown, and its tread, which is cut across it in bars."""
    mat, nt, bsdf = nodes(name)
    slow = noise(nt, 6.0, 3.0)
    base = ramp(nt, slow.outputs["Fac"], [(0.3, (0.020, 0.019, 0.018)), (0.75, (0.045, 0.041, 0.036))])
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.88
    return mat


def flat(name, colour, rough=0.6, metal=0.0):
    mat, nt, bsdf = nodes(name)
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    return mat


def scanned(name, folder, stem, scale, tint=(1, 1, 1), rough=0.85, strength=1.0):
    """A scanned surface laid on by the object's own space (from three sides, blended), so nothing needs unwrapping to wear it."""
    mat, nt, bsdf = nodes(name)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(os.path.join(SRC, folder, f"{stem}_diff_2k.png"))
    img.projection = "BOX"
    img.projection_blend = 0.25
    nt.links.new(mp.outputs["Vector"], img.inputs["Vector"])
    mul = nt.nodes.new("ShaderNodeMixRGB"); mul.blend_type = "MULTIPLY"; mul.inputs["Fac"].default_value = 1.0
    mul.inputs["Color2"].default_value = (*tint, 1)
    nt.links.new(img.outputs["Color"], mul.inputs["Color1"])
    nt.links.new(mul.outputs["Color"], bsdf.inputs["Base Color"])
    nor = nt.nodes.new("ShaderNodeTexImage")
    nor.image = bpy.data.images.load(os.path.join(SRC, folder, f"{stem}_nor_gl_2k.png"))
    nor.image.colorspace_settings.name = "Non-Color"
    nor.projection = "BOX"
    nor.projection_blend = 0.25
    nt.links.new(mp.outputs["Vector"], nor.inputs["Vector"])
    # A box-projected normal map has no one tangent frame: its relief is taken as height instead.
    bw = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(nor.outputs["Color"], bw.inputs["Color"])
    lum = nt.nodes.new("ShaderNodeRGBToBW")
    nt.links.new(img.outputs["Color"], lum.inputs["Color"])
    bump(nt, bsdf, lum.outputs["Val"], 0.35 * strength, 0.01)
    bsdf.inputs["Roughness"].default_value = rough
    return mat


def materials():
    # Olive drab 24087, weathered; the truck's with a star on each door, the jeep's with one on its hood.
    painted("od", (0.066, 0.072, 0.040), marks=[((1.07, 1.05, 1.42), 1, 0.40, True)])
    painted("od_jeep", (0.066, 0.072, 0.040), marks=[((1.10, 0.0, 1.03), 2, 0.56, False)])
    painted("od_dark", (0.040, 0.044, 0.028), 0.7)               # the same in shade: frame, axles, underside
    painted("steel", (0.035, 0.033, 0.030), 0.55, chips=False)   # bare dark steel: springs, hubs, exhaust
    canvas("canvas", (0.150, 0.150, 0.095))
    canvas("seat", (0.085, 0.090, 0.055))
    rubber("tyre")
    flat("glass", (0.03, 0.04, 0.045), 0.08)
    flat("lens", (0.55, 0.55, 0.5), 0.15)
    flat("red", (0.25, 0.02, 0.015), 0.3)
    flat("white", (0.62, 0.62, 0.58), 0.6)
    scanned("crate", "polyhaven/weathered_planks", "weathered_planks", 1.6, (0.55, 0.60, 0.36))      # ammunition boxes: wood stained olive
    scanned("slat", "polyhaven/weathered_planks", "weathered_planks", 1.3, (0.50, 0.55, 0.36))       # the bed's racks, painted once
    scanned("timber", "polyhaven/weathered_planks", "weathered_planks", 0.9, (0.86, 0.78, 0.66))
    scanned("log", "polyhaven/weathered_planks", "weathered_planks", 1.4, (0.62, 0.52, 0.40), strength=1.6)
    scanned("iron", "polyhaven/corrugated_iron_02", "corrugated_iron_02", 0.5, (0.9, 0.86, 0.8), rough=0.6)


# --- wheels ---------------------------------------------------------------------------------------

def wheel(k, at, r, w, side, dual=False, studs=6):
    """A wheel on its hub, its axle along y: the tyre with its shoulders and its tread bars, the dished steel rim,
    the hub and its nuts. side: +1 on the left (+y), -1 on the right; a dual is two tyres on one hub."""
    x, y, z = at
    rim = r * 0.58
    for n in range(2 if dual else 1):
        cy = y - side * n * (w + 0.035)
        # The tyre: a square shoulder, a rounded wall, the bead at the rim.
        prof = [(-w / 2 * 0.62, rim), (-w / 2 * 0.86, rim + 0.04), (-w / 2, r * 0.80), (-w / 2 * 0.97, r * 0.93), (-w / 2 * 0.80, r),
                (w / 2 * 0.80, r), (w / 2 * 0.97, r * 0.93), (w / 2, r * 0.80), (w / 2 * 0.86, rim + 0.04), (w / 2 * 0.62, rim)]
        k.lathe(prof, (x, cy, z), (0, 1, 0), "tyre", 24)
        # Tread: bars across the crown, raked like a mud tyre's. (Plain blocks: they are a centimetre high,
        # and rounded they were two thirds of the truck's triangles.)
        bars = 18
        for b in range(bars):
            a = 2 * math.pi * (b + 0.5 * n) / bars
            for half in (-1, 1):
                mid = Vector((x + math.cos(a) * (r + 0.003), cy + half * w * 0.22, z + math.sin(a) * (r + 0.003)))
                k.box(mid - Vector((0.032, w * 0.20, 0.012)), mid + Vector((0.032, w * 0.20, 0.012)), "tyre", 0.0,
                      rot=(0, -a + math.pi / 2, half * 0.30))
        # The rim: dished in from the outside, with its lock ring.
        out = side if n == 0 else -side
        dish = [(w * 0.30, rim), (w * 0.32, rim - 0.02), (w * 0.10, rim * 0.80), (w * 0.02, rim * 0.52), (w * 0.10, rim * 0.34), (w * 0.14, 0.0)]
        k.lathe([(h * out, rr) for h, rr in dish], (x, cy, z), (0, 1, 0), "od_dark", 20)
        inner = [(-w * 0.30 * out, rim), (-w * 0.22 * out, rim * 0.6), (-w * 0.22 * out, 0.0)]
        k.lathe(inner, (x, cy, z), (0, 1, 0), "steel", 14)
        if n == 0:
            # The hub and its nuts, on the outer wheel.
            k.cyl((x, cy + side * w * 0.08, z), (x, cy + side * (w * 0.14 + 0.10), z), rim * 0.26, "od_dark", 12, r2=rim * 0.20, bevel=0.008)
            for s in range(studs):
                a = 2 * math.pi * s / studs
                c = Vector((x + math.cos(a) * rim * 0.42, cy + side * w * 0.07, z + math.sin(a) * rim * 0.42))
                k.cyl(c, c + Vector((0, side * 0.035, 0)), 0.016, "steel", 6)


def axle(k, x, z, half, drive=True):
    """An axle under the frame: the tube from wheel to wheel, the differential's pumpkin, and a leaf spring a side."""
    k.cyl((x, -half, z), (x, half, z), 0.055, "od_dark", 10)
    if drive:
        k.lathe([(-0.16, 0.06), (-0.12, 0.15), (0.0, 0.19), (0.12, 0.15), (0.16, 0.06)], (x, 0.12, z), (0, 1, 0), "od_dark", 14)
        k.cyl((x + 0.12, 0.12, z), (x + 0.34, 0.12, z + 0.04), 0.07, "od_dark", 10)


def spring(k, x, y, z, length, leaves=6):
    """A leaf spring: plates stacked, each shorter than the one above, bowed."""
    for n in range(leaves):
        half = length / 2 * (1 - 0.13 * n)
        k.box((x - half, y - 0.035, z - 0.012 * n - 0.012), (x + half, y + 0.035, z - 0.012 * n), "steel", 0.003, 1)
    k.box((x - 0.05, y - 0.05, z - 0.012 * leaves - 0.03), (x + 0.05, y + 0.05, z + 0.02), "steel", 0.006, 1)


# --- the M35 ---------------------------------------------------------------------------------------

FRONT, REAR = 2.47, (-0.83, -2.05)               # the axles, x
TYRE_R, TYRE_W = 0.53, 0.26


def m35_common():
    k = Kit("m35")
    # Frame: two rails the length of it and their crossmembers.
    for sy in (-1, 1):
        k.box((-3.30, sy * 0.43 - 0.04, 0.82), (3.20, sy * 0.43 + 0.04, 1.02), "od_dark", 0.008, 1)
    for x in (-3.2, -1.44, 0.3, 1.6, 3.1):
        k.box((x - 0.05, -0.43, 0.84), (x + 0.05, 0.43, 0.98), "od_dark", 0.008, 1)
    # Running gear.
    axle(k, FRONT, TYRE_R, 0.82)
    for x in REAR:
        axle(k, x, TYRE_R, 0.88)
    for sy in (-1, 1):
        spring(k, FRONT, sy * 0.43, 0.80, 1.15)
        spring(k, -1.44, sy * 0.43, 0.80, 1.30, 8)
        k.box((-2.10, sy * 0.52 - 0.03, 0.50), (-0.78, sy * 0.52 + 0.03, 0.58), "od_dark", 0.01, 1)     # the bogie's torque rods
    k.cyl((FRONT - 0.34, 0.12, 0.57), (0.5, 0.05, 0.80), 0.04, "steel", 8)                             # driveshafts
    k.cyl((0.2, 0.05, 0.80), (REAR[0] + 0.34, 0.12, 0.57), 0.04, "steel", 8)
    k.cyl((REAR[0] - 0.2, 0.12, 0.57), (REAR[1] + 0.34, 0.12, 0.57), 0.04, "steel", 8)
    k.box((0.15, -0.22, 0.62), (0.75, 0.25, 0.98), "od_dark", 0.03)                                    # transfer case
    k.box((1.75, -0.38, 0.70), (2.95, 0.38, 1.05), "steel", 0.04)                                      # the engine's sump, seen under the hood
    wheel(k, (FRONT, 0.86, TYRE_R), TYRE_R, TYRE_W, 1)
    wheel(k, (FRONT, -0.86, TYRE_R), TYRE_R, TYRE_W, -1)
    for x in REAR:
        wheel(k, (x, 1.04, TYRE_R), TYRE_R, TYRE_W, 1, dual=True)
        wheel(k, (x, -1.04, TYRE_R), TYRE_R, TYRE_W, -1, dual=True)

    # The bumper: a heavy channel, with its shackles and the brush guard above it.
    k.box((3.24, -1.16, 0.78), (3.36, 1.16, 1.02), "od", 0.015)
    for sy in (-1, 1):
        k.box((3.10, sy * 0.43 - 0.05, 0.84), (3.26, sy * 0.43 + 0.05, 0.98), "od_dark", 0.01, 1)
        k.tube([(3.36, sy * 0.80, 0.86), (3.44, sy * 0.80, 0.90), (3.44, sy * 0.80, 0.98), (3.36, sy * 0.80, 1.00)], 0.016, "steel", 6)
    # Brush guard: uprights and bars before the radiator and the headlights.
    for y in (-0.92, -0.52, 0.52, 0.92):
        k.box((3.20, y - 0.02, 1.02), (3.24, y + 0.02, 1.78), "od", 0.006, 1)
    for z in (1.12, 1.30, 1.48, 1.66, 1.78):
        k.box((3.20, -0.94, z - 0.014), (3.235, 0.94, z + 0.014), "od", 0.005, 1)
    # Radiator shell and grille, set back in the guard.
    k.box((3.04, -0.50, 1.04), (3.14, 0.50, 1.74), "od", 0.02)
    for n in range(9):
        y = -0.40 + n * 0.10
        k.box((3.135, y - 0.012, 1.10), (3.155, y + 0.012, 1.68), "od_dark", 0.004, 1)
    # Headlights in round shells on the fender fronts, their blackout lamps beside them.
    for sy in (-1, 1):
        k.lathe([(0.0, 0.0), (0.0, 0.07), (0.06, 0.105), (0.16, 0.105), (0.17, 0.095)], (2.99, sy * 0.72, 1.36), (1, 0, 0), "od", 16)
        k.lathe([(0.168, 0.094), (0.185, 0.07), (0.19, 0.0)], (2.99, sy * 0.72, 1.36), (1, 0, 0), "lens", 16)
        k.cyl((2.96, sy * 0.72, 1.24), (2.96, sy * 0.72, 1.15), 0.02, "od_dark", 6)
        k.cyl((3.04, sy * 0.95, 1.50), (3.12, sy * 0.95, 1.50), 0.035, "od", 10, bevel=0.006)             # blackout marker
    # Hood: narrower and lower at the nose than at the cowl, its top edges round; louvres in its sides.
    k.box((1.76, -0.66, 1.06), (3.06, 0.66, 1.84), "od", 0.07, 3, taper=(0, 0.80, 0.93))
    for sy in (-1, 1):
        for n in range(7):
            x = 2.02 + n * 0.12
            k.box((x, sy * 0.62 - 0.015, 1.34), (x + 0.07, sy * 0.62 + 0.015, 1.62), "od_dark", 0.006, 1, rot=(0, 0, sy * 0.10))
        k.box((1.78, sy * 0.50 - 0.02, 1.835), (3.0, sy * 0.50 + 0.02, 1.85), "od_dark", 0.004, 1, rot=(0, 0.063, -sy * 0.02))   # hinge line
    # Fenders: flat on top, turned down at the front and at the back onto the step.
    for sy in (-1, 1):
        k.box((1.85, sy * 0.92 - 0.30, 1.38), (3.02, sy * 0.92 + 0.30, 1.42), "od", 0.012)
        k.box((2.98, sy * 0.92 - 0.30, 1.14), (3.02, sy * 0.92 + 0.30, 1.42), "od", 0.012, rot=(0, 0.35, 0), pivot=(3.0, sy * 0.92, 1.42))
        k.box((1.80, sy * 0.92 - 0.30, 1.06), (1.86, sy * 0.92 + 0.30, 1.42), "od", 0.012, rot=(0, -0.45, 0), pivot=(1.86, sy * 0.92, 1.42))
        k.box((1.90, sy * 0.63, 1.10), (2.95, sy * 0.63 + sy * 0.02, 1.40), "od_dark", 0.004, 1)                  # the inner skirt
    # Cowl and windscreen: two panes in a frame that folds, raked back a little.
    k.box((1.60, -1.02, 1.06), (1.80, 1.02, 1.80), "od", 0.03)
    for sy in (-1, 1):
        k.box((1.66, sy * 0.50 - 0.46, 1.84), (1.69, sy * 0.50 + 0.46, 2.36), "glass", 0.004, 1, rot=(0, -0.10, 0), pivot=(1.68, 0, 1.80))
    for y in (-1.0, 0.0, 1.0):
        k.box((1.65, y - 0.03, 1.80), (1.71, y + 0.03, 2.40), "od", 0.01, 1, rot=(0, -0.10, 0), pivot=(1.68, 0, 1.80))
    for z in (1.80, 2.38):
        k.box((1.65, -1.02, z - 0.025), (1.71, 1.02, z + 0.025), "od", 0.01, 1, rot=(0, -0.10, 0), pivot=(1.68, 0, 1.80))
    # Cab: floor, the back panel, doors half height with their handles, seats, the wheel.
    k.box((0.46, -1.02, 1.04), (1.62, 1.02, 1.12), "od_dark", 0.01, 1)
    k.box((0.44, -1.04, 1.06), (0.52, 1.04, 1.86), "od", 0.02)
    for sy in (-1, 1):
        k.box((0.52, sy * 1.02 - 0.025, 1.10), (1.62, sy * 1.02 + 0.025, 1.74), "od", 0.02)
        k.box((0.62, sy * 1.045 - 0.006, 1.20), (1.52, sy * 1.045 + 0.006, 1.64), "od", 0.03, 2)                 # the door's pressed panel
        k.box((0.66, sy * 1.05, 1.52), (0.76, sy * 1.07, 1.55), "steel", 0.004, 1)
        k.box((0.60, sy * 0.52 - 0.36, 1.28), (1.10, sy * 0.52 + 0.36, 1.40), "seat", 0.04)
        k.box((0.54, sy * 0.52 - 0.36, 1.36), (0.66, sy * 0.52 + 0.36, 1.86), "seat", 0.04, rot=(0, -0.12, 0), pivot=(0.6, 0, 1.36))
        # Steps under the doors, and the mirrors on their arms.
        k.box((0.70, sy * 0.98 - 0.16, 0.66), (1.50, sy * 0.98 + 0.16, 0.70), "od_dark", 0.008, 1)
        k.tube([(1.72, sy * 1.02, 1.80), (1.80, sy * 1.26, 1.92), (1.80, sy * 1.26, 2.10)], 0.012, "od_dark", 6)
        k.box((1.795, sy * 1.26 - 0.07, 2.00), (1.815, sy * 1.26 + 0.07, 2.22), "od", 0.008, 1)
    k.cyl((1.40, 0.52, 1.52), (1.20, 0.52, 1.70), 0.02, "steel", 8)                                    # steering column
    k.lathe([(-0.012, 0.19), (0.0, 0.205), (0.012, 0.19), (0.0, 0.175), (-0.012, 0.19)], (1.19, 0.52, 1.71), (-0.74, 0, 0.67), "steel", 18)
    # The canvas cab top over its bows, sagging between them, and the back curtain.
    rows = []
    for i in range(9):
        x = 0.44 + (1.76 - 0.44) * i / 8
        sag = 0.03 * math.sin(math.pi * i / 4) ** 2
        rows.append([(x, -1.05 * math.cos(math.pi * j / 10), 2.36 + 0.14 * math.sin(math.pi * j / 10) - sag * math.sin(math.pi * j / 10)) for j in range(11)])
    k.sheet(rows, "canvas", 0.012, away=(1.1, 0, 1.6))
    k.box((0.44, -1.03, 1.86), (0.47, 1.03, 2.38), "canvas", 0.008, 1)
    for sy in (-1, 1):
        k.box((0.46, sy * 1.03, 2.28), (1.70, sy * 1.03 + sy * 0.015, 2.38), "canvas", 0.005, 1)                 # the top's side flap
    # Exhaust stack up the right of the cab, fuel tank and tool box under the doors, the spare behind the cab.
    k.cyl((1.72, -1.10, 0.90), (1.72, -1.10, 2.62), 0.045, "steel", 10)
    k.cyl((1.72, -1.10, 1.30), (1.72, -1.10, 2.10), 0.065, "steel", 10, bevel=0.01)
    k.box((0.70, -1.02, 0.72), (1.56, -0.62, 1.04), "od", 0.07, 3)
    k.cyl((1.0, -0.82, 1.04), (1.0, -0.82, 1.08), 0.045, "od_dark", 10)
    k.box((0.78, 0.62, 0.72), (1.50, 1.0, 1.02), "od", 0.02)
    # Cargo bed: floor, steel sides, the slatted racks above them, the front board, the tailgate on its chains.
    k.box((-3.36, -1.14, 1.20), (0.34, 1.14, 1.28), "od_dark", 0.01, 1)
    for x in np.linspace(-3.2, 0.2, 6):
        k.box((x - 0.04, -1.12, 1.04), (x + 0.04, 1.12, 1.20), "od_dark", 0.008, 1)                              # sills
    k.box((0.28, -1.16, 1.28), (0.34, 1.16, 2.06), "od", 0.012)
    for sy in (-1, 1):
        k.box((-3.36, sy * 1.14 - 0.025, 1.28), (0.34, sy * 1.14 + 0.025, 1.66), "od", 0.012)
        for x in np.linspace(-3.26, 0.24, 7):
            k.box((x - 0.03, sy * 1.17 - 0.02, 1.26), (x + 0.03, sy * 1.17 + 0.02, 2.08), "od", 0.008, 1)          # stakes
        for z in (1.74, 1.88, 2.02):
            k.box((-3.34, sy * 1.14 - 0.012, z - 0.05), (0.30, sy * 1.14 + 0.012, z + 0.05), "slat", 0.006, 1)
        # Mud flaps and the tail lights.
        k.box((-2.80, sy * 0.86 - 0.28, 0.42), (-2.78, sy * 0.86 + 0.28, 1.02), "tyre", 0.004, 1)
        k.cyl((-3.37, sy * 0.95, 1.10), (-3.40, sy * 0.95, 1.10), 0.045, "red", 10)
        k.box((-3.40, sy * 0.36 - 0.10, 0.86), (-3.28, sy * 0.36 + 0.10, 1.02), "od_dark", 0.02)                 # bumperettes
    k.box((-3.40, -1.12, 1.28), (-3.35, 1.12, 1.66), "od", 0.012)
    for sy in (-1, 1):
        k.box((-3.385, sy * 0.55 - 0.42, 1.34), (-3.405, sy * 0.55 + 0.42, 1.60), "od", 0.03, 2)                 # the tailgate's pressed panels
    k.tube([(-3.40, 0.0, 0.92), (-3.50, 0.0, 0.92), (-3.52, 0.0, 0.99)], 0.03, "steel", 8)                       # the pintle
    return k


def m35_cover():
    """The tarpaulin on its five bows: pulled tight over each, sagging between, its skirts down the racks,
    its back flaps tied open."""
    k = Kit("m35_cover")
    bows = np.linspace(-3.30, 0.28, 5)
    for x in bows:
        k.tube([(x, -1.12, 2.06), (x, -1.10, 2.34), (x, -0.78, 2.69), (x, 0.0, 2.80), (x, 0.78, 2.69), (x, 1.10, 2.34), (x, 1.12, 2.06)], 0.02, "od_dark", 6)
    rows = []
    n_x, n_a = 33, 18
    for i in range(n_x):
        x = -3.36 + (0.34 + 3.36) * i / (n_x - 1)
        # How far to the nearest bow: the canvas hangs between them.
        gap = min(abs(x - b) for b in bows) / ((bows[1] - bows[0]) / 2)
        row = []
        for j in range(n_a + 1):
            a = math.pi * j / n_a
            up = math.sin(a)
            sag = 0.055 * min(1.0, gap) ** 1.5 * up ** 2 + 0.012 * math.sin(7 * a + x * 3.1) * up
            y = -1.19 * math.cos(a) * (1 + 0.012 * math.sin(5 * x + j))
            z = 2.08 + (0.79 - sag) * up ** 0.72
            if j in (0, n_a):
                z = 1.70 + 0.03 * math.sin(4.1 * x + j)             # the skirt, down over the racks
            row.append((x, y, z))
        rows.append(row)
    k.sheet(rows, "canvas", 0.015, away=(-1.5, 0, 1.8))
    # The back: two flaps drawn aside and tied half way up, the bed open between them. Each hangs from
    # the arch of the last bow, so its top follows the arch.
    for sy in (-1, 1):
        rows = []
        for i in range(7):
            t = i / 6
            y = 1.17 - 0.66 * t
            top = 2.06 + 0.79 * math.sin(math.acos(min(1.0, y / 1.19))) ** 0.72
            row = []
            for j in range(7):
                u = j / 6
                tied = 0.30 * t * math.sin(math.pi * u) ** 2                 # gathered toward the side where it is tied
                row.append((-3.385 - 0.035 * math.sin(5 * t + 2.2 * u) * math.sin(math.pi * u), sy * (y + tied), 1.70 + (top - 1.70) * u))
            rows.append(row)
        k.sheet(rows, "canvas", 0.012, away=(-2.6, 0, 2.2))
    return k


def m35_load():
    """The bows bare, the tarpaulin rolled and lashed behind the cab, and a load: ammunition crates and drums."""
    k = Kit("m35_load")
    rng = np.random.default_rng(65)
    for x in np.linspace(-3.30, 0.28, 5):
        k.tube([(x, -1.12, 2.06), (x, -1.10, 2.34), (x, -0.78, 2.69), (x, 0.0, 2.80), (x, 0.78, 2.69), (x, 1.10, 2.34), (x, 1.12, 2.06)], 0.02, "od_dark", 6)
    k.cyl((0.14, -1.05, 2.16), (0.14, 1.05, 2.16), 0.13, "canvas", 12, bevel=0.03)
    for y in (-0.6, 0.0, 0.6):
        k.lathe([(-0.012, 0.136), (0.012, 0.136)], (0.14, y, 2.16), (0, 1, 0), "seat", 12)
    for n in range(9):
        cx = -3.15 + (n % 5) * 0.62 + rng.uniform(-0.03, 0.03)
        cz = 1.28 + (n // 5) * 0.31
        cy = rng.uniform(-0.25, 0.05) if n < 5 else rng.uniform(-0.35, 0.0)
        k.box((cx, cy - 0.45, cz), (cx + 0.56, cy + 0.45, cz + 0.30), "crate", 0.008, 1, rot=(0, 0, rng.uniform(-0.05, 0.05)))
        for e in (-0.38, 0.38):
            k.box((cx - 0.008, cy + e - 0.03, cz + 0.02), (cx + 0.568, cy + e + 0.03, cz + 0.305), "crate", 0.004, 1)   # battens
    for n in range(3):
        c = Vector((-0.55 + 0.02 * n, 0.72 - 0.02 * n, 1.28))
        c.x -= n * 0.62
        k.cyl(c, c + Vector((0, 0, 0.88)), 0.29, "od", 20, bevel=0.012)
        for z in (0.28, 0.60):
            k.lathe([(-0.012, 0.295), (0.0, 0.302), (0.012, 0.295)], c + Vector((0, 0, z)), (0, 0, 1), "od", 20)
    return k


# --- the M151 ----------------------------------------------------------------------------------------

def jeep():
    k = Kit("jeep")
    r, w = 0.39, 0.19
    fx, rx = 1.05, -1.11
    for x in (fx, rx):
        k.cyl((x, -0.60, r), (x, 0.60, r), 0.04, "od_dark", 8)
        k.lathe([(-0.10, 0.04), (-0.07, 0.10), (0.0, 0.12), (0.07, 0.10), (0.10, 0.04)], (x, 0.0, r), (0, 1, 0), "od_dark", 12)
        for sy in (-1, 1):
            wheel(k, (x, sy * 0.675, r), r, w, sy, studs=5)
    k.box((-1.5, -0.30, 0.34), (1.5, 0.30, 0.44), "od_dark", 0.02)                                     # the unit body's belly
    # The body: one tub, its sides cut away over the wheels in flat-topped arches.
    k.box((-1.62, -0.78, 0.44), (0.62, 0.78, 0.50), "od_dark", 0.01, 1)
    for sy in (-1, 1):
        # The side: high at the cowl and over the rear wheel, cut down between them where a man gets in,
        # its top edge rolled; a strap across the gap.
        k.box((-1.62, sy * 0.78 - 0.025, 0.92), (-0.34, sy * 0.78 + 0.025, 0.99), "od_jeep", 0.022, 3)
        k.box((0.46, sy * 0.78 - 0.025, 0.92), (0.62, sy * 0.78 + 0.025, 0.99), "od_jeep", 0.022, 3)
        k.box((-0.40, sy * 0.78 - 0.025, 0.72), (0.52, sy * 0.78 + 0.025, 0.78), "od_jeep", 0.022, 3)
        k.box((-0.62, sy * 0.78 - 0.02, 0.46), (-0.34, sy * 0.78 + 0.02, 0.94), "od_jeep", 0.018, 3)
        k.box((0.46, sy * 0.78 - 0.02, 0.46), (0.56, sy * 0.78 + 0.02, 0.94), "od_jeep", 0.018, 3)
        k.box((-0.38, sy * 0.78 - 0.02, 0.46), (0.50, sy * 0.78 + 0.02, 0.75), "od_jeep", 0.018, 3)
        k.box((-0.36, sy * 0.79 - 0.004, 0.93), (0.48, sy * 0.79 + 0.004, 0.965), "seat", 0.003, 1, rot=(0, 0.035, 0))   # the strap
        k.box((-0.34, sy * 0.80 - 0.006, 0.58), (0.44, sy * 0.80 + 0.006, 0.63), "od_jeep", 0.012, 2)         # the rib pressed along the side
        k.box((-0.60, sy * 0.78 - 0.02, 0.40), (0.54, sy * 0.78 + 0.02, 0.48), "od_dark", 0.02, 2)            # the rocker under it
        k.box((-1.62, sy * 0.78 - 0.02, 0.46), (-1.55, sy * 0.78 + 0.02, 0.94), "od_jeep", 0.012)
        k.box((-1.58, sy * 0.78 - 0.02, 0.84), (-0.60, sy * 0.78 + 0.02, 0.94), "od_jeep", 0.012)             # over the rear wheel
        k.box((-1.50, sy * 0.60, 0.50), (-0.66, sy * 0.60 + sy * 0.02, 0.86), "od_dark", 0.006, 1)       # the arch's inner wall
        k.box((-1.50, sy * 0.60 - 0.02, 0.84), (-0.66, sy * 0.78, 0.88), "od_dark", 0.006, 1)
        # Front fender: flat, level with the hood, squared off.
        k.box((0.56, sy * 0.66 - 0.14, 0.93), (1.60, sy * 0.66 + 0.14, 0.985), "od_jeep", 0.024, 3)
        k.box((1.54, sy * 0.66 - 0.14, 0.70), (1.60, sy * 0.66 + 0.14, 0.98), "od_jeep", 0.024, 3)
        k.box((0.56, sy * 0.80 - 0.012, 0.80), (1.58, sy * 0.80 + 0.012, 0.98), "od_jeep", 0.01, 1)
        k.box((0.56, sy * 0.52, 0.50), (1.56, sy * 0.52 + sy * 0.02, 0.96), "od_dark", 0.006, 1)
    k.box((-1.64, -0.78, 0.46), (-1.58, 0.78, 0.98), "od_jeep", 0.022, 3)                                   # the back panel
    for y in (-0.5, 0.0, 0.5):
        k.box((-1.652, y - 0.02, 0.54), (-1.636, y + 0.02, 0.90), "od_jeep", 0.008, 1)                          # its pressed ribs
    k.box((-1.70, -0.74, 0.44), (-1.60, 0.74, 0.52), "od_jeep", 0.02, 2)                                    # the rear bumper
    k.box((0.56, -0.78, 0.46), (0.62, 0.78, 1.02), "od_jeep", 0.014)                                        # the firewall and dash
    # Hood, flat and wide, and the grille panel: horizontal slots, the lamps set into it.
    k.box((0.60, -0.53, 0.95), (1.62, 0.53, 1.03), "od_jeep", 0.035, 3, taper=(0, 0.94, 1.0))
    for sy in (-1, 1):
        k.box((0.70, sy * 0.30 - 0.008, 1.028), (1.50, sy * 0.30 + 0.008, 1.036), "od_jeep", 0.004, 1)         # the hood's pressed ribs
        k.box((0.64, sy * 0.49 - 0.02, 1.0), (0.70, sy * 0.49 + 0.02, 1.045), "od_dark", 0.008, 1)            # its hinges
        k.box((1.50, sy * 0.47 - 0.015, 0.96), (1.56, sy * 0.47 + 0.015, 1.04), "steel", 0.006, 1)            # its catches
    k.box((1.56, -0.52, 0.50), (1.62, 0.52, 1.0), "od_jeep", 0.014)
    for n in range(6):
        z = 0.60 + n * 0.055
        k.box((1.615, -0.24, z - 0.014), (1.632, 0.24, z + 0.014), "od_dark", 0.004, 1)
    for sy in (-1, 1):
        k.lathe([(0.0, 0.085), (0.02, 0.085), (0.035, 0.07), (0.04, 0.0)], (1.60, sy * 0.38, 0.80), (1, 0, 0), "lens", 14)
        k.lathe([(0.0, 0.10), (0.03, 0.10), (0.03, 0.086), (0.0, 0.086)], (1.60, sy * 0.38, 0.80), (1, 0, 0), "od_dark", 14)
        k.cyl((1.60, sy * 0.38, 0.93), (1.63, sy * 0.38, 0.93), 0.03, "od_dark", 8)
    k.box((1.60, -0.72, 0.46), (1.70, 0.72, 0.56), "od_jeep", 0.014)                                        # bumper
    for sy in (-1, 1):
        k.box((1.69, sy * 0.50 - 0.05, 0.47), (1.74, sy * 0.50 + 0.05, 0.55), "od_dark", 0.01, 1)
    # Windscreen: one frame, two panes, raked; folded forward it lies on the hood. Here it is up.
    piv = (0.60, 0, 1.02)
    for y in (-0.74, 0.0, 0.74):
        k.box((0.585, y - 0.022, 1.02), (0.615, y + 0.022, 1.66), "od_jeep", 0.008, 1, rot=(0, -0.20, 0), pivot=piv)
    for z in (1.04, 1.64):
        k.box((0.585, -0.76, z - 0.022), (0.615, 0.76, z + 0.022), "od_jeep", 0.008, 1, rot=(0, -0.20, 0), pivot=piv)
    for sy in (-1, 1):
        k.box((0.595, sy * 0.37 - 0.34, 1.07), (0.605, sy * 0.37 + 0.34, 1.61), "glass", 0.003, 1, rot=(0, -0.20, 0), pivot=piv)
    # The driver's mirror, on its arm at the side of the frame.
    k.tube([(0.56, 0.76, 1.30), (0.60, 0.94, 1.34), (0.60, 0.94, 1.40)], 0.008, "od_dark", 5)
    k.cyl((0.595, 0.94, 1.44), (0.615, 0.94, 1.44), 0.065, "od_jeep", 12, bevel=0.004)
    # The radio behind the driver, and the grab rail before the bench.
    k.box((-1.04, 0.18, 0.50), (-0.66, 0.62, 0.84), "od_dark", 0.02, 2)
    k.box((-1.03, 0.22, 0.84), (-0.67, 0.58, 0.86), "steel", 0.006, 1)
    k.tube([(-0.36, -0.70, 0.98), (-0.36, -0.70, 1.18), (-0.36, 0.70, 1.18), (-0.36, 0.70, 0.98)], 0.014, "od_dark", 6)
    # Seats: two in front on their frames, the bench behind; the wheel; the gear levers.
    for sy in (-1, 1):
        k.box((-0.28, sy * 0.38 - 0.24, 0.80), (0.20, sy * 0.38 + 0.24, 0.90), "seat", 0.035)
        k.box((-0.34, sy * 0.38 - 0.24, 0.86), (-0.24, sy * 0.38 + 0.24, 1.32), "seat", 0.035, rot=(0, -0.16, 0), pivot=(-0.28, 0, 0.86))
        k.tube([(-0.26, sy * 0.38 - 0.22, 0.50), (-0.26, sy * 0.38 - 0.22, 0.80)], 0.012, "od_dark", 6)
        k.tube([(0.16, sy * 0.38 + 0.22, 0.50), (0.16, sy * 0.38 + 0.22, 0.80)], 0.012, "od_dark", 6)
    k.box((-1.50, -0.56, 0.84), (-1.10, 0.56, 0.93), "seat", 0.035)
    k.box((-1.56, -0.56, 0.90), (-1.47, 0.56, 1.30), "seat", 0.035, rot=(0, -0.12, 0), pivot=(-1.50, 0, 0.90))
    k.cyl((0.58, 0.38, 0.92), (0.30, 0.38, 1.16), 0.017, "steel", 8)
    k.lathe([(-0.01, 0.17), (0.0, 0.184), (0.01, 0.17), (0.0, 0.156), (-0.01, 0.17)], (0.29, 0.38, 1.17), (-0.76, 0, 0.65), "steel", 16)
    k.cyl((0.20, 0.0, 0.50), (0.14, 0.0, 0.86), 0.012, "steel", 6)
    k.box((0.50, -0.70, 0.86), (0.56, 0.70, 1.0), "od_dark", 0.02)                                     # the dash's face
    # On the back: the spare, the jerrycan, the tail lamps; the antenna on its spring base.
    k.lathe([(-0.09, 0.22), (-0.095, 0.32), (-0.08, 0.385), (0.08, 0.385), (0.095, 0.32), (0.09, 0.22)], (-1.76, -0.30, 0.88), (1, 0, 0), "tyre", 22)
    k.lathe([(-0.05, 0.22), (-0.02, 0.12), (-0.02, 0.0)], (-1.76, -0.30, 0.88), (1, 0, 0), "od_dark", 14)
    k.box((-1.80, 0.28, 0.56), (-1.65, 0.62, 1.02), "od_jeep", 0.03, 3)
    k.box((-1.775, 0.38, 1.0), (-1.675, 0.52, 1.07), "od_jeep", 0.02)
    for sy in (-1, 1):
        k.cyl((-1.64, sy * 0.66, 0.70), (-1.67, sy * 0.66, 0.70), 0.04, "red", 10)
    k.cyl((-1.50, 0.80, 0.98), (-1.50, 0.80, 1.12), 0.03, "steel", 8)
    k.cyl((-1.50, 0.80, 1.12), (-1.58, 0.80, 3.30), 0.008, "steel", 5, r2=0.004)
    # The pioneer tools on the side: a shovel's handle and blade.
    k.cyl((-0.50, -0.815, 0.70), (0.40, -0.815, 0.72), 0.016, "slat", 6)
    k.box((0.38, -0.83, 0.62), (0.56, -0.81, 0.80), "steel", 0.008, 1)
    return k


# --- the watchtower -----------------------------------------------------------------------------------

# The deck's planks come to 8.45 m, where the simulation's men stand (Maps.Firebase, the post's Height).
# The tower's middle is 1.32 m behind them, away from the lens (+y is toward it): they stand half a metre
# inside the front rail, and the ladder they climb, 0.45 m in front of where they stand, comes up through
# a hatch in the deck just inside that rail.
DECK, TOP = 8.40, 10.40
STAND = 1.32                                     # where the men stand on the deck, y
LADDER = STAND + 0.34                            # the ladder's rails: a man on it is 11 cm nearer the lens


def beam(k, a, b, size, mat="timber", bevel=0.012):
    """A sawn timber from one point to another, square in section."""
    a, b = Vector(a), Vector(b)
    d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size, v.co.y * size, v.co.z * d.length))
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
    k.take(bm, mat, mathutils.Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4())


def tower():
    """The tower in members, each a mesh of its own: when it is brought down (Stage 7) they come apart."""
    out = {}
    foot, head = 1.45, 1.20
    k = Kit("tower_legs")
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(k, (sx * foot, sy * foot, -0.3), (sx * head, sy * head, DECK), 0.24)
            k.box((sx * foot - 0.22, sy * foot - 0.22, -0.3), (sx * foot + 0.22, sy * foot + 0.22, 0.06), "timber", 0.02)       # footing
    out["tower_legs"] = k
    k = Kit("tower_braces")
    for face in range(4):
        for bay in range(3):
            z0, z1 = bay * DECK / 3 + 0.25, (bay + 1) * DECK / 3 - 0.15
            w0, w1 = foot - (foot - head) * z0 / DECK, foot - (foot - head) * z1 / DECK
            c, s = math.cos(face * math.pi / 2), math.sin(face * math.pi / 2)

            def at(u, w, z, c=c, s=s):
                x, y = u, w + 0.14                       # a face's braces lie on the outside of its legs
                return (x * c - y * s, x * s + y * c, z)
            beam(k, at(-w0, w0, z0), at(w1, w1, z1), 0.11)
            beam(k, at(w0, w0, z0), at(-w1, w1, z1), 0.11, bevel=0.01)
            k.box(Vector(at(0, (w0 + w1) / 2, (z0 + z1) / 2)) - Vector((0.03, 0.03, 0.03)),
                  Vector(at(0, (w0 + w1) / 2, (z0 + z1) / 2)) + Vector((0.03, 0.03, 0.03)), "steel", 0.008, 1)                  # the bolt where they cross
        for z in (DECK / 3, 2 * DECK / 3):
            w = foot - (foot - head) * z / DECK
            c, s = math.cos(face * math.pi / 2), math.sin(face * math.pi / 2)
            beam(k, (-w * c - (w + 0.14) * s, -w * s + (w + 0.14) * c, z), (w * c - (w + 0.14) * s, w * s + (w + 0.14) * c, z), 0.13)   # girts
    out["tower_braces"] = k
    k = Kit("tower_deck")
    for y in (-1.5, -0.5, 0.5, 1.28):
        beam(k, (-1.95, y, DECK - 0.09), (1.95, y, DECK - 0.09), 0.18)                                 # joists on the legs' heads
    rng = np.random.default_rng(8)
    y = -1.92
    while y < 1.9:
        wide = rng.uniform(0.17, 0.24)
        y1 = min(1.92, y + wide - 0.012)
        # The hatch the ladder comes up through: the planks over it stop short on either side.
        spans = [(-1.95, -0.42), (0.42, 1.95)] if y1 > LADDER - 0.22 else [(-1.95, 1.95)]
        for x0, x1 in spans:
            k.box((x0 + (rng.uniform(-0.03, 0.03) if x0 < -1 else 0), y, DECK), (x1 + (rng.uniform(-0.03, 0.03) if x1 > 1 else 0), y1, DECK + 0.045), "timber", 0.006, 1,
                  rot=(rng.uniform(-0.006, 0.006), 0, rng.uniform(-0.004, 0.004)))
        y += wide
    for x in (-0.44, 0.44):
        beam(k, (x, LADDER - 0.24, DECK - 0.03), (x, 1.93, DECK - 0.03), 0.07, bevel=0.008)             # the hatch's trimmers
    out["tower_deck"] = k
    k = Kit("tower_cabin")
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(k, (sx * 1.82, sy * 1.82, DECK + 0.04), (sx * 1.82, sy * 1.82, TOP - 0.12), 0.13)
    for face in range(4):
        c, s = math.cos(face * math.pi / 2), math.sin(face * math.pi / 2)
        for z in (DECK + 0.55, DECK + 1.05):
            beam(k, (-1.82 * c - 1.82 * s, -1.82 * s + 1.82 * c, z), (1.82 * c - 1.82 * s, 1.82 * s + 1.82 * c, z), 0.07, bevel=0.008)
        beam(k, (-1.9 * c - 1.82 * s, -1.9 * s + 1.82 * c, TOP - 0.16), (1.9 * c - 1.82 * s, 1.9 * s + 1.82 * c, TOP - 0.16), 0.11)      # the plate the rafters sit on
    out["tower_cabin"] = k
    k = Kit("tower_roof")
    pitch = 0.10
    for x in (-1.7, -0.57, 0.57, 1.7):
        beam(k, (x, -2.25, TOP - 0.05 - 2.25 * pitch), (x, 2.25, TOP - 0.05 + 2.25 * pitch), 0.09)     # rafters
    # Corrugated iron: three sheets across, lapped, each wave 7.6 cm.
    for n in range(3):
        x0, x1 = -2.3 + n * 1.52, -2.3 + n * 1.52 + 1.62
        waves = 21
        rows = []
        for j in range(2):
            y = -2.4 if j == 0 else 2.4
            z = TOP + 0.02 + y * pitch + 0.006 * n
            rows.append([(x0 + (x1 - x0) * i / (waves * 4), y, z + 0.011 * math.sin(2 * math.pi * i / 4)) for i in range(waves * 4 + 1)])
        k.sheet(rows, "iron", 0.004, up=(0, 0, 1))
    out["tower_roof"] = k
    k = Kit("tower_ladder")
    # Up the face toward the lens (+y), upright, just outside that face's braces, and through the hatch
    # to a hand's height above the deck; stood off the legs on two blocks.
    for sx in (-0.27, 0.27):
        beam(k, (sx, LADDER, -0.1), (sx, LADDER, DECK + 1.0), 0.085, bevel=0.01)
    n = int((DECK + 0.6) / 0.31)
    for i in range(n + 1):
        z = 0.28 + i * 0.31
        beam(k, (-0.33, LADDER, z), (0.33, LADDER, z), 0.05, bevel=0.008)
    for z in (DECK / 3, 2 * DECK / 3):
        for sx in (-0.27, 0.27):
            beam(k, (sx, LADDER - 0.26, z + 0.1), (sx, LADDER, z + 0.1), 0.06, bevel=0.008)
    out["tower_ladder"] = k
    return out


# --- the bunker's timbers ----------------------------------------------------------------------------

def bunker():
    out = {}
    k = Kit("bunker_post")                                       # 2.2 m, a log stood on end with its top notched for the beam
    k.cyl((0, 0, -0.25), (0, 0, 2.20), 0.11, "log", 16, r2=0.095, bevel=0.012)
    out["bunker_post"] = k
    k = Kit("bunker_beam")                                       # 2.4 m along x: laid end to end across the posts
    k.cyl((-1.2, 0, 0.0), (1.2, 0, 0.0), 0.10, "log", 16, r2=0.092, bevel=0.012)
    out["bunker_beam"] = k
    k = Kit("bunker_roof")                                       # a 2.4 m run of logs across (along y, 1 m: the game stretches them to the depth)
    rng = np.random.default_rng(65)
    x = -1.2
    while x < 1.2 - 0.05:
        r = rng.uniform(0.075, 0.105)
        r = min(r, (1.2 - x) / 2 + 0.01)
        over = rng.uniform(0.02, 0.10)
        k.cyl((x + r, -0.5 - over, r), (x + r, 0.5 + rng.uniform(0.02, 0.10), r * rng.uniform(0.86, 1.0)), r, "log", 14, r2=r * rng.uniform(0.84, 0.96), bevel=0.008)
        x += 2 * r - 0.006
    out["bunker_roof"] = k
    return out


# --- baking ---------------------------------------------------------------------------------------------

def bake(objs, kind, img, samples=4, space=None, passes=None):
    for mat in {m for o in objs for m in o.data.materials}:
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
    sc.render.bake.margin = 8
    sc.render.bake.use_clear = True
    kw = {}
    if kind == "NORMAL":
        sc.render.bake.normal_space = space or "TANGENT"
    if passes:
        kw["pass_filter"] = passes
    bpy.ops.object.bake(type=kind, **kw)
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[::-1].copy()


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


def blotches(rng, n, cells=24):
    """Smooth noise in 0..1 over the atlas: what makes no two patches of paint quite alike."""
    g = rng.random((cells, cells)).astype(np.float32)
    buf = oiio.ImageBuf(oiio.ImageSpec(cells, cells, 1, "float"))
    buf.set_pixels(oiio.ROI(0, cells, 0, cells), g.reshape(cells, cells, 1))
    big = oiio.ImageBufAlgo.resize(buf, roi=oiio.ROI(0, n, 0, n))
    a = np.asarray(big.get_pixels(oiio.FLOAT)).reshape(n, n)
    return np.clip((a - a.min()) / max(a.max() - a.min(), 1e-4), 0, 1)


def unwrap(objs):
    """Every object's faces laid out in one square together, by the angle between them."""
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0016, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def smaller(px, by):
    """An image at 1/by of its size: each texel the mean of the block it stands for."""
    if by == 1:
        return px
    h, w, c = px.shape
    return px.reshape(h // by, by, w // by, by, c).mean((1, 3))


def bake_set(name, objs, weather, out, albedo_by=1, rest_by=2):
    """Unwrap a set of objects into one atlas, bake what they are made of, and weather it from themselves.
    Baked at 2048; written smaller where the screen cannot show more (albedo_by, and rest_by for the normal
    and the mask, which the game reads at half the albedo's size at most)."""
    unwrap(objs)
    img = bpy.data.images.new("bake " + name, ATLAS, ATLAS, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    base = bake(objs, "DIFFUSE", img, passes={"COLOR"})[..., :3]
    rough = bake(objs, "ROUGHNESS", img)[..., 0]
    normal = bake(objs, "NORMAL", img)[..., :3]
    world = bake(objs, "NORMAL", img, space="OBJECT")[..., :3] * 2 - 1
    place = bake(objs, "POSITION", img)[..., :3]
    ao = bake(objs, "AO", img, samples=96)[..., 0]
    used = base.sum(-1) > 1e-5
    up = np.clip(world[..., 2], 0, 1)
    height = place[..., 2]
    stain = blotches(np.random.default_rng(7), ATLAS)[..., None]
    fine = blotches(np.random.default_rng(9), ATLAS, 160)[..., None]
    albedo, smooth = weather(base, 1 - rough, ao, up, height, stain, fine)
    lum = albedo @ np.array([0.2126, 0.7152, 0.0722])
    log(f"{name}: atlas {ATLAS}, {used.mean() * 100:.0f}% used, albedo median {np.median(lum[used]):.3f} "
        f"(clean {np.median((base @ [0.2126, 0.7152, 0.0722])[used]):.3f}), occlusion mean {ao[used].mean():.2f}")
    write_png(os.path.join(out, f"{name}_albedo.png"), to_srgb(smaller(albedo, albedo_by)))
    n = smaller(normal, rest_by) * 2 - 1
    n = n / np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-4) * 0.5 + 0.5
    write_png(os.path.join(out, f"{name}_normal.png"), n)
    write_png(os.path.join(out, f"{name}_mask.png"), smaller(np.dstack([np.zeros_like(ao), ao, np.zeros_like(ao), smooth]), rest_by))
    return albedo


def weather_vehicle(base, smooth, ao, up, height, stain, fine):
    # (Most of a truck's surface is where one part sits against another, and is dark there whatever is
    # done: only what is properly shut in, a third lit or less, takes the earth.)
    crease = np.clip((0.55 - ao) * 1.5, 0, 0.6)[..., None]
    albedo = base * (0.88 + 0.24 * stain)
    # The paint where it faces the sky: chalked by the sun, a film of dust on it.
    fade = (up ** 1.6)[..., None] * (0.16 + 0.18 * stain)
    grey = albedo.mean(-1, keepdims=True)
    albedo = albedo * (1 - fade) + (0.5 * grey + 0.5 * albedo + 0.30 * DUST) * fade
    # Road dust: thick on the wheels and what hangs under the frame, thinning to nothing by the bed's floor.
    road = np.clip(1.0 - height / 1.25, 0, 1)[..., None] ** 1.8 * (0.40 + 0.45 * fine)
    road = np.clip(road, 0, 0.8)
    albedo = albedo * (1 - road) + DUST * (0.7 + 0.3 * stain) * road
    # Earth packed where two parts meet, darker and damp.
    albedo = albedo * (1 - crease) + EARTH * (0.55 + 0.3 * stain) * crease
    smooth = smooth * (1 - np.clip(road[..., 0] + fade[..., 0] + crease[..., 0], 0, 1) * 0.85)
    return albedo, smooth


def weather_timber(base, smooth, ao, up, height, stain, fine):
    crease = np.clip((0.6 - ao) * 1.4, 0, 0.7)[..., None]
    albedo = base * (0.84 + 0.32 * stain)
    albedo = albedo * (1 - crease) + EARTH * (0.5 + 0.3 * stain) * crease
    # Tops silvered by the sun; the foot dark, damp and going green.
    bleach = (up ** 1.5)[..., None] * 0.40
    grey = albedo.mean(-1, keepdims=True)
    albedo = albedo * (1 - bleach) + (grey * 1.2 + np.array([0.03, 0.028, 0.022])) * bleach
    damp = np.clip(1 - height / 0.7, 0, 1)[..., None] * (0.35 + 0.4 * stain)
    albedo = albedo * (1 - damp) + (albedo * 0.5 + MOSS * 0.5 + EARTH * 0.3) * damp
    return albedo, smooth * 0.5


# --- putting it together --------------------------------------------------------------------------------

def setup():
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
        log(f"GPU unavailable ({e}); on the CPU")
    sc.world = bpy.data.worlds.new("bake")
    sc.world.light_settings.distance = 0.6
    return sc


def look(sc, objs, path, at, eye, lens=50):
    """A picture of what was built, under a sun and a sky, to look at before it goes anywhere."""
    for o in list(bpy.data.objects):
        if o.type in ("CAMERA", "LIGHT"):
            bpy.data.objects.remove(o, do_unlink=True)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    cam.data.lens = lens
    cam.location = eye
    cam.rotation_euler = (Vector(at) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.data.energy = 4.0
    sun.data.angle = 0.02
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    w = sc.world
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.55, 0.65, 0.8, 1)
    bg.inputs["Strength"].default_value = 0.9
    sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.render.filepath = path
    sc.view_settings.view_transform = "Standard"
    hidden = [o for o in bpy.data.objects if o.type == "MESH" and o not in objs and o.name != "ground"]
    for o in hidden:
        o.hide_render = True
    bpy.ops.render.render(write_still=True)
    for o in hidden:
        o.hide_render = False
    log(f"looked: {path}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = OUT
    if "--out" in argv:
        out = os.path.join(ROOT, argv[argv.index("--out") + 1])
    only_look = "--look" in argv
    os.makedirs(out, exist_ok=True)
    seen = os.path.join(ROOT, "Logs", "firebase")
    os.makedirs(seen, exist_ok=True)
    sc = setup()
    materials()

    # What is unwrapped and baked: each thing once. The two trucks are one truck and what differs.
    kits = dict(m35=m35_common(), m35_cover=m35_cover(), m35_load=m35_load(), jeep=jeep())
    timber = {**tower(), **bunker()}
    objs = {}
    spread = dict(m35=(0, 0), m35_cover=(0, 0), m35_load=(0, 6), jeep=(0, 12))
    for name, k in kits.items():
        objs[name] = k.object()
    for name, k in timber.items():
        objs[name] = k.object()
    tris = {n: sum(len(p.vertices) - 2 for p in o.data.polygons) for n, o in objs.items()}
    log("triangles: " + ", ".join(f"{n} {t}" for n, t in tris.items()))

    # The ground they stand on, so what is near it is occluded by it.
    bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, 0.0))
    ground = bpy.context.object
    ground.name = "ground"
    gm = bpy.data.materials.new("ground")
    gm.use_nodes = True
    next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Base Color"].default_value = (0.12, 0.10, 0.06, 1)
    ground.data.materials.append(gm)

    if only_look:
        look(sc, [objs["m35"], objs["m35_cover"]], os.path.join(seen, "m35_covered.png"), (0, 0, 1.4), (9.5, 8.5, 4.2))
        look(sc, [objs["m35"], objs["m35_load"]], os.path.join(seen, "m35_open.png"), (0, 0, 1.4), (-9.5, 8.0, 4.6))
        look(sc, [objs["m35"], objs["m35_cover"]], os.path.join(seen, "m35_side.png"), (0, 0, 1.4), (0.5, 16, 3.0), 70)
        look(sc, [objs["jeep"]], os.path.join(seen, "jeep.png"), (0, 0, 0.8), (5.0, 4.6, 2.4))
        look(sc, [objs["jeep"]], os.path.join(seen, "jeep_back.png"), (0, 0, 0.8), (-5.0, 4.2, 2.6))
        look(sc, [objs[n] for n in timber if n.startswith("tower")], os.path.join(seen, "tower.png"), (0, 0, 5.2), (10, 17, 7.5), 40)
        look(sc, [objs[n] for n in timber if n.startswith("bunker")], os.path.join(seen, "bunker.png"), (0, 0, 0.9), (3.5, 4.5, 2.4))
        return

    # Apart for the bake, so no thing's shade falls on another.
    for n, o in objs.items():
        o.location = (0, 0, 0)
    for i, n in enumerate(("m35", "jeep")):
        objs[n].location = (0, i * 14.0, 0)
    objs["m35_cover"].location = objs["m35"].location
    objs["m35_load"].location = (0, 28.0, 0)
    for i, n in enumerate(n for n in timber if n.startswith("bunker")):
        objs[n].location = (20 + 5 * i, 0, 0.3 if n != "bunker_post" else 0)
    for n in timber:
        if n.startswith("tower"):
            objs[n].location = (40, 0, 0)
    bpy.context.view_layer.update()
    bake_set("vehicles", [objs[n] for n in kits], weather_vehicle, out)
    # (A leg of the tower is 45 pixels across at the closest zoom: a 1024 atlas gives it 22 texels.)
    bake_set("timber", [objs[n] for n in timber], weather_timber, out, albedo_by=2, rest_by=2)
    bpy.data.objects.remove(ground, do_unlink=True)

    # The meshes the game is given: each truck is the truck and its own back.
    def joined(name, parts, material):
        copies = []
        for p in parts:
            c = objs[p].copy()
            c.data = objs[p].data.copy()
            c.location = (0, 0, 0)
            sc.collection.objects.link(c)
            copies.append(c)
        for o in bpy.context.view_layer.objects:
            o.select_set(o in copies)
        bpy.context.view_layer.objects.active = copies[0]
        if len(copies) > 1:
            bpy.ops.object.join()
        o = bpy.context.view_layer.objects.active
        o.name = o.data.name = name
        o.data.materials.clear()
        o.data.materials.append(material)
        return o

    # (The kits' own objects hold these names until now.)
    for n, o in objs.items():
        o.name = o.data.name = "kit " + n
    vm = bpy.data.materials.new("vehicles_baked")
    tm = bpy.data.materials.new("timber_baked")
    final = [joined("m35_covered", ["m35", "m35_cover"], vm), joined("m35_open", ["m35", "m35_load"], vm), joined("jeep", ["jeep"], vm)]
    final += [joined(n, [n], tm) for n in timber]
    info = {}
    for o in final:
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        info[o.name] = dict(length=round(max(v.x for v in bb) - min(v.x for v in bb), 3), width=round(max(v.y for v in bb) - min(v.y for v in bb), 3),
                            height=round(max(v.z for v in bb), 3), triangles=sum(len(p.vertices) - 2 for p in o.data.polygons))
        log(f"{o.name}: {info[o.name]}")
    for o in bpy.context.view_layer.objects:
        o.select_set(o in final)
    bpy.context.view_layer.objects.active = final[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, "firebase.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="OFF", path_mode="STRIP")
    with open(os.path.join(out, "firebase.json"), "w") as f:
        json.dump(dict(deck=round(DECK + 0.045, 3), top=TOP, stand=STAND, ladder=LADDER, meshes=info), f, indent=1)
        f.write("\n")
    log(f"wrote {out}/firebase.fbx")

    # And a look at them as the game will have them: the meshes that were written, wearing what was baked.
    for mat, name in ((vm, "vehicles"), (tm, "timber")):
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        col = nt.nodes.new("ShaderNodeTexImage")
        col.image = bpy.data.images.load(os.path.join(out, f"{name}_albedo.png"))
        nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
        nor = nt.nodes.new("ShaderNodeTexImage")
        nor.image = bpy.data.images.load(os.path.join(out, f"{name}_normal.png"))
        nor.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nor.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        bsdf.inputs["Roughness"].default_value = 0.75
    bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, 0.0))
    ground = bpy.context.object
    ground.name = "ground"
    ground.data.materials.append(gm)
    by = {o.name: o for o in final}
    look(sc, [by["m35_covered"]], os.path.join(seen, "baked_m35_covered.png"), (0, 0, 1.4), (9.5, 8.5, 4.2))
    look(sc, [by["m35_open"]], os.path.join(seen, "baked_m35_open.png"), (0, 0, 1.4), (-9.5, 8.0, 4.6))
    look(sc, [by["jeep"]], os.path.join(seen, "baked_jeep.png"), (0, 0, 0.8), (5.0, 4.6, 2.4))
    look(sc, [o for o in final if o.name.startswith("tower")], os.path.join(seen, "baked_tower.png"), (0, 0, 5.2), (10, 17, 7.5), 40)
    look(sc, [o for o in final if o.name.startswith("bunker")], os.path.join(seen, "baked_bunker.png"), (0, 0, 0.9), (3.5, 4.5, 2.4))


if __name__ == "__main__":
    main()
