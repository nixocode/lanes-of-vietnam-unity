"""
The soldiers as skinned 3D men for Unity (PLAN §12.3), replacing the baked
sprites: the earlier build's rigged MPFB2 soldiers (our own work on a CC0 base;
SourceArt/soldiers), made into FBX that Unity's Humanoid avatar can play
Mixamo's clips on.

    Blender -b --factory-startup --python tools/blender/soldier_rig.py -- [us_a us_b us_c vc_a vc_b vc_c] [--clips]

For each side:

  atlas     the nine materials (skin, fatigue, boots, vest, webbing, helmet,
            cover, gun steel, gun furniture), each at its palette albedo
            (albedo_gains), baked by Cycles into one 2048
            atlas: albedo, normal (tangent space, OpenGL like Unity), and a
            mask (R metallic, G occlusion, A smoothness — URP Lit's metallic
            map and occlusion map read the same texture). One material, so a
            man costs one draw call for his body and one for his rifle, not
            nine.
  rifle     split off the body and parented rigid to hand_r, as it was
            skinned; with two empties under it: grip_l, where the support
            hand holds it in the bind pose (the left hand's IK target), and
            muzzle.
  body      decimated to about BODY_TRIS triangles, after the bake, so the
            atlas holds the full mesh's detail. WebGL skins on the CPU, and a
            battle has up to 80 men.

The rig keeps its bind pose (the rifle held at the ready) as its rest; the
T-pose Humanoid maps from is enforced on Unity's side (SoldierImport), so
the mesh is not skinned twice.

With --clips, interim clips on the same rig until Mixamo's (all Humanoid, so
Mixamo's replace them clip for clip): the bind pose ("hold"), the sprite
bake's poses (stand_aim, kneel, kneel_aim, prone, prone_aim, dead0, dead1)
and its CMU motion capture (walk, run, crouch, idle) as keyed actions at 30
fps, each dropped so its feet are on the ground.

Outputs, in Assets/_Project/Art/Soldiers3D/:
    soldier_<side>.fbx, soldier_<side>_albedo.png, _normal.png, _mask.png,
    soldier_<side>.json (measurements), soldier_poses.fbx (--clips)

Coordinates: Blender (x, y, z); the soldier faces -Y, his left is +X.
"""
import json
import math
import os
import sys

import bpy
import mathutils
import numpy as np
import OpenImageIO as oiio

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt")
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Soldiers3D")

# The rebuilt bodies (tools/blender/soldier_body.py): MPFB2 dressed in real
# cloth with the earlier build's kit, three men a side.
#   fatigue_to  the sprite bake's OG-107 (0.085), for the helmet cover's scan
#   tan         the photographed skins are studio-lit and pale for men who live
#               outdoors: a gain on the skin alone
#   lift        plain gains for materials that bake too dark to read (polished
#               black shoes at 0.005)
US_TAN, VC_TAN = (0.55, 0.50, 0.46), (0.58, 0.53, 0.49)
SIDES = {
    "us_a": dict(src="soldiers/us_a_v2.glb", fatigue_to=0.085, tan=US_TAN, lift={"boots": 4.5}),
    "us_b": dict(src="soldiers/us_b_v2.glb", fatigue_to=0.085, tan=(1.75, 1.70, 1.65), lift={"boots": 4.5}),   # his skin photograph is dark already: 0.05 as shot, 0.085 here
    "us_c": dict(src="soldiers/us_c_v2.glb", fatigue_to=0.085, tan=US_TAN, lift={"boots": 4.5}),
    "vc_a": dict(src="soldiers/vc_a_v2.glb", tan=VC_TAN),
    "vc_b": dict(src="soldiers/vc_b_v2.glb", tan=VC_TAN),
    "vc_c": dict(src="soldiers/vc_c_v2.glb", tan=VC_TAN, lift={"boots": 4.5}),
}
ATLAS = 2048
BODY_TRIS = 9000
GUN_MATERIALS = ("gun_steel", "gun_furniture")
FPS = 30

