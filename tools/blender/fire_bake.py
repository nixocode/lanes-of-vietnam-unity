"""
Fire and smoke, simulated and rendered as frames for the game's effect atlases (PLAN §12.63; the owner, 2026-10-18:
"New fire and smoke images ... baking new ones in Blender").

    Blender -b --factory-startup --python tools/blender/fire_bake.py -- [inferno|billow|all] [--look]

Nothing is downloaded: Blender's own fluid solver (Mantaflow) burns a strip of fuel and the frames are rendered from
the game's side-on view (the camera never turns), each twice:

    glow    the fire alone, its light on black: what is added to the scene (fx_glow.png, LOV/FX Glow)
    smoke   the smoke alone, grey on clear, lit from above and the lens's side: what is drawn over it (fx_smoke.png)

The sims:

    inferno  a burning strip of napalm, two metres wide: flames to three metres, licking and rolling, and the black
             smoke thrown up out of them. 48 frames rendered from the sim's steady part; fx_atlas.py makes them loop.
    billow   the column over a fire: a fatter, slower, heavier smoke rising off a broad hot source, little flame.

Output: SourceArt/fx/<sim>/glow_####.png and smoke_####.png (256 x 256), then tools/art/fx_atlas.py packs them.
--look renders a few frames only, to look at.
"""
import os
import sys

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "SourceArt", "fx")

SIMS = {
    # Domain size (x across the lens, y toward it, z up), metres; the fuel's patch; how hot and how much fuel; the
    # frames rendered (the first, and how many: the sim has settled by then); vorticity for the curl in the flames.
    # (glow: the height of the frame for the fire, metres, from the floor; the smoke's is the whole domain.)
    "inferno": dict(domain=(3.0, 2.0, 6.0), source=(1.4, 1.0), res=128, fuel=3.0, temperature=3.0, smoke=4.0,
                    flame_smoke=2.5, vorticity=0.5, flame_vorticity=1.0, buoyancy=1.2, heat=2.2, burn=0.24, lift=2.0,
                    glow=3.2, first=70, count=48),
    "billow": dict(domain=(4.0, 3.0, 7.0), source=(2.0, 1.4), res=112, fuel=0.6, temperature=1.4, smoke=2.0,
                   flame_smoke=1.6, vorticity=0.25, flame_vorticity=0.5, buoyancy=0.9, heat=1.0, burn=0.6, lift=0.6,
                   glow=3.0, first=90, count=48),
}
FRAME = 256


def log(msg):
    print(f"[fire] {msg}", flush=True)


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build(name, p):
    sc = bpy.context.scene
    dx, dy, dz = p["domain"]
    # The domain: its floor at z 0, centred on x and y.
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, dz / 2))
    dom = bpy.context.object
    dom.name = "domain"
    dom.scale = (dx, dy, dz)
    bpy.ops.object.transform_apply(scale=True)
    mod = dom.modifiers.new("Fluid", "FLUID")
    mod.fluid_type = "DOMAIN"
    ds = mod.domain_settings
    ds.domain_type = "GAS"
    ds.resolution_max = p["res"]
    ds.use_adaptive_domain = False
    ds.vorticity = p["vorticity"]
    ds.alpha = p["buoyancy"] * 0.6        # density's buoyancy
    ds.beta = p["heat"]                   # heat's
    ds.burning_rate = p["burn"]
    ds.flame_smoke = p["flame_smoke"]
    ds.flame_vorticity = p["flame_vorticity"]
    ds.flame_max_temp = 3.0
    ds.flame_ignition = 1.25
    # Open on its sides and its top: smoke goes out of it there rather than piling up against a wall (it stood in a
    # straight-sided column).
    ds.use_collision_border_front = ds.use_collision_border_back = False
    ds.use_collision_border_left = ds.use_collision_border_right = False
    ds.use_collision_border_top = False
    ds.use_collision_border_bottom = True
    ds.use_dissolve_smoke = True
    ds.dissolve_speed = 70
    ds.cache_type = "ALL"
    ds.cache_data_format = "OPENVDB"
    ds.cache_directory = os.path.join(OUT, name, "cache")
    ds.cache_frame_start = 1
    ds.cache_frame_end = p["first"] + p["count"]
    os.makedirs(ds.cache_directory, exist_ok=True)

    # The fuel: a thin slab on the floor, burning, with a little turbulence in what it gives off.
    sx, sy = p["source"]
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.12))
    src = bpy.context.object
    src.name = "fuel"
    src.scale = (sx, sy, 0.16)
    bpy.ops.object.transform_apply(scale=True)
    fm = src.modifiers.new("Fluid", "FLUID")
    fm.fluid_type = "FLOW"
    fs = fm.flow_settings
    fs.flow_type = "BOTH"
    fs.flow_behavior = "INFLOW"
    fs.flow_source = "MESH"
    fs.surface_distance = 0.4
    fs.fuel_amount = p["fuel"]
    fs.temperature = p["temperature"]
    fs.density = p["smoke"]
    fs.smoke_color = (0.04, 0.035, 0.03)
    fs.use_initial_velocity = True
    fs.velocity_normal = p["lift"]
    # A texture on the flow so it does not rise as one sheet.
    tex = bpy.data.textures.new("fuelnoise", "CLOUDS")
    tex.noise_scale = 0.25
    fs.use_texture = True
    fs.noise_texture = tex
    fs.texture_size = 1.0
    src.hide_render = True
    return dom, sc


