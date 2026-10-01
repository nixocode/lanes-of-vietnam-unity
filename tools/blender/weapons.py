"""
The weapons, built: one for every class on either side.

Both sides had carried the earlier build's one rifle, a box with a rod on it,
whatever the card said they were (the owner, playtests 2 and 3: "more variety
and realism"; "add all the corresponding gun models to each class"). A weapon
in this game is seen from the side, a metre long and a hundred pixels at the
closest, so what it has to have is its silhouette: the M16's carrying handle
and triangular handguard, the AK's curved magazine and wooden furniture, the
M60's bipod and belt box, the PPSh's drum, the scope on a sniper's rifle, the
RPG's cone and warhead. Each part is its side profile, drawn to the weapon's
dimensions, given its thickness and a chamfer; barrels and tubes are turned.

    Blender -b --factory-startup -P tools/blender/weapons.py [-- --preview]

Outputs, in Assets/_Project/Art/Weapons/:
    weapons.fbx          a mesh a weapon, each in its own frame: x along the
                         bore from the butt (0) to the muzzle, z up, the bore
                         on z = 0. Under each, empties: butt, muzzle, up, and
                         where the two wrists go (hold_r under the pistol
                         grip, hold_l under the handguard), so that Unity
                         guesses nothing from the mesh
    weapons_palette.png  one texel a material; every face's UVs sit on its own
    weapons.json         each weapon's length and triangles
--preview also renders them all side on to captures/weapons.png.

Lengths (metres), from the weapons' published specifications: M16A1 0.986,
AK-47 0.880, SKS 1.020, M60 1.105, RPD 1.037, PPSh-41 0.843, M3A1 0.757
(stock out), M40 1.117, Mosin-Nagant 91/30 1.232, M79 0.731, RPG-7 0.950
(1.34 with its round), M19 60 mm mortar 0.819.
"""
import json
import math
import os
import sys

import bmesh
import bpy
import mathutils

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Weapons")

# The palette: linear albedo. A texel each, in this order.
PALETTE = [
    ("steel", (0.040, 0.040, 0.045)),          # parkerised and blued steel
    ("black", (0.020, 0.020, 0.020)),          # the M16's plastic furniture
    ("wood", (0.150, 0.070, 0.028)),           # birch and beech, oiled
    ("walnut", (0.085, 0.040, 0.018)),         # walnut
    ("olive", (0.075, 0.085, 0.040)),          # painted steel: a mortar, a rocket
    ("glass", (0.010, 0.014, 0.020)),          # a scope's lens
    ("brass", (0.300, 0.200, 0.060)),          # a belt of ammunition
    ("canvas", (0.110, 0.105, 0.070)),         # a belt box, a sling
]
STEEL, BLACK, WOOD, WALNUT, OLIVE, GLASS, BRASS, CANVAS = range(8)


def log(msg):
    print(f"[weapons] {msg}", flush=True)


def prism(bm, outline, thick, mat, y0=0.0, chamfer=0.004):
    """A side profile (x, z) as a slab `thick` wide about y0, its long edges chamfered."""
    n = len(outline)
    area = sum(outline[i][0] * outline[(i + 1) % n][1] - outline[(i + 1) % n][0] * outline[i][1] for i in range(n))
    if area < 0:
        outline = outline[::-1]
    cx = sum(p[0] for p in outline) / n
    cz = sum(p[1] for p in outline) / n
    h = thick * 0.5
    c = min(chamfer, h * 0.6)
    rings = []
    # The two faces are a little smaller than the middle, which is the chamfer.
    for y, shrink in ((-h, c), (-h + c, 0.0), (h - c, 0.0), (h, c)):
        ring = []
        for x, z in outline:
            dx, dz = x - cx, z - cz
            d = math.hypot(dx, dz) or 1.0
            ring.append(bm.verts.new((x - dx / d * shrink, y0 + y, z - dz / d * shrink)))
        rings.append(ring)
    faces = []
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            faces.append(bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i])))
    faces.append(bm.faces.new(rings[0][::-1]))
    faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = mat


