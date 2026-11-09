# Generates the game models as FBX into Assets/Resources/Models.
# Run (Unity can stay open, it imports the files on focus):
#   "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Blender/make_models.py
# Rails, cushions, felt and pockets are NOT models: they must merge seamlessly, so LevelBuilder generates them.
# Size/pivot don't matter: LevelBuilder fits every model into its slot. Material NAMES matter (Unity colors them by name).
#
# Art style: LOW POLY / STYLIZED (see CLAUDE.md "Stile grafico"): flat shading, round things 8-16 sides,
# boxes with a single chamfer, no textures (flat colors from LevelBuilder.Palette; balls are the only exception).
import bpy, bmesh, os, math

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Models"))
os.makedirs(OUT, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def flat(obj):
    for p in obj.data.polygons:
        p.use_smooth = False
    return obj


def export(obj, name):
    flat(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE")
    print("exported", name)


def mesh_object(name, verts, faces):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    return objs[0]


def lathe(name, profile, mats, segments=8):
    # profile: (z, radius, material index of the band ending at this ring); low poly = few segments, flat
    verts, faces, face_mats = [], [], []
    for z, r, _ in profile:
        for s in range(segments):
            a = 2 * math.pi * (s + 0.5) / segments  # half-step offset: flat sides face the axes
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for k in range(1, len(profile)):
        for s in range(segments):
            a, b = (k - 1) * segments + s, (k - 1) * segments + (s + 1) % segments
            faces.append((a, b, b + segments, a + segments))
            face_mats.append(profile[k][2])
    faces.append(tuple(reversed(range(segments))))
    face_mats.append(profile[0][2])
    last = (len(profile) - 1) * segments
    faces.append(tuple(range(last, last + segments)))
    face_mats.append(profile[-1][2])
    obj = mesh_object(name, verts, faces)
    for m in mats:
        obj.data.materials.append(material(m))
    for poly, mi in zip(obj.data.polygons, face_mats):
        poly.material_index = mi
    return flat(obj)


def chamfer_box(name, size, chamfer, side_mat, top_mat=None, location=(0, 0, 0)):
    # box with one bevel segment on every edge; faces pointing up get top_mat
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(scale=True)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=chamfer, segments=1, profile=0.5, affect="EDGES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.materials.append(material(side_mat))
    if top_mat:
        obj.data.materials.append(material(top_mat))
        for p in obj.data.polygons:
            p.material_index = 1 if p.normal.z > 0.99 else 0
    return flat(obj)


def cue():
    # real cue: 147 cm, tip 13 mm, butt 30 mm; tip at the top (+Z)
    reset()
    L = 1.47
    profile = [
        (0.0, 0.0050, 0), (-0.002, 0.0065, 0), (-0.012, 0.0065, 0),   # chalked tip
        (-0.035, 0.0066, 1),                                         # ferrule
        (-0.80, 0.0095, 2),                                          # maple shaft
        (-1.00, 0.0110, 3),                                          # forearm
        (-1.30, 0.0135, 4),                                          # wrap
        (-1.46, 0.0150, 3), (-L, 0.0140, 3),                         # butt
    ]
    obj = lathe("Cue", profile, ["CueTip", "CueFerrule", "CueWood", "CueButt", "CueWrap"], segments=8)
    export(obj, "Cue")


def ball():
    # low-poly UV sphere: keeps the equirectangular UVs BallArt paints on
    reset()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.5)
    obj = bpy.context.active_object
    obj.data.materials.append(material("Ball"))
    export(obj, "Ball")


def pillar():
    # solid block filling a whole tile
    reset()
    export(chamfer_box("Pillar", (1, 1, 1), 0.06, "Block", "BlockTop"), "Pillar")


def spikes():
    reset()
    parts = [chamfer_box("Spikes", (1, 1, 0.12), 0.03, "SpikeBase")]
    for i in range(3):
        for j in range(3):
            bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.16, radius2=0, depth=0.7,
                                            location=((i - 1) * 0.3, (j - 1) * 0.3, 0.06 + 0.35), rotation=(0, 0, math.pi / 4))
            cone = bpy.context.active_object
            cone.data.materials.append(material("Metal"))
            parts.append(cone)
    export(join(parts, "Spikes"), "Spikes")


def fan():
    # jump tile ('J'): octagonal housing open on top, dark duct inside; the blades are a separate model (Unity spins them)
    reset()
    profile = [(0, 0.5, 0), (0.3, 0.5, 0), (0.3, 0.42, 0), (0.05, 0.42, 1)]
    export(lathe("Fan", profile, ["FanHousing", "FanDuct"], segments=8), "Fan")