def bake(dom):
    bpy.context.view_layer.objects.active = dom
    dom.select_set(True)
    log("baking ...")
    with bpy.context.temp_override(object=dom, active_object=dom):
        bpy.ops.fluid.bake_all()
    log("baked")


def material(dom, kind):
    """The domain's look: kind 'glow' (the fire's light only) or 'smoke' (the smoke only)."""
    mat = bpy.data.materials.get(kind) or bpy.data.materials.new(kind)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    if kind == "glow":
        # Emission from the flame field, coloured by its heat as a flame is (deep red where it cools, yellow-white at its
        # heart), no absorption.
        flame = nt.nodes.new("ShaderNodeAttribute"); flame.attribute_name = "flame"
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        cr = ramp.color_ramp
        cr.elements[0].position = 0.0; cr.elements[0].color = (0, 0, 0, 1)
        cr.elements[1].position = 1.0; cr.elements[1].color = (1.0, 0.92, 0.65, 1)
        e = cr.elements.new(0.25); e.color = (0.55, 0.06, 0.0, 1)
        e = cr.elements.new(0.55); e.color = (1.0, 0.42, 0.05, 1)
        strength = nt.nodes.new("ShaderNodeMath"); strength.operation = "MULTIPLY"; strength.inputs[1].default_value = 5.0
        emit = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(flame.outputs["Fac"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], emit.inputs["Color"])
        nt.links.new(flame.outputs["Fac"], strength.inputs[0])
        nt.links.new(strength.outputs["Value"], emit.inputs["Strength"])
        nt.links.new(emit.outputs["Emission"], out.inputs["Volume"])
    else:
        dens = nt.nodes.new("ShaderNodeAttribute"); dens.attribute_name = "density"
        mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = 32.0
        vol = nt.nodes.new("ShaderNodeVolumePrincipled")
        vol.inputs["Color"].default_value = (0.5, 0.5, 0.5, 1)       # mid grey: the game tints it (FX Smoke)
        vol.inputs["Anisotropy"].default_value = 0.2
        nt.links.new(dens.outputs["Fac"], mul.inputs[0])
        nt.links.new(mul.outputs["Value"], vol.inputs["Density"])
        nt.links.new(vol.outputs["Volume"], out.inputs["Volume"])
    dom.data.materials.clear()
    dom.data.materials.append(mat)


def frame(p, kind):
    """The camera for a kind: the fire framed close (its height, from the floor), the smoke the whole of the domain."""
    dx, dy, dz = p["domain"]
    ob = bpy.data.objects["cam"]
    h = p["glow"] if kind == "glow" else max(dx, dz)
    ob.data.ortho_scale = h
    ob.location = (0, -20, h / 2)


def camera(p):
    sc = bpy.context.scene
    cam = bpy.data.cameras.new("cam")
    cam.type = "ORTHO"
    ob = bpy.data.objects.new("cam", cam)
    sc.collection.objects.link(ob)
    # From in front (-y), level.
    ob.rotation_euler = (1.5708, 0, 0)
    sc.camera = ob
    # Light for the smoke: the game's sun is high and behind the lens's left; and the sky's fill.
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = 0.3
    so = bpy.data.objects.new("sun", sun); sc.collection.objects.link(so)
    so.rotation_euler = (0.85, 0.35, -0.6)
    world = bpy.data.worlds.new("w"); sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.55, 0.6, 0.66, 1); bg.inputs["Strength"].default_value = 0.6


def render(name, p, look):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.cycles.volume_bounces = 1
    sc.cycles.volume_step_rate = 1.0
    sc.render.resolution_x = sc.render.resolution_y = FRAME
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "Standard"
    dom = bpy.data.objects["domain"]
    first, count = p["first"], p["count"]
    frames = range(first, first + count) if not look else (first, first + count // 2)
    out_dir = os.path.join(OUT, name)
    for kind in ("glow", "smoke"):
        material(dom, kind)
        frame(p, kind)
        # The fire on black, the smoke on clear.
        sc.render.film_transparent = kind == "smoke"
        sc.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0 if kind == "glow" else 0.6
        bpy.data.objects["sun"].hide_render = kind == "glow"
        for f in frames:
            sc.frame_set(f)
            sc.render.filepath = os.path.join(out_dir, f"{kind}_{f - first:04d}.png")
            bpy.ops.render.render(write_still=True)
        log(f"{name} {kind}: {len(frames)} frames")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    look = "--look" in argv
    names = [a for a in argv if a in SIMS] or (list(SIMS) if "all" in argv or not argv else [])
    for name in names:
        clear()
        p = SIMS[name]
        dom, sc = build(name, p)
        sc.frame_start, sc.frame_end = 1, p["first"] + p["count"]
        bake(dom)
        camera(p)
        render(name, p, look)


main()