def tube(bm, x0, x1, r, mat, z=0.0, y=0.0, sides=10, r1=None):
    """A turned part along the bore: from x0 to x1, radius r (to r1, for a cone), at height z."""
    r1 = r if r1 is None else r1
    ts = [2 * math.pi * k / sides for k in range(sides)]
    a = [bm.verts.new((x0, y + r * math.cos(t), z + r * math.sin(t))) for t in ts]
    b = [bm.verts.new((x1, y + r1 * math.cos(t), z + r1 * math.sin(t))) for t in ts]
    faces = [bm.faces.new((a[i], b[i], b[(i + 1) % sides], a[(i + 1) % sides])) for i in range(sides)]
    faces.append(bm.faces.new(a))
    faces.append(bm.faces.new(b[::-1]))
    for f in faces:
        f.material_index = mat
        f.smooth = True


def rod(bm, p, q, r, mat, sides=6):
    """A thin round rod between two points (x, y, z): a bipod leg, a wire stock."""
    p, q = mathutils.Vector(p), mathutils.Vector(q)
    ax = (q - p).normalized()
    side = ax.cross(mathutils.Vector((0, 1, 0)))
    if side.length < 1e-3:
        side = ax.cross(mathutils.Vector((0, 0, 1)))
    side.normalize()
    up = ax.cross(side)
    ring = lambda c: [bm.verts.new(c + (side * math.cos(t) + up * math.sin(t)) * r) for t in (2 * math.pi * k / sides for k in range(sides))]
    a, b = ring(p), ring(q)
    faces = [bm.faces.new((a[i], b[i], b[(i + 1) % sides], a[(i + 1) % sides])) for i in range(sides)]
    faces.append(bm.faces.new(a))
    faces.append(bm.faces.new(b[::-1]))
    for f in faces:
        f.material_index = mat
        f.smooth = True


def scope(bm, x0, x1, z, r=0.013):
    """A telescopic sight on two rings."""
    tube(bm, x0, x1, r, STEEL, z=z)
    tube(bm, x0 - 0.03, x0, r * 1.35, STEEL, z=z)
    tube(bm, x1, x1 + 0.035, r * 1.45, STEEL, z=z)
    tube(bm, x1 + 0.033, x1 + 0.036, r * 1.3, GLASS, z=z)
    for x in (x0 + 0.03, x1 - 0.03):
        prism(bm, [(x - 0.008, z - r - 0.016), (x - 0.008, z), (x + 0.008, z), (x + 0.008, z - r - 0.016)], 0.014, STEEL, chamfer=0.002)


# --- the weapons: each returns its points -------------------------------------------

def m16(bm):
    prism(bm, [(0.0, -0.112), (0.0, 0.018), (0.012, 0.028), (0.262, 0.030), (0.262, -0.034)], 0.040, BLACK)
    prism(bm, [(0.262, -0.036), (0.262, 0.032), (0.468, 0.032), (0.468, -0.020), (0.438, -0.036), (0.432, -0.066), (0.372, -0.066), (0.366, -0.036)], 0.046, STEEL)
    # The carrying handle: two posts and the bar with the rear sight.
    prism(bm, [(0.288, 0.032), (0.296, 0.074), (0.312, 0.074), (0.318, 0.032)], 0.030, STEEL)
    prism(bm, [(0.405, 0.032), (0.412, 0.066), (0.428, 0.066), (0.440, 0.032)], 0.030, STEEL)
    prism(bm, [(0.292, 0.066), (0.292, 0.084), (0.330, 0.084), (0.432, 0.074), (0.432, 0.060), (0.330, 0.066)], 0.030, STEEL)
    prism(bm, [(0.300, -0.036), (0.338, -0.036), (0.306, -0.134), (0.266, -0.126)], 0.030, BLACK)
    # The twenty-round magazine: straight.
    prism(bm, [(0.376, -0.060), (0.428, -0.060), (0.420, -0.158), (0.368, -0.154)], 0.022, STEEL)
    # The triangular handguard, tapering to the front sight.
    prism(bm, [(0.468, -0.044), (0.468, 0.036), (0.772, 0.027), (0.772, -0.030)], 0.058, BLACK, chamfer=0.012)
    prism(bm, [(0.772, -0.016), (0.772, 0.014), (0.782, 0.074), (0.796, 0.074), (0.816, 0.014), (0.816, -0.016)], 0.014, STEEL, chamfer=0.002)
    tube(bm, 0.772, 0.948, 0.0085, STEEL)
    tube(bm, 0.940, 0.986, 0.0115, STEEL)
    return dict(muzzle=0.986, hold_r=(0.268, -0.100), hold_l=(0.585, -0.062))


