# Builds the Drone FPV quadcopter in its own Blender scene ("DroneFPV") and exports drone.obj/.mtl.
# Run inside Blender (sent over the Blender MCP socket). Units: metres. Nose points -Y (Blender front),
# which ends up as +Z (forward) in Unity after the OBJ "Y up / -Z forward" export.
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

OUT = r"C:\Users\User\Documents\typing-test\planet-crafter-drone-fpv\assets"

scene = bpy.data.scenes.get("DroneFPV") or bpy.data.scenes.new("DroneFPV")
# start clean, but only inside our own scene
for o in list(scene.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for c in list(scene.collection.children):
    scene.collection.children.unlink(c)
scene.unit_settings.system = 'METRIC'

def mat(name, rgb, metal=0.0, rough=0.5, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit > 0:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*rgb, 1)
    return m

M = {
    "Hull":      mat("Hull", (0.82, 0.84, 0.86), 0.1, 0.35),
    "Accent":    mat("Accent", (0.95, 0.38, 0.04), 0.0, 0.4),
    "Carbon":    mat("Carbon", (0.035, 0.037, 0.042), 0.2, 0.55),
    "Metal":     mat("Metal", (0.55, 0.57, 0.6), 0.9, 0.25),
    "PropBlade": mat("PropBlade", (0.06, 0.065, 0.075), 0.0, 0.3),
    "Lens":      mat("Lens", (0.02, 0.06, 0.12), 0.6, 0.05),
    "Battery":   mat("Battery", (0.95, 0.78, 0.1), 0.0, 0.45),
    "LED_Green": mat("LED_Green", (0.1, 1.0, 0.35), 0, 0.3, 6),
    "LED_Red":   mat("LED_Red", (1.0, 0.08, 0.04), 0, 0.3, 6),
}

def link(obj, material):
    scene.collection.objects.link(obj)
    if material:
        obj.data.materials.clear()
        for m in (material if isinstance(material, list) else [material]):
            obj.data.materials.append(M[m])
    return obj

def mesh_obj(name, build, material, loc=(0, 0, 0), rot=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    o.location = loc
    o.rotation_euler = rot
    return link(o, material)

def box(sx, sy, sz):
    def b(bm):
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    return b

def cyl(r, h, seg=24, r2=None):
    def b(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    return b

def sphere(r):
    def b(bm):
        bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=r)
    return b

def bevel(o, w, seg=3):
    md = o.modifiers.new("Bevel", 'BEVEL')
    md.width = w; md.segments = seg; md.limit_method = 'ANGLE'
    o.modifiers.new("Smooth", 'WEIGHTED_NORMAL') if hasattr(bpy.types, "WeightedNormalModifier") else None
    return o

def shade_smooth(o):
    for p in o.data.polygons:
        p.use_smooth = True
    return o

# ---- hull ----
hull = bevel(mesh_obj("hull", box(0.15, 0.27, 0.055), "Hull", (0, 0, 0)), 0.018)
canopy = shade_smooth(bevel(mesh_obj("canopy", box(0.11, 0.17, 0.04), "Accent", (0, 0.015, 0.042)), 0.016, 4))
belly = bevel(mesh_obj("belly", box(0.12, 0.22, 0.025), "Carbon", (0, 0, -0.038)), 0.008)
stripe = mesh_obj("stripe", box(0.152, 0.03, 0.057), "Accent", (0, 0.07, 0))
# battery strapped on top
batt = bevel(mesh_obj("battery", box(0.075, 0.12, 0.035), "Battery", (0, 0.06, 0.078)), 0.006)
strap = mesh_obj("strap", box(0.085, 0.018, 0.04), "Carbon", (0, 0.06, 0.08))

# ---- camera pod at the nose, tilted up 20 degrees like a real FPV cam ----
pod = bevel(mesh_obj("campod", box(0.07, 0.06, 0.055), "Carbon", (0, -0.15, 0.005), (math.radians(-20), 0, 0)), 0.01)
lens = shade_smooth(mesh_obj("lens", cyl(0.02, 0.03, 32), "Lens", (0, -0.185, 0.015), (math.radians(90 - 20), 0, 0)))
lensring = mesh_obj("lensring", cyl(0.026, 0.012, 32), "Metal", (0, -0.176, 0.012), (math.radians(90 - 20), 0, 0))

# ---- arms, motors, guards, props (X layout) ----
D = 0.235
corners = {"FR": (D, -D), "FL": (-D, -D), "BR": (D, D), "BL": (-D, D)}
for key, (x, y) in corners.items():
    ang = math.atan2(y, x)
    L = math.hypot(x, y)
    arm = bevel(mesh_obj("arm_" + key, box(L, 0.032, 0.02), "Carbon", (x * 0.5, y * 0.5, 0.0), (0, 0, ang)), 0.006)
    mesh_obj("motor_" + key, cyl(0.028, 0.032), "Metal", (x, y, 0.022))
    mesh_obj("motorcap_" + key, cyl(0.02, 0.01, 24, 0.012), "Accent", (x, y, 0.043))
    # prop guard ring (torus)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.128, minor_radius=0.007, major_segments=48, minor_segments=8,
                                     location=(x, y, 0.05))
    g = bpy.context.active_object
    # primitive ops add to the active scene: move it into ours
    for c in g.users_collection: c.objects.unlink(g)
    scene.collection.objects.link(g)
    g.name = "guard_" + key
    g.data.materials.clear(); g.data.materials.append(M["Accent"])
    shade_smooth(g)
    # two struts holding the guard
    for s in (1, -1):
        a2 = ang + s * math.radians(70)
        mesh_obj("guardstrut_%s_%d" % (key, s), box(0.12, 0.008, 0.006), "Carbon",
                 (x + math.cos(a2) * 0.068, y + math.sin(a2) * 0.068, 0.048), (0, 0, a2))
    # two-blade prop with a little twist; its bounds centre is the motor axis, so it spins in place
    def prop(bm):
        for side in (1, -1):
            r = bmesh.ops.create_cube(bm, size=1.0)
            vs = r["verts"]
            bmesh.ops.scale(bm, vec=(0.11, 0.022, 0.003), verts=vs)
            bmesh.ops.translate(bm, vec=(side * 0.06, 0, 0), verts=vs)
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(12 * side), 3, 'X'), verts=vs)
        hub = bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.012, radius2=0.012, depth=0.012)
    mesh_obj("prop_" + key, prop, "PropBlade", (x, y, 0.052), (0, 0, math.radians(30 if key in ("FR", "BL") else -30)))
    # landing leg
    mesh_obj("leg_" + key, cyl(0.007, 0.05, 12), "Carbon", (x * 0.62, y * 0.62, -0.05))
    mesh_obj("foot_" + key, sphere(0.012), "Accent", (x * 0.62, y * 0.62, -0.076))
    # nav LEDs under the motors: green at the front, red at the back
    mesh_obj("led_" + key, sphere(0.011), "LED_Green" if y < 0 else "LED_Red", (x, y, -0.003))