# The CMU takes, as the sprite bake chose them (plant_bake.py MOCAP, with its
# reasons). frames: None, so the clip keeps the take's own timing at 30 fps.
MOCAP = [
    dict(name="walk", file="16_15.bvh", cycle=True),
    dict(name="run", file=["16_36.bvh", "16_35.bvh", "02_03.bvh", "35_17.bvh", "35_22.bvh"], cycle=True),
    dict(name="crouch", file="136_09.bvh", cycle=True, torso=0.45),
    dict(name="idle", file="137_28.bvh", cycle=False, window=3.0, torso=0.6),
]
POSES = ["hold", "stand_aim", "kneel", "kneel_aim", "prone", "prone_aim", "dead0", "dead1"]


def log(msg):
    print(f"[soldier] {msg}", flush=True)


# --- loading ---------------------------------------------------------------------------

def load(src):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, src))
    objs = list(bpy.data.objects)
    arm = [o for o in objs if o.type == "ARMATURE"][0]
    body = [o for o in objs if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers)][0]
    for o in objs:
        if o.type == "MESH" and o is not body:
            log(f"leaving out {o.name} ({len(o.data.vertices)} vertices, not skinned)")
            bpy.data.objects.remove(o, do_unlink=True)
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm, body


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def select_only(objs, active=None):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


# --- the atlas ---------------------------------------------------------------------------

def atlas_uv(objs):
    """A second UV map on each object: the first map's islands, scaled to one
    texel density and packed together into 0..1."""
    for o in objs:
        me = o.data
        src = me.uv_layers[0].name
        uv = me.uv_layers.new(name="atlas")
        me.uv_layers.active = uv
        for n in (m for m in me.materials if m and m.use_nodes):
            nt = n.node_tree
            for node in list(nt.nodes):
                # Every texture reads the source map by name; the atlas is only the bake's target.
                if node.type == "TEX_IMAGE" and not node.inputs["Vector"].is_linked:
                    uvn = nt.nodes.new("ShaderNodeUVMap")
                    uvn.uv_map = src
                    nt.links.new(uvn.outputs["UV"], node.inputs["Vector"])
                elif node.type == "UVMAP" and not node.uv_map:
                    node.uv_map = src
                elif node.type == "NORMAL_MAP":
                    node.uv_map = src
                elif node.type == "TEX_COORD":
                    for l in list(node.outputs["UV"].links):
                        uvn = nt.nodes.new("ShaderNodeUVMap")
                        uvn.uv_map = src
                        nt.links.new(uvn.outputs["UV"], l.to_socket)
        uv.active_render = True
    select_only(objs)
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")


def principled(mat):
    return next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def route(mats, socket, gains=None):
    """Wire each material's Principled input `socket` straight to its output
    as emission, for an EMIT bake, times its colour gain if it has one;
    returns the undo."""
    undo = []
    for mat in mats:
        nt = mat.node_tree
        p = principled(mat)
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
        em = nt.nodes.new("ShaderNodeEmission")
        inp = p.inputs[socket]
        if inp.is_linked and gains and mat.name in gains:
            mul = nt.nodes.new("ShaderNodeMix")
            mul.data_type, mul.blend_type = "RGBA", "MULTIPLY"
            mul.inputs["Factor"].default_value = 1.0
            nt.links.new(inp.links[0].from_socket, mul.inputs[6])
            mul.inputs[7].default_value = (*gains[mat.name], 1.0)
            nt.links.new(mul.outputs[2], em.inputs["Color"])
            undo.append((nt, None, None, mul))
        elif inp.is_linked:
            nt.links.new(inp.links[0].from_socket, em.inputs["Color"])
        else:
            v = inp.default_value
            em.inputs["Color"].default_value = (v[0], v[1], v[2], 1) if hasattr(v, "__len__") else (v, v, v, 1)
        old = out.inputs["Surface"].links[0].from_socket
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
        undo.append((nt, old, out, em))

    def restore():
        for nt, old, out, em in undo:
            if old is not None:
                nt.links.new(old, out.inputs["Surface"])
            nt.nodes.remove(em)
    return restore


def bake(objs, kind, img, clear=True):
    mats = {m for o in objs for m in o.data.materials if m}
    targets = []
    for mat in mats:
        nt = mat.node_tree
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = img
        nt.nodes.active = t
        targets.append((nt, t))
    select_only(objs)
    sc = bpy.context.scene
    sc.render.bake.margin = 16
    sc.render.bake.use_clear = clear
    sc.cycles.samples = 64 if kind == "AO" else 4
    if kind == "NORMAL":
        sc.render.bake.normal_space = "TANGENT"
        sc.render.bake.normal_r, sc.render.bake.normal_g, sc.render.bake.normal_b = "POS_X", "POS_Y", "POS_Z"
    bpy.ops.object.bake(type=kind)
    for nt, t in targets:
        nt.nodes.remove(t)


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[::-1]          # Blender's rows run bottom-up


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