def ak(bm):
    prism(bm, [(0.0, -0.118), (0.0, -0.012), (0.018, -0.002), (0.246, 0.006), (0.246, -0.046)], 0.038, WOOD)
    prism(bm, [(0.246, -0.048), (0.246, 0.008), (0.262, 0.024), (0.452, 0.024), (0.470, 0.010), (0.470, -0.048)], 0.036, STEEL)
    prism(bm, [(0.470, 0.004), (0.470, 0.034), (0.500, 0.030), (0.512, 0.008)], 0.026, STEEL, chamfer=0.002)
    prism(bm, [(0.276, -0.048), (0.312, -0.048), (0.288, -0.140), (0.250, -0.132)], 0.028, WOOD)
    prism(bm, [(0.312, -0.048), (0.350, -0.048), (0.350, -0.056), (0.330, -0.078), (0.314, -0.074)], 0.010, STEEL, chamfer=0.002)
    # The thirty-round magazine: curved forward.
    prism(bm, [(0.350, -0.046), (0.404, -0.046), (0.432, -0.104), (0.468, -0.176), (0.424, -0.200), (0.392, -0.132), (0.372, -0.090)], 0.026, STEEL)
    prism(bm, [(0.470, -0.044), (0.470, -0.006), (0.628, -0.006), (0.628, -0.036)], 0.046, WOOD, chamfer=0.008)
    prism(bm, [(0.512, 0.012), (0.512, 0.040), (0.616, 0.040), (0.616, 0.012)], 0.034, WOOD, chamfer=0.008)
    tube(bm, 0.616, 0.706, 0.0080, STEEL, z=0.028)
    prism(bm, [(0.698, -0.012), (0.698, 0.038), (0.724, 0.030), (0.724, -0.012)], 0.022, STEEL, chamfer=0.002)
    prism(bm, [(0.800, -0.012), (0.800, 0.012), (0.806, 0.066), (0.820, 0.066), (0.830, 0.012), (0.830, -0.012)], 0.014, STEEL, chamfer=0.002)
    tube(bm, 0.470, 0.862, 0.0082, STEEL)
    tube(bm, 0.856, 0.880, 0.0105, STEEL)
    return dict(muzzle=0.880, hold_r=(0.250, -0.104), hold_l=(0.535, -0.058))


def sks(bm):
    # One long wooden stock from the butt to halfway up the barrel, a fixed ten-round box, the folded bayonet under it.
    prism(bm, [(0.0, -0.110), (0.0, -0.006), (0.020, 0.004), (0.300, 0.004), (0.330, -0.018), (0.700, -0.014), (0.700, -0.046), (0.360, -0.052), (0.300, -0.060), (0.262, -0.104), (0.236, -0.096), (0.262, -0.048)], 0.042, WOOD, chamfer=0.008)
    prism(bm, [(0.300, 0.000), (0.300, 0.030), (0.520, 0.030), (0.540, 0.012), (0.540, 0.000)], 0.034, STEEL)
    prism(bm, [(0.392, -0.052), (0.470, -0.052), (0.462, -0.098), (0.400, -0.098)], 0.024, STEEL)
    prism(bm, [(0.540, 0.010), (0.540, 0.036), (0.700, 0.034), (0.700, 0.010)], 0.032, WOOD, chamfer=0.008)
    tube(bm, 0.700, 0.770, 0.0078, STEEL, z=0.024)
    prism(bm, [(0.930, -0.010), (0.930, 0.010), (0.936, 0.058), (0.950, 0.058), (0.958, 0.010), (0.958, -0.010)], 0.014, STEEL, chamfer=0.002)
    tube(bm, 0.540, 1.020, 0.0080, STEEL)
    rod(bm, (0.720, 0, -0.022), (0.960, 0, -0.018), 0.0035, STEEL)                 # the bayonet, folded back
    return dict(muzzle=1.020, hold_r=(0.240, -0.086), hold_l=(0.560, -0.060))


