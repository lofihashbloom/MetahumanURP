"""Bake legacy eye optical lookups as character data, without saving Unreal assets.

Run with Unreal Editor rendering enabled and METAHUMAN_MANIFEST pointing at a
complete exported bundle. Writes a NEW *-Optics.mhmanifest.json beside that bundle.
The original manifest is retained. No Epic renderer implementation is translated.
"""
import hashlib
import json
import os
from pathlib import Path
import unreal

manifest_path = Path(os.environ['METAHUMAN_MANIFEST']).resolve()
manifest = json.loads(manifest_path.read_text())
assert manifest.get('complete') and manifest.get('schemaVersion') == 1
records = [m for m in manifest['materials'] if m.get('baseMaterial', '').split('.')[-1] == 'M_EyeOcclusion'
           or 'EyeOcclusion' in m.get('name', '')]
if not records:
    raise RuntimeError('This bundle has no supported legacy M_EyeOcclusion material.')
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
lib = unreal.MaterialEditingLibrary
output = manifest_path.parent
(output / 'Derived').mkdir(exist_ok=True)
receipt = {'engine': unreal.SystemLibrary.get_engine_version(), 'sourceAssetsSaved': False,
           'method': 'Original source material evaluated into linear UV lookup textures', 'materials': []}

def validate_optical_graph(material):
    """Gate this data bake on inspected semantic edges, as well as node IDs."""
    nodes = {e.get_name(): e for e in lib.get_material_expressions(material)}
    def node(name, kind):
        expression = nodes.get(name)
        if not expression or expression.get_class().get_name() != kind:
            raise RuntimeError('Unsupported source optical graph node: ' + name)
        return expression
    def inputs(expression):
        return dict(zip((str(x) for x in lib.get_material_expression_input_names(expression)),
                        lib.get_inputs_for_material_expression(material, expression)))
    def edge(expression, slot, expected):
        actual = inputs(expression).get(slot)
        if not actual or actual.get_name() != expected.get_name():
            raise RuntimeError('Unsupported source optical graph edge: ' + expression.get_name() + '.' + slot)
    weight = node('MaterialExpressionOneMinus_18', 'MaterialExpressionOneMinus')
    transmission = node('MaterialExpressionMultiply_17', 'MaterialExpressionMultiply')
    blend = node('MaterialExpressionLinearInterpolate_0', 'MaterialExpressionLinearInterpolate')
    scene = node('MaterialExpressionSceneColor_15', 'MaterialExpressionSceneColor')
    final = node('MaterialExpressionMultiply_48', 'MaterialExpressionMultiply')
    edge(blend, 'Alpha', weight)
    edge(blend, 'A', scene)
    edge(final, 'A', blend)
    edge(final, 'B', transmission)
    root = lib.get_material_property_input_node(material, unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    if root != final:
        if not root or root.get_class().get_name() != 'MaterialExpressionFeatureLevelSwitch':
            raise RuntimeError('Unsupported optical emissive output.')
        edge(root, 'Default', final)
        # An explicit desktop override would need separate inspection.
        for feature in ('SM5', 'SM6'):
            override = inputs(root).get(feature)
            if override and override != final:
                raise RuntimeError('Unsupported optical desktop feature branch: ' + feature)
    def ancestors(expression, visited=None):
        visited = set() if visited is None else visited
        if not expression or expression.get_name() in visited: return []
        visited.add(expression.get_name())
        return [expression] + [item for parent in lib.get_inputs_for_material_expression(material, expression)
                              for item in ancestors(parent, visited)]
    blur_input = inputs(blend).get('B')
    if not any(e.get_class().get_name() == 'MaterialExpressionSceneColor' for e in ancestors(blur_input)):
        raise RuntimeError('Expected SceneColor blur branch was not connected.')
    for lookup in (weight, transmission):
        dependencies = ancestors(lookup)
        if any(e.get_class().get_name() == 'MaterialExpressionSceneColor' for e in dependencies):
            raise RuntimeError('A UV lookup unexpectedly depends on SceneColor: ' + lookup.get_name())
        for expression in dependencies:
            if expression.get_class().get_name() == 'MaterialExpressionTextureCoordinate':
                if int(expression.get_editor_property('coordinate_index')) != 0:
                    raise RuntimeError('The canvas bake supports source UV0 only: ' + expression.get_name())
    return {'blurAlpha': weight.get_name(), 'sceneBlend': blend.get_name(),
            'transmission': transmission.get_name(), 'finalMultiply': final.get_name(),
            'emissiveRoot': root.get_name(), 'desktopSemanticEdgesValidated': True, 'uvChannel': 0}

for record in records:
    original = unreal.load_asset(record['source'])
    base = original.get_base_material()
    if base.get_name() != 'M_EyeOcclusion':
        raise RuntimeError('Unsupported source optical material: ' + base.get_path_name())
    graph_validation = validate_optical_graph(base)
    record['baseMaterial'] = base.get_path_name()
    config = json.dumps({'source': record['source'], 'scalars': record.get('scalars', []),
                         'vectors': record.get('vectors', [])}, sort_keys=True)
    source_files = []
    for asset in (original, base):
        package = asset.get_path_name().split('.')[0]
        if not package.startswith('/Game/'):
            raise RuntimeError('Expected a project-owned source material path.')
        disk = Path(unreal.Paths.project_content_dir()) / (package[len('/Game/'):] + '.uasset')
        source_files.append({'source': package, 'sha256': hashlib.sha256(disk.read_bytes()).hexdigest()})
    digest = hashlib.sha256((config + json.dumps(source_files, sort_keys=True)).encode()).hexdigest()
    baked = []
    for parameter, node_name, expected_type in [
        ('OcclusionBlurWeight', 'MaterialExpressionOneMinus_18', 'MaterialExpressionOneMinus'),
        ('OcclusionTransmission', 'MaterialExpressionMultiply_17', 'MaterialExpressionMultiply')]:
        # Node identifiers deliberately gate this exporter to the inspected legacy
        # graph. A changed graph must be inspected instead of silently approximated.
        copy = unreal.AssetToolsHelpers.get_asset_tools().duplicate_asset(
            'Optics_' + manifest['characterName'] + '_' + parameter + '_' + digest[:8], '/Game/__MetaHumanDiagnostic', base)
        if not copy:
            raise RuntimeError('Could not create temporary optical material for ' + manifest['characterName'])
        nodes = lib.get_material_expressions(copy)
        expression = next((e for e in nodes if e.get_name() == node_name), None)
        if not expression or expression.get_class().get_name() != expected_type:
            raise RuntimeError('Unsupported source optical graph layout: ' + node_name)
        lib.connect_material_property(expression, '', unreal.MaterialProperty.MP_EMISSIVE_COLOR)
        copy.set_editor_property('blend_mode', unreal.BlendMode.BLEND_OPAQUE)
        copy.set_editor_property('shading_model', unreal.MaterialShadingModel.MSM_UNLIT)
        lib.recompile_material(copy)
        instance = unreal.new_object(unreal.MaterialInstanceConstant)
        lib.set_material_instance_parent(instance, copy)
        for p in record.get('scalars', []):
            lib.set_material_instance_scalar_parameter_value(instance, p['name'], p['value'])
        for p in record.get('vectors', []):
            lib.set_material_instance_vector_parameter_value(instance, p['name'], unreal.LinearColor(*p['value']))
        lib.get_statistics(instance)
        target = unreal.RenderingLibrary.create_render_target2d(world, 512, 512,
            unreal.TextureRenderTargetFormat.RTF_RGBA8, unreal.LinearColor(0, 0, 0, 0))
        target.set_editor_property('target_gamma', 1)
        filename = parameter + '_' + digest[:8] + '.png'
        unreal.RenderingLibrary.draw_material_to_render_target(world, target, instance)
        unreal.RenderingLibrary.export_render_target(world, target, str(output / 'Derived'), filename)
        disk = output / 'Derived' / filename
        if not disk.exists():
            raise RuntimeError('Lookup export failed: ' + str(disk))
        source = 'derived:' + record['source'] + ':' + parameter
        texture = dict(source=source, name=parameter, file='Derived/' + filename,
            sha256=hashlib.sha256(disk.read_bytes()).hexdigest(), bytes=disk.stat().st_size,
            srgb=False, compression='LinearData', flipGreen=False,
            derived=True, derivedFrom=record['source'], bakeSourceSha256=digest)
        manifest['textures'] = [t for t in manifest['textures'] if t['source'] != source] + [texture]
        record['parameters'] = [p for p in record['parameters'] if p['name'] != parameter] + [dict(name=parameter, texture=source)]
        baked.append(texture)
    receipt['materials'].append(dict(source=record['source'], sourceFiles=source_files,
        bakeSourceSha256=digest, graphValidation=graph_validation, outputs=baked,
        limitations='UV lookups freeze source material parameters until re-exported.'))
manifest.setdefault('notes', []).append('Eye optics include derived source-material UV data; source assets retain their licensing. Filtering is independently implemented; full render parity remains incomplete.')
new_manifest = output / (manifest['characterName'] + '-Optics.mhmanifest.json')
new_manifest.write_text(json.dumps(manifest, indent=2) + '\n')
(output / 'Derived' / 'EyeOpticsBakeReceipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
unreal.log('EYE_OPTICS_EXPORT ' + str(new_manifest))
# This is a dedicated offscreen export invocation, never an interactive session.
if os.environ.get('METAHUMAN_QUIT_AFTER_EXPORT') == '1':
    unreal.SystemLibrary.quit_editor()
