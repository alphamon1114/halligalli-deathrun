"""Blender --background --python Tools/Art/build-diner-stool.py -- [output-folder]
Build the shared round diner stool, one mesh/material with a small PBR atlas.
Default output is the existing WorkshopChair asset folder; preserve its .meta files.
"""
import bpy
import bmesh
import json
import math
import random
import struct
import sys
import zlib
from pathlib import Path
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
folder = Path(args[0]) if args else Path(__file__).resolve().parents[2] / 'Assets/HalliGalliMovement/Art/Environment/WorkshopChair'
folder.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
vertices, faces, face_uvs = [], [], []


def lathe(profile, slot, segments=64, rotation=None, offset=Vector((0, 0, 0))):
    rings, uv_rings = [], []
    distances = [0.0]
    for a, b in zip(profile, profile[1:]):
        distances.append(distances[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
    for (radius, height), distance in zip(profile, distances):
        ring, uv_ring = [], []
        for i in range(1 if radius == 0 else segments):
            angle = math.tau * i / segments
            point = Vector((radius * math.cos(angle), radius * math.sin(angle), height))
            if rotation is not None:
                point = rotation @ point
            ring.append(len(vertices)); vertices.append(tuple(point + offset))
            uv_ring.append((slot / 4 + .025 + .20 * i / segments, .04 + .92 * distance / distances[-1]))
        rings.append(ring); uv_rings.append(uv_ring)
    for a, b, au, bu in zip(rings, rings[1:], uv_rings, uv_rings[1:]):
        for i in range(segments):
            j = (i + 1) % segments
            if len(a) == 1:
                faces.append((a[0], b[j], b[i])); face_uvs.append((au[0], bu[j], bu[i]))
            elif len(b) == 1:
                faces.append((a[i], a[j], b[0])); face_uvs.append((au[i], au[j], bu[0]))
            else:
                faces.append((a[i], a[j], b[j], b[i])); face_uvs.append((au[i], au[j], bu[j], bu[i]))


# Rubber underside, softly rolled steel base, and the central pedestal.
lathe([(0, 0), (.208, 0), (.217, .003), (.220, .008), (.218, .014), (0, .014)], 2)
lathe([(0, .012), (.211, .012), (.218, .018), (.220, .025), (.218, .032),
       (.210, .041), (.194, .049), (.165, .055), (.115, .060), (.052, .064), (0, .064)], 1)
lathe([(0, .052), (.047, .052), (.050, .057), (.050, .074), (.045, .082),
       (.038, .090), (.038, .456), (.042, .469), (.063, .480), (.085, .487), (0, .487)], 1)
# Under-seat chrome rim, round vinyl cushion, and narrow stitched-edge piping.
lathe([(0, .477), (.180, .477), (.210, .484), (.231, .491), (.243, .497),
       (.245, .503), (.243, .510), (0, .510)], 1)
lathe([(0, .507), (.228, .507), (.244, .511), (.254, .520), (.260, .533),
       (.260, .548), (.258, .562), (.252, .575), (.241, .583), (.224, .587),
       (.175, .588), (.100, .588), (0, .588)], 0)
lathe([(.259 + .002 * math.cos(math.tau * i / 8), .542 + .002 * math.sin(math.tau * i / 8)) for i in range(9)], 3)
# Low footrest with three real spokes joining it to the pedestal.
lathe([(.150 + .010 * math.cos(math.tau * i / 12), .180 + .010 * math.sin(math.tau * i / 12)) for i in range(13)], 1)
for i in range(3):
    angle = math.tau * i / 3
    direction = Vector((math.cos(angle), math.sin(angle), 0))
    lathe([(0, 0), (.008, 0), (.008, .114), (0, .114)], 1, 20,
          direction.to_track_quat('Z', 'Y').to_matrix(), direction * .036 + Vector((0, 0, .180)))
mesh = bpy.data.meshes.new('WorkshopChair_Mesh_0')
mesh.from_pydata(vertices, [], faces)
uvs = mesh.uv_layers.new(name='UVMap')
for polygon, coordinates in zip(mesh.polygons, face_uvs):
    polygon.use_smooth = True
    for index, uv in zip(polygon.loop_indices, coordinates):
        uvs.data[index].uv = uv
bm = bmesh.new(); bm.from_mesh(mesh)
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.0000001)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
assert all(edge.is_manifold for edge in bm.edges), 'Every stool part must be closed'
bm.to_mesh(mesh); bm.free(); mesh.update()
obj = bpy.data.objects.new('WorkshopChair_Mesh_0', mesh)
bpy.context.collection.objects.link(obj)
obj.data.materials.append(bpy.data.materials.new('material_0'))
bpy.context.view_layer.objects.active = obj; obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(folder / 'WorkshopChair.fbx'), use_selection=True,
    object_types={'MESH'}, use_mesh_modifiers=True, bake_anim=False, add_leaf_bones=False,
    axis_forward='-Z', axis_up='Y', path_mode='STRIP', use_custom_props=False)


def png(path, rows, size=512):
    def chunk(kind, data):
        return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data) & 0xffffffff)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', size, size, 8, 6, 0, 0, 0)) +
                     chunk(b'IDAT', zlib.compress(b''.join(b'\0' + row for row in rows), 9)) + chunk(b'IEND', b''))


# Atlas quarters: warm red vinyl, satin steel, rubber, darker seam. Existing
# texture GUIDs and the single environment material remain shared by all seats.
rows = {key: [] for key in ('Texture_0', 'Texture_1', 'Texture_2', 'MetalSmooth_0', 'Occlusion_0')}
randomizer = random.Random(16112026)
colors = [(159, 35, 27), (172, 181, 188), (20, 23, 26), (112, 23, 19)]
metals, roughness = [0, 205, 0, 0], [105, 82, 220, 135]
for y in range(512):
    buffers = {key: bytearray() for key in rows}
    for x in range(512):
        slot = x // 128; noise = randomizer.uniform(-1, 1)
        grain = noise * (1.5 if slot == 0 else .6)
        color = [max(0, min(255, round(c + grain))) for c in colors[slot]]
        rough = max(0, min(255, roughness[slot] + round(noise * 3)))
        buffers['Texture_0'].extend((*color, 255))
        buffers['Texture_1'].extend((255, rough, metals[slot], 255))
        buffers['Texture_2'].extend((128, 128, 255, 255))
        buffers['MetalSmooth_0'].extend((metals[slot], 0, 0, 255 - rough))
        buffers['Occlusion_0'].extend((255, 255, 255, 255))
    for key in rows:
        rows[key].append(bytes(buffers[key]))
for key, data in rows.items():
    png(folder / ('WorkshopChair_' + key + '.png'), data)
print('DINER_STOOL_BUILT', json.dumps({'vertices': len(mesh.vertices),
    'triangles': sum(len(p.vertices) - 2 for p in mesh.polygons), 'dimensionsBlenderXYZ': list(obj.dimensions),
    'materialSlots': 1, 'textureSize': 512, 'closedParts': True}))