def m60(bm):
    prism(bm, [(0.0, -0.090), (0.0, 0.030), (0.200, 0.036), (0.200, -0.040)], 0.050, BLACK, chamfer=0.010)
    prism(bm, [(0.200, -0.050), (0.200, 0.044), (0.560, 0.044), (0.560, -0.030), (0.480, -0.050)], 0.052, STEEL)
    prism(bm, [(0.330, 0.044), (0.340, 0.062), (0.520, 0.062), (0.530, 0.044)], 0.050, STEEL)      # the feed cover
    prism(bm, [(0.250, -0.050), (0.286, -0.050), (0.262, -0.140), (0.226, -0.134)], 0.030, BLACK)
    # The belt box on the left, and a few rounds going in.
    prism(bm, [(0.360, -0.130), (0.360, -0.020), (0.470, -0.020), (0.470, -0.130)], 0.070, CANVAS, y0=-0.058, chamfer=0.008)
    prism(bm, [(0.372, -0.020), (0.372, 0.030), (0.458, 0.030), (0.458, -0.020)], 0.020, BRASS, y0=-0.044, chamfer=0.002)
    prism(bm, [(0.560, -0.040), (0.560, 0.030), (0.800, 0.024), (0.800, -0.030)], 0.056, BLACK, chamfer=0.012)
    tube(bm, 0.560, 0.840, 0.0085, STEEL, z=-0.036)                                # the gas cylinder
    tube(bm, 0.560, 1.060, 0.0115, STEEL)
    tube(bm, 1.050, 1.105, 0.0150, STEEL)
    prism(bm, [(1.010, -0.010), (1.010, 0.012), (1.016, 0.058), (1.030, 0.058), (1.038, 0.012), (1.038, -0.010)], 0.014, STEEL, chamfer=0.002)
    rod(bm, (0.520, 0, 0.062), (0.640, 0, 0.100), 0.006, STEEL)                    # carrying handle
    for y in (-0.05, 0.05):                                                         # the bipod, folded back along the barrel
        rod(bm, (0.980, y * 0.3, -0.016), (0.700, y, -0.060), 0.0045, STEEL)
    return dict(muzzle=1.105, hold_r=(0.222, -0.104), hold_l=(0.640, -0.066))


def rpd(bm):
    prism(bm, [(0.0, -0.104), (0.0, -0.004), (0.020, 0.008), (0.230, 0.010), (0.230, -0.044)], 0.040, WOOD)
    prism(bm, [(0.230, -0.050), (0.230, 0.030), (0.560, 0.030), (0.560, -0.036), (0.470, -0.050)], 0.044, STEEL)
    prism(bm, [(0.262, -0.050), (0.298, -0.050), (0.274, -0.138), (0.238, -0.130)], 0.028, WOOD)
    # The drum under the receiver: what an RPD is known by.
    tube(bm, -0.034, 0.034, 0.066, STEEL, sides=14)
    for v in list(bm.verts)[-28:]:                                                   # turn the drum to lie across the gun
        x, y, z = v.co
        v.co = (0.410 + y, x, -0.112 + z)
    prism(bm, [(0.560, -0.040), (0.560, 0.020), (0.730, 0.016), (0.730, -0.034)], 0.046, WOOD, chamfer=0.010)
    tube(bm, 0.560, 0.800, 0.0080, STEEL, z=-0.030)
    tube(bm, 0.560, 1.010, 0.0100, STEEL)
    tube(bm, 1.004, 1.037, 0.0120, STEEL)
    prism(bm, [(0.960, -0.010), (0.960, 0.010), (0.966, 0.056), (0.980, 0.056), (0.988, 0.010), (0.988, -0.010)], 0.014, STEEL, chamfer=0.002)
    for y in (-0.05, 0.05):
        rod(bm, (0.940, y * 0.3, -0.014), (0.700, y, -0.058), 0.0045, STEEL)
    return dict(muzzle=1.037, hold_r=(0.236, -0.100), hold_l=(0.640, -0.062))