def albedo_gains(mats, fatigue_to, tan=None, lift=None):
    """Per material, the colour gain that makes its base colour's median its
    palette albedo. The earlier build wrote each material as palette colour
    (its glTF factor: OG-107 at 0.118, black leather 0.022, gun steel 0.04,
    VC black cotton 0.03) times a fabric scan meant to carry only the
    pattern, but the scans were never normalised: the fatigue's linear median
    is 0.053 and the leather's 0.012, so the products baked 20 to 80 times
    too dark (a first bake: fatigue 0.007, boots 0.000). Dividing by the
    scan's own median, per channel, keeps its pattern and restores the
    palette. The US uniform and helmet cover then go to fatigue_to, the
    sprite bake's measured choice for this light."""
    gains = {}
    for mat in mats:
        link = principled(mat).inputs["Base Color"].links
        if not link:
            continue
        node = link[0].from_node
        if mat.name == "skin" and tan:
            gains[mat.name] = tuple(tan)
            continue
        if lift and mat.name in lift:
            gains[mat.name] = (lift[mat.name],) * 3
            continue
        # Only the earlier build's palette-times-scan materials (a Mix node):
        # the rebuilt body's own textures are already what they should be.
        if node.type not in ("MIX", "MIX_RGB"):
            continue
        tex = [n for n in ([node] + [l.from_node for i in node.inputs for l in i.links]) if n.type == "TEX_IMAGE" and n.image]
        if not tex:
            continue
        img = tex[0].image
        a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
        img.pixels.foreach_get(a)
        a = a.reshape(-1, 4)[:, :3]
        if img.colorspace_settings.name == "sRGB":          # a byte image's pixels are its stored, encoded values
            a = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
        g = 1.0 / np.maximum(np.median(a, 0), 1e-4)
        if fatigue_to and mat.name in ("fatigue", "helmet_cover"):
            g = g * fatigue_to / 0.1206
        gains[mat.name] = tuple(float(x) for x in g)
        log(f"{mat.name}: {img.name} median {np.median(a @ [0.2126, 0.7152, 0.0722]):.3f}, gain {g.mean():.1f}")
    return gains