def fan_blades():
    # 4 tilted blades around an octagonal hub, spinning around Z (= Unity Y)
    reset()
    parts = [lathe("Hub", [(0, 0.08, 0), (0.06, 0.08, 0), (0.08, 0.05, 0)], ["FanHub"], segments=8)]
    for i in range(4):
        a = i * math.pi / 2
        blade = chamfer_box("Blade", (0.3, 0.12, 0.02), 0.005, "FanBlade", location=(0.22, 0, 0.04))
        blade.rotation_euler = (math.radians(30), 0, 0)  # pitch: tilt around the blade's long axis (X)
        bpy.ops.object.select_all(action="DESELECT")
        blade.select_set(True)
        bpy.context.view_layer.objects.active = blade
        bpy.ops.object.transform_apply(location=True, rotation=True)  # origin back to the hub
        blade.rotation_euler = (0, 0, a)  # then turn it to its spot around the hub
        parts.append(blade)
    export(join(parts, "FanBlades"), "FanBlades")


def booster():
    # plate only (symmetric); the arrow is built in Unity so its direction is exact
    reset()
    export(chamfer_box("Booster", (1, 1, 0.1), 0.04, "BoosterPlate"), "Booster")


def leg():
    reset()
    profile = [(0, 0.42, 0), (0.08, 0.45, 0), (0.9, 0.33, 0), (1.0, 0.42, 0)]
    export(lathe("Leg", profile, ["DarkWood"], segments=8), "Leg")


def portal(name):
    # flat ring in the portal's color around a pale core (16 sides: it must read as round from above)
    reset()
    profile = [(0, 0.5, 0), (0.06, 0.5, 0), (0.06, 0.38, 0), (0.03, 0.38, 1)]
    export(lathe(name, profile, [name, "PortalCore"], segments=16), name)


def bumper():
    # dark base, pink post with a mushroom cap
    reset()
    profile = [(0, 0.5, 0), (0.15, 0.5, 0), (0.2, 0.3, 1), (0.75, 0.3, 1), (0.85, 0.5, 1), (1.0, 0.38, 1)]
    export(lathe("Bumper", profile, ["BumperBase", "Bumper"], segments=8), "Bumper")


def button():
    reset()
    profile = [(0, 0.5, 0), (0.4, 0.5, 0), (0.4, 0.38, 1), (1.0, 0.32, 1)]
    export(lathe("Button", profile, ["Metal", "ButtonCap"], segments=8), "Button")


def manhole():
    # round lid with three raised bars
    reset()
    parts = [lathe("Manhole", [(0, 0.5, 0), (0.1, 0.5, 0)], ["Grate"], segments=8)]
    for i in range(3):
        parts.append(chamfer_box("Bar", (0.7, 0.12, 0.05), 0.01, "Stripe", location=(0, (i - 1) * 0.22, 0.12)))
    export(join(parts, "Manhole"), "Manhole")


def wall_block(name):
    # gate / timed wall: a block with a dark band on top
    reset()
    export(chamfer_box(name, (1, 1, 1), 0.06, name, "Stripe"), name)


def magnet():
    # flat horseshoe: red U with metal tips
    reset()
    parts = [chamfer_box("L", (0.25, 0.7, 0.2), 0.02, "Magnet", location=(-0.3, 0.1, 0)),
             chamfer_box("R", (0.25, 0.7, 0.2), 0.02, "Magnet", location=(0.3, 0.1, 0)),
             chamfer_box("B", (0.85, 0.25, 0.2), 0.02, "Magnet", location=(0, -0.35, 0)),
             chamfer_box("TL", (0.25, 0.15, 0.2), 0.02, "Metal", location=(-0.3, 0.52, 0)),
             chamfer_box("TR", (0.25, 0.15, 0.2), 0.02, "Metal", location=(0.3, 0.52, 0))]
    export(join(parts, "Magnet"), "Magnet")


def plate(name, top=None, height=0.1):
    # flat tile covers (ice, sand, belt, platform): one chamfered slab
    reset()
    export(chamfer_box(name, (1, 1, height), 0.03, name, top), name)


cue()
ball()
pillar()
spikes()
fan()
fan_blades()
booster()
leg()
portal("PortalBlue")
portal("PortalOrange")
bumper()
button()
manhole()
wall_block("Gate")
wall_block("TimedWall")
magnet()
plate("Ice")
plate("Sand")
plate("Belt")
plate("Platform", "Felt", 0.2)