# ---- antennas at the back ----
for s in (1, -1):
    mesh_obj("antenna_%d" % s, cyl(0.004, 0.12, 8), "Carbon", (s * 0.04, 0.14, 0.07), (math.radians(-25), s * math.radians(18), 0))
    mesh_obj("antennatip_%d" % s, sphere(0.009), "Accent", (s * 0.058, 0.165, 0.123))

# preview camera + light in our scene only
cam_data = bpy.data.cameras.get("DroneFPV_cam") or bpy.data.cameras.new("DroneFPV_cam")
cam = bpy.data.objects.new("DroneFPV_cam", cam_data)
scene.collection.objects.link(cam)
cam.location = (0.75, -0.85, 0.55)
direction = Vector((0, 0, 0.02)) - cam.location
cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 50
scene.camera = cam
ld = bpy.data.lights.get("DroneFPV_sun") or bpy.data.lights.new("DroneFPV_sun", 'SUN')
ld.energy = 3.5
sun = bpy.data.objects.new("DroneFPV_sun", ld)
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
scene.collection.objects.link(sun)
world = bpy.data.worlds.get("DroneFPV_world") or bpy.data.worlds.new("DroneFPV_world")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.42, 0.5, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
scene.world = world

# show our scene in the open window so the user sees the model
bpy.context.window.scene = scene

meshes = [o for o in scene.objects if o.type == 'MESH']
mn = Vector((1e9, 1e9, 1e9)); mx = -mn
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))

result = {"objects": len(meshes), "size_m": [round(v, 3) for v in (mx - mn)], "scene": scene.name}
