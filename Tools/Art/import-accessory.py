"""Blender --background --python Tools/Art/import-accessory.py -- source.glb AssetName
Converts a VARCO accessory to Unity FBX and extracts its authored PBR textures.
"""
import bpy
import os
import sys
import numpy as np

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
source = os.path.abspath(sys.argv[sys.argv.index('--') + 1])
asset_name = sys.argv[sys.argv.index('--') + 2]
if not asset_name.isidentifier():
    raise ValueError('AssetName must be an identifier, e.g. CatEars or BirthdayHat')
output = os.path.join(root, 'Assets/HalliGalliMovement/Art/Accessories', asset_name)
os.makedirs(output, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=source)
for material in bpy.data.materials:
    if not material.use_nodes:
        continue
    for node in material.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or not node.image:
            continue
        image = node.image
        name = asset_name + '_' + node.label.replace(' ', '_')
        if node.label in ('METALLIC ROUGHNESS', 'OCCLUSION'):
            pixels = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
            image.pixels.foreach_get(pixels)
            pixels = pixels.reshape(-1, 4)
            converted = np.ones_like(pixels)
            if node.label == 'METALLIC ROUGHNESS':
                converted[:, 0] = pixels[:, 2]
                converted[:, 1:3] = 0
                converted[:, 3] = 1 - pixels[:, 1]
                name = asset_name + '_MetallicSmoothness'
            else:
                converted[:, :3] = pixels[:, 0:1]
                name = asset_name + '_Occlusion'
            image = bpy.data.images.new(name, width=image.size[0], height=image.size[1], alpha=True)
            image.colorspace_settings.name = 'Non-Color'
            image.pixels.foreach_set(converted.ravel())
        image.filepath_raw = os.path.join(output, name + '.png')
        image.file_format = 'PNG'
        image.save()
        print('ACCESSORY_TEXTURE', node.label, image.filepath_raw)
mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
for obj in mesh_objects:
    obj.name = asset_name if len(mesh_objects) == 1 else asset_name + '_' + obj.name
    print('ACCESSORY_MESH', obj.name, len(obj.data.vertices), len(obj.data.polygons), tuple(obj.dimensions))
bpy.ops.export_scene.fbx(filepath=os.path.join(output, asset_name + '.fbx'), object_types={'MESH'},
    use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
    axis_forward='-Z', axis_up='Y', path_mode='STRIP', use_custom_props=False)