def ppsh(bm):
    prism(bm, [(0.0, -0.112), (0.0, -0.010), (0.020, 0.000), (0.300, 0.004), (0.330, -0.016), (0.470, -0.014), (0.470, -0.044), (0.340, -0.050), (0.300, -0.058), (0.262, -0.102), (0.236, -0.094), (0.262, -0.046)], 0.040, WOOD, chamfer=0.008)
    prism(bm, [(0.290, 0.000), (0.290, 0.034), (0.560, 0.034), (0.560, 0.000)], 0.036, STEEL)
    # The perforated jacket, to the muzzle, cut on the slant.
    tube(bm, 0.470, 0.800, 0.0190, STEEL, sides=12)
    prism(bm, [(0.800, -0.019), (0.800, 0.019), (0.843, 0.019), (0.826, -0.019)], 0.036, STEEL, chamfer=0.006)
    # The seventy-one-round drum.
    tube(bm, -0.030, 0.030, 0.062, STEEL, sides=14)
    for v in list(bm.verts)[-28:]:
        x, y, z = v.co
        v.co = (0.420 + y, x, -0.090 + z)
    return dict(muzzle=0.843, hold_r=(0.240, -0.088), hold_l=(0.500, -0.050))


def m3(bm):
    # The "grease gun": a tube for a receiver, a wire stock, a straight magazine that is also the forward grip.
    tube(bm, 0.300, 0.560, 0.0260, STEEL, sides=12)
    tube(bm, 0.560, 0.757, 0.0095, STEEL)
    prism(bm, [(0.320, -0.026), (0.356, -0.026), (0.334, -0.120), (0.298, -0.114)], 0.028, STEEL)
    prism(bm, [(0.430, -0.026), (0.474, -0.026), (0.470, -0.200), (0.428, -0.200)], 0.026, STEEL)
    for y in (-0.020, 0.020):
        rod(bm, (0.300, y, 0.004), (0.012, y, 0.004), 0.0040, STEEL)
    rod(bm, (0.012, -0.020, 0.004), (0.012, 0.020, 0.004), 0.0040, STEEL)
    rod(bm, (0.012, 0, 0.004), (0.012, 0, -0.090), 0.0040, STEEL)
    return dict(muzzle=0.757, hold_r=(0.286, -0.086), hold_l=(0.450, -0.150))


def m40(bm):
    # A hunting rifle's walnut stock, a heavy barrel, the scope.
    prism(bm, [(0.0, -0.118), (0.0, -0.014), (0.016, -0.004), (0.250, 0.010), (0.330, 0.006), (0.360, -0.016), (0.760, -0.014), (0.760, -0.042), (0.380, -0.050), (0.330, -0.060), (0.300, -0.112), (0.270, -0.104), (0.286, -0.050)], 0.046, WALNUT, chamfer=0.010)
    prism(bm, [(0.330, 0.000), (0.330, 0.026), (0.560, 0.026), (0.560, 0.000)], 0.032, STEEL)
    rod(bm, (0.345, 0.020, 0.012), (0.345, 0.050, -0.030), 0.005, STEEL)             # the bolt handle
    tube(bm, 0.560, 1.117, 0.0105, STEEL, r1=0.0090)
    scope(bm, 0.340, 0.580, 0.062)
    return dict(muzzle=1.117, hold_r=(0.276, -0.092), hold_l=(0.600, -0.060))


