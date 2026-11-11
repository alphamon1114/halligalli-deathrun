"""Blender --background --python Tools/Art/repair-birthday-hat.py -- source.glb repaired.glb
Removes the generated pompom fragments and replaces them with a closed red sphere.
The generated cone, rolled rim, UVs and textures remain intact.
"""
import bpy
import bmesh
import os
import sys

args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(args[0]))
cone = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH')
cone.name = 'Cone'
bpy.context.view_layer.objects.active = cone
cone.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bm = bmesh.new()
bm.from_mesh(cone.data)
# The source GLB is Y-up; Blender's import converts its vertical axis to Z.
# The ragged red fragments begin above z=0.39 in this generated source.
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z > .39], context='VERTS')
bm.to_mesh(cone.data)
bm.free()
cone.data.update()
bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=20, radius=.065, location=(0, 0, .425))
pompom = bpy.context.object
pompom.name = 'Pompom'
for polygon in pompom.data.polygons:
    polygon.use_smooth = True
material = bpy.data.materials.new('PompomRed')
material.diffuse_color = (.8, .015, .025, 1)
material.use_nodes = True
shader = material.node_tree.nodes.get('Principled BSDF')
shader.inputs['Base Color'].default_value = (.8, .015, .025, 1)
shader.inputs['Roughness'].default_value = .85
pompom.data.materials.append(material)
check = bmesh.new()
check.from_mesh(pompom.data)
assert all(edge.is_manifold for edge in check.edges), 'Pompom must be a closed manifold'
check.free()
bpy.ops.export_scene.gltf(filepath=os.path.abspath(args[1]), export_format='GLB')
print('BIRTHDAY_POMPOM_REPAIRED closed_manifold=True vertices=' + str(len(pompom.data.vertices)))