def bake_atlas(side, body, rifle, fatigue_to, skin=None, tan=None, lift=None):
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
    both = [body, rifle]
    mats = {m for o in both for m in o.data.materials if m}
    for m in mats:
        if m.name == "skin" and skin:
            principled(m).inputs["Base Color"].default_value = (*skin, 1.0)
    img = bpy.data.images.new("bake", ATLAS, ATLAS, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    out = {}
    gains = albedo_gains(mats, fatigue_to, tan, lift)
    for name, socket in (("albedo", "Base Color"), ("metal", "Metallic"), ("rough", "Roughness")):
        restore = route(mats, socket, gains if socket == "Base Color" else None)
        bake(both, "EMIT", img)
        restore()
        out[name] = pixels(img)[..., :3].copy()
    bake(both, "NORMAL", img)
    out["normal"] = pixels(img)[..., :3].copy()
    # Occlusion within 8 cm: the creases and where kit meets cloth. Farther,
    # the layers of clothing inside one another occlude each other (a first
    # bake, unlimited, averaged 0.35).
    if sc.world is None:
        sc.world = bpy.data.worlds.new("bake")
    sc.world.light_settings.distance = 0.08
    # Occlusion: the body by itself, then the rifle by itself, so the rifle's
    # shadow is not baked onto a chest it will move away from.
    rifle.hide_render = True
    bake([body], "AO", img)
    rifle.hide_render = False
    body.hide_render = True
    bake([rifle], "AO", img, clear=False)
    body.hide_render = False
    out["ao"] = pixels(img)[..., 0].copy()

    albedo = out["albedo"]
    # Each material's median albedo, read back at its faces' atlas centres: the bake's check.
    for o in both:
        me = o.data
        uv = me.uv_layers["atlas"].data
        by = {}
        for poly in me.polygons:
            c = sum((uv[li].uv for li in poly.loop_indices), mathutils.Vector((0, 0))) / poly.loop_total
            x = min(ATLAS - 1, max(0, int(c.x * ATLAS))); y = min(ATLAS - 1, max(0, int((1 - c.y) * ATLAS)))
            by.setdefault(me.materials[poly.material_index].name, []).append(albedo[y, x])
        log("albedo by material: " + ", ".join(f"{k} {np.median(np.array(v) @ [0.2126, 0.7152, 0.0722]):.3f}"
                                                for k, v in sorted(by.items())))
    base = os.path.join(OUT, f"soldier_{side}")
    write_png(base + "_albedo.png", to_srgb(albedo))
    write_png(base + "_normal.png", out["normal"])
    mask = np.dstack([out["metal"][..., 0], out["ao"], np.zeros_like(out["ao"]), 1 - out["rough"][..., 0]])
    write_png(base + "_mask.png", mask)
    lum = albedo @ np.array([0.2126, 0.7152, 0.0722])
    used = lum > 1e-4
    log(f"atlas {ATLAS}: {used.mean() * 100:.0f}% used, albedo median {np.median(lum[used]):.3f}, "
        f"occlusion mean {out['ao'][used].mean():.2f}")
    bpy.data.images.remove(img)


# --- posing (plant_bake.py build_soldier's, facing -Y) -----------------------------------

class Poser:
    """Poses the rig by rotating bones about world axes through their heads,
    parents first; nothing depends on the bones' own local axes."""

    def __init__(self, arm, body, rifle):
        self.arm, self.body, self.rifle = arm, body, rifle
        self.F = mathutils.Vector((0, -1, 0))
        self.U = mathutils.Vector((0, 0, 1))
        self.P = self.F.cross(self.U).normalized()        # + pitch swings a hanging bone's tail forward
        self.reset()
        # The rifle's axis in the right hand's frame, from its vertices in the bind pose.
        pts = [rifle.matrix_world @ v.co for v in rifle.data.vertices]
        P0 = np.array([tuple(p) for p in pts])
        c = P0.mean(0)
        _, _, vt = np.linalg.svd(P0 - c, full_matrices=False)
        ax = mathutils.Vector(vt[0])
        wrist = self.head("hand_r")
        far = max(pts, key=lambda p: (p - wrist).length)
        if ax.dot(far - wrist) < 0:
            ax = -ax
        Mh = (arm.matrix_world @ arm.pose.bones["hand_r"].matrix).to_3x3()
        self.gun_axis_local = Mh.inverted() @ ax
        self.ground = self.zmin()

    def update(self):
        bpy.context.view_layer.update()

    def reset(self):
        for pb in self.arm.pose.bones:
            pb.matrix_basis = mathutils.Matrix.Identity(4)
        self.update()

    def head(self, bone):
        self.update()
        return self.arm.matrix_world @ self.arm.pose.bones[bone].head

    def rot(self, bone, axis, angle):
        pb = self.arm.pose.bones.get(bone)
        if pb is None or angle == 0:
            return
        self.update()
        a = (self.arm.matrix_world.to_3x3().inverted() @ axis).normalized()
        h = pb.head.copy()
        pb.matrix = (mathutils.Matrix.Translation(h) @ mathutils.Matrix.Rotation(angle, 4, a)
                     @ mathutils.Matrix.Translation(-h) @ pb.matrix)
        self.update()

    def align(self, bone, v_from, v_to):
        a, b = v_from.normalized(), v_to.normalized()
        ax = a.cross(b)
        if ax.length < 1e-6:
            return
        self.rot(bone, ax.normalized(), a.angle(b))

    def two_bone(self, upper, lower, hand, target, pole):
        S, E, W = self.head(upper), self.head(lower), self.head(hand)
        la, lb = (E - S).length, (W - E).length
        d = min((target - S).length, (la + lb) * 0.999)
        axis = (target - S).normalized()
        x = (la * la - lb * lb + d * d) / (2 * d)
        h = math.sqrt(max(la * la - x * x, 0.0))
        pdir = (pole - axis * pole.dot(axis)).normalized()
        E2 = S + axis * x + pdir * h
        self.align(upper, E - S, E2 - S)
        E, W = self.head(lower), self.head(hand)
        self.align(lower, W - E, (S + axis * d) - E)

    def lie_prone(self):
        P = self.P
        self.rot("pelvis", P, -1.5)
        self.rot("spine_02", P, 0.15); self.rot("spine_03", P, 0.2)
        self.rot("neck_01", P, 0.35); self.rot("head", P, 0.45)
        self.rot("thigh_l", self.F, -0.12); self.rot("thigh_r", self.F, 0.12)

    def shoulder_rifle(self, pitch=0.0):
        F, U, P = self.F, self.U, self.P
        S = self.head("upperarm_r")
        aim = (F * math.cos(pitch) - U * math.sin(pitch)).normalized()
        grip = S + aim * 0.30 - U * 0.06 + P * 0.04
        self.two_bone("upperarm_r", "lowerarm_r", "hand_r", grip, -U * 0.8 - P * 0.6)
        self.update()
        Mh = (self.arm.matrix_world @ self.arm.pose.bones["hand_r"].matrix).to_3x3()
        self.align("hand_r", Mh @ self.gun_axis_local, aim)
        grip2 = self.head("hand_r") + aim * 0.20 - U * 0.04 - P * 0.06
        self.two_bone("upperarm_l", "lowerarm_l", "hand_l", grip2, -U * 0.8 + P * 0.5)
        self.rot("head", P, 0.12)

    def kneel_legs(self):
        P = self.P
        self.rot("spine_01", P, 0.14)
        self.rot("thigh_r", P, 1.45); self.rot("calf_r", P, -1.45)
        self.rot("thigh_l", P, 0.12); self.rot("calf_l", P, -1.72); self.rot("foot_l", P, 0.6)

    def pose(self, name):
        F, U, P = self.F, self.U, self.P
        self.reset()
        if name == "hold":
            pass
        elif name == "kneel":
            self.kneel_legs()
        elif name == "prone":
            self.lie_prone()
            self.rot("upperarm_r", P, 0.9); self.rot("upperarm_l", P, 0.9)
        elif name == "stand_aim":
            self.rot("spine_01", P, 0.05)
            self.shoulder_rifle(0.04)
        elif name == "kneel_aim":
            self.kneel_legs()
            self.shoulder_rifle(0.03)
        elif name == "prone_aim":
            self.lie_prone()
            self.shoulder_rifle(0.0)
        elif name == "dead0":                               # thrown on his back
            self.rot("pelvis", P, 1.55)
            self.rot("thigh_l", F, 0.35); self.rot("thigh_r", F, -0.2); self.rot("calf_r", P, -0.5)
            self.rot("upperarm_l", P, 1.1); self.rot("upperarm_r", F, 0.6)
            self.rot("head", U, 0.9)
        elif name == "dead1":                               # face down, where he fell
            self.rot("pelvis", P, -1.52)
            self.rot("spine_03", P, 0.15); self.rot("head", F, 0.5)
            self.rot("upperarm_l", F, -1.0); self.rot("upperarm_r", F, 1.0)
            self.rot("thigh_l", F, 0.25); self.rot("thigh_r", F, -0.3); self.rot("calf_l", P, -0.6)

    def zmin(self):
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.body.evaluated_get(dg)
        me = ev.to_mesh()
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        ev.to_mesh_clear()
        M = np.array(self.body.matrix_world)
        z = co.reshape(-1, 3) @ M[2, :3] + M[2, 3]
        return float(z.min())

    def lift(self, dz):
        """Move the whole man up by dz metres, through the pelvis."""
        pb = self.arm.pose.bones["pelvis"]
        d = self.arm.matrix_world.to_3x3().inverted() @ mathutils.Vector((0, 0, dz))
        pb.matrix = mathutils.Matrix.Translation(d) @ pb.matrix
        self.update()

    # --- motion capture (plant_bake.py load_clip / apply_mocap) ---
    BONES = [("pelvis", "Hips"), ("spine_01", "LowerBack"), ("spine_02", "Spine"), ("spine_03", "Spine1"),
             ("neck_01", "Neck"), ("head", "Head"),
             ("thigh_l", "LeftUpLeg"), ("calf_l", "LeftLeg"), ("foot_l", "LeftFoot"), ("ball_l", "LeftToeBase"),
             ("thigh_r", "RightUpLeg"), ("calf_r", "RightLeg"), ("foot_r", "RightFoot"), ("ball_r", "RightToeBase")]

    def mocap(self, spec):
        """World rotations of BONES per frame at 30 fps, and the hips' bob, for one take."""
        arm, F, U = self.arm, self.F, self.U
        self.reset()
        tb = {b: (arm.matrix_world @ arm.pose.bones[b].matrix).to_quaternion() for b, _ in self.BONES}
        t_pelvis = arm.matrix_world @ arm.pose.bones["pelvis"].head
        t_foot = min(self.head("foot_l").z, self.head("foot_r").z)
        t_hip_h = t_pelvis.z - t_foot

        def yaw_to(a, b):
            a = mathutils.Vector((a.x, a.y, 0)).normalized(); b = mathutils.Vector((b.x, b.y, 0)).normalized()
            return mathutils.Quaternion(U, math.atan2(a.cross(b).z, a.dot(b)))

        path = os.path.join(SRC, "mocap", "cmu", spec["file"])
        before = set(bpy.data.objects)
        bpy.ops.import_anim.bvh(filepath=path, axis_forward="-Z", axis_up="Y", update_scene_fps=False,
                                use_fps_scale=False, frame_start=1)
        src = [o for o in bpy.data.objects if o not in before][0]
        sc = bpy.context.scene
        n = int(src.animation_data.action.frame_range[1])
        names = [s for _, s in self.BONES]

        def world(fr):
            sc.frame_set(int(fr), subframe=fr - int(fr))
            bpy.context.view_layer.update()
            M = src.matrix_world
            return {b: (M @ src.pose.bones[b].matrix) for b in names}

        T = world(1)
        rq = {b: T[b].to_quaternion() for b in T}
        right = T["RightUpLeg"].translation - T["LeftUpLeg"].translation
        Fs = U.cross(mathutils.Vector((right.x, right.y, 0))).normalized()
        s_hip = T["Hips"].translation.z - min(T["LeftFoot"].translation.z, T["RightFoot"].translation.z)
        scale = t_hip_h / s_hip
        Y0 = yaw_to(Fs, F)

        def heading(W):
            d = (W["Hips"].to_quaternion() @ rq["Hips"].inverted()) @ Fs
            return mathutils.Vector((d.x, d.y, 0)).normalized()

        if spec["cycle"]:
            lo, hi = (2, n - 1) if spec.get("short") else (max(2, int(n * 0.1)), int(n * 0.95))
            ahead = []
            for fr in range(lo, hi):
                W = world(fr)
                ahead.append((W["LeftFoot"].translation - W["Hips"].translation).dot(heading(W)))
            peaks = [lo + i for i in range(1, len(ahead) - 1) if ahead[i] >= ahead[i - 1] and ahead[i] > ahead[i + 1]
                     and ahead[i] > 0.5 * max(ahead)]
            if len(peaks) < 2:
                bpy.data.objects.remove(src, do_unlink=True)
                raise RuntimeError(f"{spec['file']}: no gait cycle found")
            a, b = peaks[len(peaks) // 2 - 1], peaks[len(peaks) // 2]
            Wa, Wb = world(a), world(b)
            travel = Wb["Hips"].translation - Wa["Hips"].translation
            stride = mathutils.Vector((travel.x, travel.y, 0)).length * scale
            frames = max(8, round((b - a) / 120.0 * FPS))
            span = [a + (b - a) * k / frames for k in range(frames)]      # the last key repeats the first
        else:
            win = int(spec["window"] * 120)
            samples = []
            for fr in range(2, n, 10):
                W = world(fr)
                samples.append((fr, W["LeftFoot"].translation.copy(), W["RightFoot"].translation.copy(), heading(W)))
            best, a = None, 2
            for i in range(len(samples)):
                j = i + win // 10
                if j >= len(samples):
                    break
                travel = sum((samples[k + 1][1] - samples[k][1]).length + (samples[k + 1][2] - samples[k][2]).length
                             for k in range(i, j))
                turn = max(samples[i][3].angle(samples[k][3]) for k in range(i, j + 1))
                travel += turn * (s_hip * 2.0)
                if best is None or travel < best:
                    best, a = travel, samples[i][0]
            b = a + win
            frames = max(8, round((b - a) / 120.0 * FPS))
            span = [a + (b - a) * k / (frames - 1) for k in range(frames)]
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
            for tb_name, sb in self.BONES:
                delta = W[sb].to_quaternion() @ rq[sb].inverted()
                r = Yc @ (Y0 @ delta @ Y0.inverted()) @ tb[tb_name]
                if tb_name in ("pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head") and spec.get("torso", 1) < 1:
                    r = tb[tb_name].slerp(r, spec["torso"])
                rots[tb_name] = r
            poses.append((rots, (W["Hips"].translation.z - z0) * scale))
        bpy.data.objects.remove(src, do_unlink=True)
        self.t_pelvis = t_pelvis
        return poses, stride, (b - a) / 120.0

    def apply(self, rots, bob):
        self.reset()
        arm = self.arm
        Minv = arm.matrix_world.inverted()
        Rinv = arm.matrix_world.to_quaternion().inverted()
        for b, _ in self.BONES:
            pb = arm.pose.bones[b]
            self.update()
            h = pb.head.copy()
            if b == "pelvis":
                h = Minv @ (self.t_pelvis + self.U * bob)
            pb.matrix = mathutils.Matrix.LocRotScale(h, Rinv @ rots[b], None)
        self.update()


# --- clips ---------------------------------------------------------------------------------

def capture(arm):
    return {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in arm.pose.bones}


def write_action(arm, name, frames):
    """Key captured poses, one a frame, into a new action. Poses are built with
    no action on the rig: with one assigned, every depsgraph update re-applies
    it over the pose being built (the first run measured the walk on the
    previous clip's dead man, and got a stride of -1 cm)."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, len(frames) - 1
    for f, cap in enumerate(frames):
        for pb in arm.pose.bones:
            pb.location, pb.rotation_quaternion = cap[pb.name]
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("rotation_quaternion", frame=f)
    arm.animation_data.action = None


def make_clips(arm, poser):
    meta, built = [], []
    for name in POSES:
        poser.pose(name)
        dz = poser.ground - poser.zmin()
        poser.lift(dz)
        built.append((name, [capture(arm)] * 2))
        meta.append(dict(name=name, frames=1, loop=True, source="posed (plant_bake.py poses)"))
        log(f"pose {name}: lifted {dz * 100:+.0f} cm to the ground")
    for spec in MOCAP:
        files = spec["file"] if isinstance(spec["file"], list) else [spec["file"]]
        for fi, fname in enumerate(files):
            try:
                poses, stride, seconds = poser.mocap(dict(spec, file=fname, short=len(files) > 1))
                break
            except RuntimeError as e:
                log(f"mocap {spec['name']}: {e}")
                if fi == len(files) - 1:
                    raise
        # Feet on the ground: the clip's mean lowest point, not each frame's, so the bob survives.
        zs = []
        for rots, bob in poses:
            poser.apply(rots, bob)
            zs.append(poser.zmin())
        dz = poser.ground - sum(zs) / len(zs)
        seq = list(range(len(poses)))
        if spec["cycle"]:
            seq.append(0)                                   # the loop closes on its first pose
        else:
            seq += list(range(len(poses) - 2, -1, -1))      # an idle plays forward and back
        caps = []
        for k in range(len(poses)):
            rots, bob = poses[k]
            poser.apply(rots, bob)
            poser.lift(dz)
            caps.append(capture(arm))
        built.append((spec["name"], [caps[k] for k in seq]))
        meta.append(dict(name=spec["name"], frames=len(seq) - 1, loop=True, stride_m=round(stride, 3),
                         seconds=round(seconds, 3) if spec["cycle"] else round((len(seq) - 1) / FPS, 3),
                         source=f"CMU {fname}"))
        log(f"mocap {spec['name']}: {len(seq) - 1} frames at {FPS} fps from {fname}, stride {stride:.2f} m, "
            f"cycle {seconds:.2f} s, lifted {dz * 100:+.0f} cm")
    poser.reset()
    for name, frames in built:
        write_action(arm, name, frames)
    poser.reset()
    return meta


# --- the build -----------------------------------------------------------------------------

def build(side, spec, clips):
    arm, body = load(spec["src"])
    src_tris = tris(body)
    # The rifle, off the body.
    select_only([body])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    gun = {i for i, m in enumerate(body.data.materials) if m and m.name in GUN_MATERIALS}
    for p in body.data.polygons:
        p.select = p.material_index in gun
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    rifle = [o for o in bpy.context.selected_objects if o is not body][0]
    rifle.name = "rifle"
    rifle.modifiers.clear()
    rifle.vertex_groups.clear()
    mw = rifle.matrix_world.copy()
    rifle.parent = arm
    rifle.parent_type = "BONE"
    rifle.parent_bone = "hand_r"
    rifle.matrix_world = mw
    for o in (body, rifle):                                 # unused slots out, so the bake sees only what is there
        used = {p.material_index for p in o.data.polygons}
        for i in reversed(range(len(o.data.materials))):
            if i not in used:
                o.data.materials.pop(index=i)
    log(f"{side}: body {tris(body)} triangles, rifle {tris(rifle)} ({src_tris} together)")

    atlas_uv([body, rifle])
    bake_atlas(side, body, rifle, spec.get("fatigue_to"), spec.get("skin"), spec.get("tan"), spec.get("lift"))

    poser = Poser(arm, body, rifle)
    # The support hand's grip and the muzzle, under the rifle.
    hl = arm.matrix_world @ arm.pose.bones["hand_l"].matrix
    grip = bpy.data.objects.new("grip_l", None)
    bpy.context.scene.collection.objects.link(grip)
    grip.matrix_world = hl
    grip.parent = rifle
    grip.matrix_world = hl
    Mh = (arm.matrix_world @ arm.pose.bones["hand_r"].matrix).to_3x3()
    axis = (Mh @ poser.gun_axis_local).normalized()
    tip = max((rifle.matrix_world @ v.co for v in rifle.data.vertices), key=lambda p: p.dot(axis))
    muzzle = bpy.data.objects.new("muzzle", None)
    bpy.context.scene.collection.objects.link(muzzle)
    muzzle.matrix_world = mathutils.Matrix.Translation(tip)
    muzzle.parent = rifle
    muzzle.matrix_world = mathutils.Matrix.Translation(tip)

    # Decimated after the bake: the atlas already holds the full mesh.
    dec = body.modifiers.new("decimate", "DECIMATE")
    dec.ratio = min(1.0, BODY_TRIS / tris(body))
    dec.use_collapse_triangulate = True
    select_only([body])
    bpy.ops.object.modifier_move_to_index(modifier="decimate", index=0)
    bpy.ops.object.modifier_apply(modifier="decimate")

    # One material, and the atlas the only UV map.
    mat = bpy.data.materials.new(f"soldier_{side}")
    for o in (body, rifle):
        o.data.materials.clear()
        o.data.materials.append(mat)
        me = o.data
        for uv in [u for u in me.uv_layers if u.name != "atlas"]:
            me.uv_layers.remove(uv)

    def verts(o):
        # What a GPU is sent: one vertex per distinct (position, UV, normal) corner.
        me = o.data
        uv = me.uv_layers[0].data
        return len({(l.vertex_index, round(uv[l.index].uv[0], 5), round(uv[l.index].uv[1], 5)) for l in me.loops})

    height = max((body.matrix_world @ v.co).z for v in body.data.vertices) - poser.ground
    info = dict(side=side, source=spec["src"], body_tris=tris(body), body_verts=verts(body),
                rifle_tris=tris(rifle), bones=len(arm.data.bones), height_m=round(height, 3),
                muzzle_from_grip_m=round((tip - poser.head("hand_r")).length, 3))
    log(f"{side}: body {info['body_tris']} triangles / {info['body_verts']} vertices after decimation, "
        f"{info['bones']} bones, {height:.2f} m tall")

    path = os.path.join(OUT, f"soldier_{side}.fbx")
    select_only([arm, body, rifle, grip, muzzle], arm)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, add_leaf_bones=False, bake_anim=False,
                             mesh_smooth_type="OFF", use_armature_deform_only=False, path_mode="STRIP")
    log(f"wrote {path}")

    if clips:
        info["clips"] = make_clips(arm, poser)
        cpath = os.path.join(OUT, "soldier_poses.fbx")
        select_only([arm])
        bpy.ops.export_scene.fbx(filepath=cpath, use_selection=True, object_types={"ARMATURE"},
                                 apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, add_leaf_bones=False, bake_anim=True,
                                 bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                                 bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                                 use_armature_deform_only=False)
        log(f"wrote {cpath}")
    with open(os.path.join(OUT, f"soldier_{side}.json"), "w") as f:
        json.dump(info, f, indent=1)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    clips = "--clips" in argv
    sides = [a for a in argv if not a.startswith("--")] or list(SIDES)
    os.makedirs(OUT, exist_ok=True)
    for side in sides:
        build(side, SIDES[side], clips and side == "us_a")


if __name__ == "__main__":
    main()
