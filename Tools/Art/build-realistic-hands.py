"""Refine the original realistic hands without changing UVs or the animation rig.

Blender --background --python Tools/Art/build-realistic-hands.py
Add -- --preview-only to refresh studio images without exporting the model.
Add -- --no-previews to export while the original-layout texture maps are prepared.
An optional --texture-dir PATH selects original-layout maps for studio rendering.
The committed original FBX and RigContract.blend are immutable inputs. This tool
never writes texture maps, Unity materials, metadata, clips, scenes or prefabs.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Hands'
ART = ROOT / 'Assets/HalliGalliMovement/Art'
PREVIEW = ROOT / 'Previews/RealisticHands'
ORIGINAL_REVISION = 'fa84b375ba78cce06bcde7e04960cae5fff6dd94'
ORIGINAL_SHA256 = 'dee5e774496d30524aa3f0bfff1c0d1989af0c794663b0e085afc29b50f64a7f'
MESH_NAMES = ('Realistic_Hand_L', 'Realistic_Hand_R')


def active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def digest(values):
    return hashlib.sha256(np.asarray(values, dtype=np.float32).tobytes()).hexdigest()


def mesh_contract(obj):
    mesh = obj.data
    return {
        'vertices': len(mesh.vertices),
        'triangles': sum(len(p.vertices) - 2 for p in mesh.polygons),
        'uv_sha256': digest([uv.uv[:] for uv in mesh.uv_layers.active.data]),
        'vertex_group_order': [g.name for g in obj.vertex_groups],
        'weights_sha256': digest([(v.index, w.group, w.weight) for v in mesh.vertices for w in v.groups]),
        'topology_sha256': hashlib.sha256(np.asarray([l.vertex_index for l in mesh.loops], dtype=np.int32).tobytes()).hexdigest(),
    }


def rig_contract(rig):
    return [(b.name, b.parent.name if b.parent else None,
             tuple(tuple(row) for row in b.matrix_local)) for b in rig.data.bones]


def recover_geometry():
    original_path = SOURCE / 'OriginalHands.fbx'
    if hashlib.sha256(original_path.read_bytes()).hexdigest() != ORIGINAL_SHA256:
        raise RuntimeError('OriginalHands.fbx differs from the recovered committed original')
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'RigContract.blend'))
    rig = bpy.data.objects['FP_Hands_Rig']
    targets = [bpy.data.objects[name] for name in MESH_NAMES]
    existing = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(original_path))
    imported = set(bpy.data.objects) - existing
    imported_rig = next(o for o in imported if o.type == 'ARMATURE')
    if rig_contract(rig) != rig_contract(imported_rig):
        raise RuntimeError('Recovered bones differ from the animation rig contract')
    for target in targets:
        source = next(o for o in imported if o.type == 'MESH' and o.name.startswith(target.name))
        if max(abs(a - b) for row_a, row_b in zip(source.matrix_world, target.matrix_world)
               for a, b in zip(row_a, row_b)) > 1e-7:
            raise RuntimeError('Recovered mesh transform differs from the rig contract')
        old_mesh = target.data
        name = old_mesh.name
        target.vertex_groups.clear()
        # Blender 5.2 mesh copies include ordered group definitions and weights.
        target.data = source.data.copy()
        bpy.data.meshes.remove(old_mesh)
        target.data.name = name
        if mesh_contract(target) != mesh_contract(source):
            raise RuntimeError('Recovered mesh data or skin weights were not transferred exactly')
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    return rig, targets


def refine_surface(obj):
    """Small volume-compensated fairing of existing points; never remesh/unwrap.

    The original dense fingers need little movement. The coarser forearm needs
    more fairing across broad triangulation ridges. Distal fingers/nails have a
    strict displacement limit. Inverse-edge-length weights prevent long sparse
    forearm triangles from dragging dense wrist/finger detail sideways.
    """
    mesh = obj.data
    before = mesh_contract(obj)
    original = np.asarray([v.co[:] for v in mesh.vertices], dtype=np.float64)
    edges = np.asarray([e.vertices[:] for e in mesh.edges], dtype=np.int32)
    a = np.concatenate((edges[:, 0], edges[:, 1]))
    b = np.concatenate((edges[:, 1], edges[:, 0]))
    edge_weight = 1.0 / np.maximum(np.linalg.norm(original[a] - original[b], axis=1), .0004)
    weight_sum = np.bincount(a, weights=edge_weight, minlength=len(original))
    y = original[:, 1]
    forearm = np.clip((y - .015) / .07, 0., 1.)
    forearm = forearm * forearm * (3. - 2. * forearm)
    distal = np.zeros(len(original), dtype=np.float64)
    for vertex in mesh.vertices:
        distal[vertex.index] = sum(w.weight for w in vertex.groups
                                   if obj.vertex_groups[w.group].name.endswith('3'))
    # Broad anatomy stays: at most 1.8 mm on forearm, .45 mm on hand,
    # tapering to .16 mm on distal phalanges/nail beds.
    limit = (.00045 + .00135 * forearm) * (1. - .64 * distal)
    strength = (.55 + .45 * forearm) * (1. - .45 * distal)
    # Preserve the original forearm cut and tiny pre-existing boundary defects.
    cap = np.clip((.498 - y) / .015, 0., 1.)
    strength *= cap
    positions = original.copy()
    for _ in range(24):
        for coefficient in (.5, -.53):
            average = np.column_stack([
                np.bincount(a, weights=edge_weight * positions[b, axis], minlength=len(original))
                / np.maximum(weight_sum, 1e-12) for axis in range(3)])
            positions += coefficient * strength[:, None] * (average - positions)
        displacement = positions - original
        length = np.linalg.norm(displacement, axis=1)
        positions = original + displacement * np.minimum(1., limit / np.maximum(length, 1e-12))[:, None]
    mesh.vertices.foreach_set('co', positions.astype(np.float32).ravel())
    # Do not retain imported normals from the old angular surface. Recalculate
    # the smooth geometric normals and export them explicitly for Unity's
    # normalImportMode=Import. UV seams are not hard shading boundaries.
    active(obj)
    if mesh.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    for edge in mesh.edges:
        edge.use_edge_sharp = False
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    mesh.update()
    geometric_normals = np.asarray([v.normal[:] for v in mesh.vertices], dtype=np.float64)
    normals = geometric_normals.copy()
    neighbor_count = np.maximum(np.bincount(a, minlength=len(original)), 1)
    normal_strength = (.5 * (1. - .65 * distal))[:, None]
    # The recovered mesh is a single connected surface, not UV-split pieces.
    # One-ring normals still expose its broad planar palm/finger panels.
    # Diffuse the NORMAL FIELD across shared topology, not the fine skin/UVs
    # or nail geometry, so light rolls across anatomical pads continuously.
    for _ in range(24):
        average = np.column_stack([
            np.bincount(a, weights=normals[b, axis], minlength=len(original))
            / neighbor_count for axis in range(3)])
        normals += normal_strength * (average - normals)
        normals /= np.maximum(np.linalg.norm(normals, axis=1)[:, None], 1e-12)
    mesh.normals_split_custom_set_from_vertices(normals.tolist())
    mesh.update()
    after = mesh_contract(obj)
    if before != after:
        raise RuntimeError('Fairing changed UVs, topology or skin binding')
    displacement_mm = np.linalg.norm(positions - original, axis=1) * 1000.
    normal_change = np.rad2deg(np.arccos(np.clip(np.sum(normals * geometric_normals, axis=1), -1., 1.)))
    return {
        'name': obj.name, 'mesh_name': mesh.name, **after,
        'displacement_mm': {'mean': float(np.mean(displacement_mm)),
                            'p95': float(np.percentile(displacement_mm, 95)),
                            'max': float(np.max(displacement_mm)),
                            'forearm_mean': float(np.mean(displacement_mm[forearm > .95])),
                            'hand_mean': float(np.mean(displacement_mm[y < .015]))},
        'explicit_smooth_normals': mesh.has_custom_normals,
        'normal_field_diffusion': {'iterations': 24, 'mean_angle_degrees': float(np.mean(normal_change)),
                                   'p95_angle_degrees': float(np.percentile(normal_change, 95))},
    }, original


def studio_material(texture_dir):
    material = bpy.data.materials.get('Original_Detailed_Skin') or bpy.data.materials.new('Original_Detailed_Skin')
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    links = material.node_tree.links
    output = nodes.new('ShaderNodeOutputMaterial')
    output.location = (650, 0)
    skin = nodes.new('ShaderNodeBsdfPrincipled')
    skin.location = (350, 0)
    skin.inputs['Metallic'].default_value = 0.
    skin.inputs['Subsurface Weight'].default_value = .025
    skin.inputs['Subsurface Radius'].default_value = (1., .45, .25)
    skin.inputs['Subsurface Scale'].default_value = .001
    links.new(skin.outputs['BSDF'], output.inputs['Surface'])
    for map_name, location in [('BaseColor', (-600, 250)), ('Normal', (-600, -50)), ('ORM', (-600, -350))]:
        image = bpy.data.images.load(str(texture_dir / ('Hands_' + map_name + '.png')), check_existing=True)
        image.reload()
        image.colorspace_settings.name = 'sRGB' if map_name == 'BaseColor' else 'Non-Color'
        texture = nodes.new('ShaderNodeTexImage')
        texture.name = 'Original ' + map_name
        texture.image = image
        texture.location = location
        if map_name == 'BaseColor':
            links.new(texture.outputs['Color'], skin.inputs['Base Color'])
        elif map_name == 'Normal':
            normal = nodes.new('ShaderNodeNormalMap')
            normal.inputs['Strength'].default_value = .22
            normal.location = (-50, -50)
            links.new(texture.outputs['Color'], normal.inputs['Color'])
            links.new(normal.outputs['Normal'], skin.inputs['Normal'])
        else:
            separate = nodes.new('ShaderNodeSeparateColor')
            separate.location = (-250, -350)
            links.new(texture.outputs['Color'], separate.inputs['Color'])
            links.new(separate.outputs['Green'], skin.inputs['Roughness'])
    return material


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def render_previews(left, right, original_left=None):
    PREVIEW.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    for obj in list(bpy.data.objects):
        if obj.type in {'CAMERA', 'LIGHT'}:
            bpy.data.objects.remove(obj, do_unlink=True)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1400
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.get('Realistic hand studio') or bpy.data.worlds.new('Realistic hand studio')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.12, .14, .17, 1.)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .45
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium Low Contrast'
    scene.view_settings.exposure = 0.
    camera = bpy.data.objects.new('RealisticStudioCamera', bpy.data.cameras.new('RealisticStudioCamera'))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = 'ORTHO'
    for name, location, power, size in [
        ('SoftKey', (-.10, -.15, .65), 16., .75),
        ('SoftFill', (.65, .2, -.50), 12., .70),
        ('ForearmRim', (.25, .65, .35), 10., .55),
    ]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy = power
        data.shape = 'DISK'
        data.size = size
        light = bpy.data.objects.new(name, data)
        scene.collection.objects.link(light)
        light.location = location
        aim(light, (.21, .12, 0.))
    right.hide_render = True
    views = [
        ('Hands_Forearm_Dorsal', (.21, .12, .95), (.21, .15, 0.), .76),
        ('Hands_Forearm_Palm', (.21, .12, -.95), (.21, .15, 0.), .76),
        ('Hands_Close_Dorsal', (.21, -.09, .65), (.21, -.075, 0.), .28),
        ('Hands_Close_Palm', (.21, -.09, -.65), (.21, -.075, 0.), .28),
        ('Hands_ThreeQuarter', (.67, -.10, .73), (.21, .14, 0.), .76),
    ]
    for name, location, target, scale in views:
        camera.location = location
        aim(camera, target)
        camera.data.ortho_scale = scale
        if location[2] > 0.:
            camera.rotation_euler.rotate_axis('Z', math.pi)
        scene.render.filepath = str(PREVIEW / (name + '.png'))
        bpy.ops.render.render(write_still=True)
    if original_left is not None:
        refined = np.asarray([v.co[:] for v in left.data.vertices], dtype=np.float32)
        refined_normals = [v.normal[:] for v in left.data.vertices]
        left.data.vertices.foreach_set('co', original_left.astype(np.float32).ravel())
        active(left)
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
        left.data.update()
        scene.render.filepath = str(PREVIEW / 'Original_ThreeQuarter_SameLighting.png')
        bpy.ops.render.render(write_still=True)
        left.data.vertices.foreach_set('co', refined.ravel())
        left.data.update()
        left.data.normals_split_custom_set_from_vertices(refined_normals)
    right.hide_render = False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preview-only', action='store_true')
    parser.add_argument('--no-previews', action='store_true')
    parser.add_argument('--texture-dir', type=Path, default=ART / 'Textures')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    original_left = None
    if args.preview_only:
        _, original_objects = recover_geometry()
        original_left = np.asarray([v.co[:] for v in original_objects[0].data.vertices], dtype=np.float64)
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'RealisticHands.blend'))
        rig = bpy.data.objects['FP_Hands_Rig']
        objects = [bpy.data.objects[name] for name in MESH_NAMES]
    else:
        rig, objects = recover_geometry()
        reports = []
        for obj in objects:
            report, original = refine_surface(obj)
            reports.append(report)
            if obj.name.endswith('_L'):
                original_left = original
        report = {
            'style': 'Original detailed realistic skin and anatomy; restrained silhouette fairing',
            'source': 'ArtSource/Hands/RealisticHands.blend',
            'original_fbx': 'ArtSource/Hands/OriginalHands.fbx',
            'original_revision': ORIGINAL_REVISION,
            'original_sha256': ORIGINAL_SHA256,
            'rig_template': 'ArtSource/Hands/RigContract.blend',
            'bone_count': len(rig.data.bones),
            'bone_order': [b.name for b in rig.data.bones],
            'method': '24 inverse-edge-length weighted Taubin position pairs (+0.5/-0.53), region-weighted and displacement-limited; 24 topology-adjacent smooth normal-field diffusion steps with reduced distal/nail influence; original topology, UV and weights unchanged',
            'meshes': reports,
            'limitations': [
                'Original VARCO anatomy is retained, not a new photogrammetric scan.',
                'No subdivision or added topology; four original nonmanifold edges per hand remain to preserve original indexing and UV.',
                'Blender studio previews are appearance renders, not Unity gameplay or motion validation.',
                'Original 4096-square skin details and UVs are retained; the separate texture tool attenuates broad baked shadow bands without replacing skin detail.',
            ],
        }
        (SOURCE / 'realistic-hand-model.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print('REALISTIC_HAND_MODEL ' + json.dumps(report))
    material = studio_material(args.texture_dir)
    for obj in objects:
        obj.data.materials.clear()
        obj.data.materials.append(material)
    bpy.context.preferences.filepaths.save_version = 0
    if not args.preview_only:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in [rig, *objects]:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = rig
        # Match the known-good Unity binding. FBX_SCALE_ALL writes meters into
        # FBX unit metadata rather than baking 100x scale into objects/bind poses.
        bpy.ops.export_scene.fbx(
            filepath=str(ART / 'FP_Hands.fbx'), use_selection=True,
            object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False,
            axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=True,
            mesh_smooth_type='OFF', use_armature_deform_only=False,
            primary_bone_axis='Y', secondary_bone_axis='X', path_mode='STRIP')
        for image in bpy.data.images:
            if image.filepath:
                image.filepath = bpy.path.relpath(image.filepath, start=str(SOURCE))
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'RealisticHands.blend'))
    if not args.no_previews:
        render_previews(*objects, original_left=original_left)
    print('REALISTIC_HAND_PREVIEWS_COMPLETE' if args.preview_only else 'REALISTIC_HAND_ASSET_EXPORT_COMPLETE')


if __name__ == '__main__':
    main()