def mosin(bm):
    # Long, straight, wood nearly to the muzzle, a PU scope on the left.
    prism(bm, [(0.0, -0.108), (0.0, -0.008), (0.018, 0.002), (0.300, 0.006), (0.330, -0.014), (1.060, -0.008), (1.060, -0.036), (0.360, -0.050), (0.300, -0.058), (0.262, -0.100), (0.238, -0.092), (0.262, -0.046)], 0.040, WOOD, chamfer=0.008)
    prism(bm, [(0.300, 0.000), (0.300, 0.026), (0.540, 0.026), (0.540, 0.000)], 0.030, STEEL)
    prism(bm, [(0.400, -0.050), (0.440, -0.050), (0.436, -0.086), (0.404, -0.082)], 0.022, STEEL)
    rod(bm, (0.330, 0.018, 0.012), (0.330, 0.046, -0.034), 0.005, STEEL)
    prism(bm, [(0.540, -0.006), (0.540, 0.020), (0.860, 0.016), (0.860, -0.006)], 0.030, WOOD, chamfer=0.008)
    tube(bm, 0.540, 1.232, 0.0080, STEEL)
    prism(bm, [(1.180, -0.008), (1.180, 0.010), (1.186, 0.040), (1.198, 0.040), (1.204, 0.010), (1.204, -0.008)], 0.012, STEEL, chamfer=0.002)
    scope(bm, 0.340, 0.520, 0.058, r=0.011)
    return dict(muzzle=1.232, hold_r=(0.244, -0.086), hold_l=(0.620, -0.056))


def m79(bm):
    # A break-open single shot: a short fat barrel on a rifle's butt.
    prism(bm, [(0.0, -0.112), (0.0, -0.004), (0.020, 0.008), (0.250, 0.012), (0.330, 0.008), (0.360, -0.020), (0.360, -0.050), (0.320, -0.060), (0.290, -0.110), (0.262, -0.102), (0.280, -0.050)], 0.046, WALNUT, chamfer=0.010)
    prism(bm, [(0.330, -0.030), (0.330, 0.030), (0.400, 0.030), (0.400, -0.030)], 0.050, STEEL)
    tube(bm, 0.400, 0.731, 0.0240, STEEL, sides=12)
    prism(bm, [(0.400, -0.052), (0.400, -0.024), (0.600, -0.024), (0.600, -0.046)], 0.040, WALNUT, chamfer=0.008)
    prism(bm, [(0.520, 0.024), (0.520, 0.070), (0.532, 0.070), (0.540, 0.024)], 0.020, STEEL, chamfer=0.002)   # the leaf sight, up
    return dict(muzzle=0.731, hold_r=(0.268, -0.090), hold_l=(0.500, -0.066))


def rpg(bm):
    # The tube with its wooden heat guard, the flared venturi behind, and the round in the muzzle.
    tube(bm, 0.170, 0.950, 0.0210, STEEL, sides=12)
    tube(bm, 0.0, 0.170, 0.0460, STEEL, sides=12, r1=0.0220)
    tube(bm, 0.300, 0.620, 0.0270, WOOD, sides=12)
    prism(bm, [(0.640, -0.021), (0.676, -0.021), (0.652, -0.120), (0.616, -0.112)], 0.028, WOOD)
    prism(bm, [(0.470, -0.021), (0.500, -0.021), (0.482, -0.100), (0.452, -0.094)], 0.026, WOOD)
    prism(bm, [(0.600, 0.020), (0.606, 0.070), (0.700, 0.070), (0.706, 0.020)], 0.022, STEEL, chamfer=0.003)   # the optical sight
    # The PG-7 round: the booster, then the warhead and its long nose.
    tube(bm, 0.950, 1.020, 0.0160, OLIVE, sides=10)
    tube(bm, 1.020, 1.110, 0.0160, OLIVE, sides=12, r1=0.0425)
    tube(bm, 1.110, 1.220, 0.0425, OLIVE, sides=12)
    tube(bm, 1.220, 1.340, 0.0425, OLIVE, sides=12, r1=0.0060)
    # The right hand takes the rear grip here (the trigger is on the front one), so that the
    # tube lies along his two hands the way a rifle does, and comes back over his shoulder.
    return dict(muzzle=1.340, hold_r=(0.462, -0.072), hold_l=(0.630, -0.086))


def mortar(bm):
    # The M19's tube as it is carried: tube, base cap and the small plate, no bipod.
    tube(bm, 0.050, 0.819, 0.0380, OLIVE, sides=12)
    tube(bm, 0.812, 0.819, 0.0400, STEEL, sides=12)
    tube(bm, 0.0, 0.050, 0.0260, OLIVE, sides=10, r1=0.0380)
    prism(bm, [(-0.010, -0.090), (-0.010, 0.090), (0.012, 0.090), (0.012, -0.090)], 0.200, OLIVE, chamfer=0.010)
    rod(bm, (0.300, 0, -0.038), (0.520, 0, -0.038), 0.006, CANVAS)                  # the carrying sling's keeper
    return dict(muzzle=0.819, hold_r=(0.260, -0.060), hold_l=(0.560, -0.060))


WEAPONS = [("m16", m16), ("ak", ak), ("sks", sks), ("m60", m60), ("rpd", rpd), ("ppsh", ppsh), ("m3", m3),
           ("m40", m40), ("mosin", mosin), ("m79", m79), ("rpg", rpg), ("mortar", mortar)]


def build(name, make, mat):
    bm = bmesh.new()
    pts = make(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = ((f.material_index + 0.5) / len(PALETTE), 0.5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    # The two wrists make a line 3.5 degrees off the bore (the support hand the higher), on every
    # weapon: that is the line Mixamo's hands make, and the bore then lies where the man is aiming.
    (rx, rz), lx = pts["hold_r"], pts["hold_l"][0]
    points = dict(butt=(0, 0, 0), muzzle=(pts["muzzle"], 0, 0), up=(0, 0, 0.1),
                  hold_r=(rx, 0, rz), hold_l=(lx, 0, rz + 0.06 * (lx - rx)))
    for key, at in points.items():
        e = bpy.data.objects.new(f"{name}.{key}", None)
        bpy.context.scene.collection.objects.link(e)
        e.empty_display_size = 0.02
        e.parent = obj
        e.location = at
    return obj, pts


def to_srgb(c):
    return [x * 12.92 if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055 for x in c]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)

    # The palette, and a material that reads it (for the preview; Unity makes its own).
    img = bpy.data.images.new("weapons_palette", len(PALETTE), 1, alpha=False)
    px = []
    for _, c in PALETTE:
        px += [*to_srgb(c), 1.0]
    img.pixels = px
    img.filepath_raw = os.path.join(OUT, "weapons_palette.png")
    img.file_format = "PNG"
    img.save()
    mat = bpy.data.materials.new("weapons")
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55

    info, objs = {}, []
    for i, (name, make) in enumerate(WEAPONS):
        obj, pts = build(name, make, mat)
        objs.append(obj)
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        info[name] = dict(length=pts["muzzle"], triangles=tris)
        log(f"{name}: {pts['muzzle']:.3f} m, {tris} triangles")

    if "--preview" in argv:
        for i, o in enumerate(objs):
            o.location = ((i % 3) * 1.45, 0, -(i // 3) * 0.42)
        sc = bpy.context.scene
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = 4.5
        cam.location = (2.1, -5, -0.68)
        cam.rotation_euler = (math.radians(90), 0, 0)
        sc.camera = cam
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sc.collection.objects.link(sun)
        sun.data.energy = 5
        sun.rotation_euler = (math.radians(60), 0, math.radians(-20))
        sc.world = bpy.data.worlds.new("w")
        sc.world.use_nodes = True
        sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.60, 0.64, 1)
        sc.world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
        sc.render.resolution_x, sc.render.resolution_y = 2000, 760
        sc.view_settings.view_transform = "Standard"
        sc.render.filepath = os.path.join(ROOT, "captures", "weapons.png")
        for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
            try:
                sc.render.engine = engine
                break
            except TypeError:
                continue
        bpy.ops.render.render(write_still=True)
        for o in objs:
            o.location = (0, 0, 0)

    bpy.context.view_layer.update()                            # objects linked since the last update are not in the layer until now
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs or o.parent in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "weapons.fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             bake_anim=False, mesh_smooth_type="FACE", path_mode="STRIP")
    with open(os.path.join(OUT, "weapons.json"), "w") as f:
        json.dump(dict(palette=[n for n, _ in PALETTE], weapons=info), f, indent=1)
    log(f"wrote {OUT}/weapons.fbx")


if __name__ == "__main__":
    main()
