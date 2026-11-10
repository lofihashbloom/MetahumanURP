"""Run inside UE 5.8 Python. Reads source assets; writes a Unity interchange bundle.

UnrealEditor-Cmd GameAnimationSample.uproject -run=pythonscript -script=<this file>
"""
import hashlib
import os
import json
import struct
import traceback
from pathlib import Path
import unreal

SOURCE = os.environ.get('METAHUMAN_SOURCE_ROOT', '/Game/MetaHumans/Kellan').rstrip('/')
CHARACTER = SOURCE.rsplit('/', 1)[-1]
OUT = Path(os.environ.get('METAHUMAN_EXPORT_DIR', str(Path(unreal.Paths.project_saved_dir()) / 'MetaHumanExport' / CHARACTER))).resolve()
OUT.mkdir(parents=True, exist_ok=True)
E = unreal.EditorAssetLibrary
M = unreal.MaterialEditingLibrary
report = dict(schemaVersion=1, characterName=CHARACTER, sourceRoot=SOURCE,
              engineVersion=unreal.SystemLibrary.get_engine_version(),
              complete=False, models=[], materials=[], textures=[], grooms=[], errors=[],
              notes=['Original MetaHuman assets retain their Epic licensing.',
                     'FBX morph export is not a RigLogic/DNA solver.',
                     'Groom strands require a separate renderer; cards exported where available.'])
textures = {}
materials = {}

def get(obj, key, fallback=None):
    try:
        return obj.get_editor_property(key)
    except Exception:
        return fallback

def save():
    # A native exporter can terminate the process before Python can catch an
    # error. Keep resolved materials and incomplete state in every checkpoint.
    report['materials'] = list(materials.values())
    (OUT / (CHARACTER + '.mhmanifest.json')).write_text(json.dumps(report, indent=2))

def export(obj, folder, suffix, exporter, options=None):
    source = obj.get_path_name()
    name = obj.get_name() + '_' + hashlib.sha1(source.encode()).hexdigest()[:8]
    target = OUT / folder / (name + suffix)
    target.parent.mkdir(parents=True, exist_ok=True)
    report['pendingExport'] = dict(source=source, file=str(target.relative_to(OUT)),
                                  exporter=exporter.get_class().get_name())
    save()
    task = unreal.AssetExportTask()
    task.object = obj
    task.filename = str(target)
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    task.exporter = exporter
    if options:
        task.options = options
    if not unreal.Exporter.run_asset_export_task(task) or not target.exists():
        raise RuntimeError(f'Export failed: {source}: {list(task.errors)}')
    report.pop('pendingExport', None)
    return dict(source=source, file=str(target.relative_to(OUT)),
                sha256=hashlib.sha256(target.read_bytes()).hexdigest(), bytes=target.stat().st_size)

def texture_exporter(texture):
    # TGA supports only 8-bit G8/BGRA8 source images. The official exporter
    # asserts on other formats rather than returning an export failure.
    # Texture.Source is protected in Python. A freshly generated AssetData
    # exposes SourceFormat through the supported Asset Registry tag API instead.
    data = unreal.AssetRegistryHelpers.create_asset_data(texture)
    fmt = unreal.AssetRegistryHelpers.get_tag_value(data, 'SourceFormat')
    if isinstance(fmt, tuple):
        fmt = next((item for item in fmt if isinstance(item, str)), '')
    if not isinstance(fmt, str) or not fmt:
        raise RuntimeError('SourceFormat asset-registry tag is unavailable: ' + texture.get_path_name())
    # Registry display names use spaces ("TSF BGRA8") in this engine build.
    token = fmt.upper().replace('_', '').replace(' ', '')
    if token.startswith('TSF'):
        token = token[3:]
    if token in ('G8', 'BGRA8'):
        return '.tga', unreal.TextureExporterTGA(), str(fmt)
    if token in ('G16', 'RGBA16'):
        return '.png', unreal.TextureExporterPNG(), str(fmt)
    if token in ('BGRE8', 'RGBA16F', 'RGBA32F', 'R16F', 'R32F'):
        return '.exr', unreal.TextureExporterEXR(), str(fmt)
    # Never invoke a format-specific native exporter with an unverified format.
    raise RuntimeError('Unsupported texture source format: ' + str(fmt))

def image_storage(path):
    """Record the actual file representation, including any native precision conversion."""
    data = path.read_bytes()
    if path.suffix == '.png':
        width, height = struct.unpack('>II', data[16:24])
        return dict(container='PNG', width=width, height=height, bitsPerChannel=data[24])
    if path.suffix == '.tga':
        width, height = struct.unpack('<HH', data[12:16])
        return dict(container='TGA', width=width, height=height, bitsPerPixel=data[16])
    if path.suffix == '.exr':
        position = 8
        result = dict(container='EXR', channels={})
        def cstring(offset):
            end = data.index(b'\0', offset)
            return data[offset:end].decode('ascii'), end + 1
        while data[position] != 0:
            name, position = cstring(position)
            kind, position = cstring(position)
            size = struct.unpack_from('<I', data, position)[0]
            position += 4
            end = position + size
            if kind == 'chlist':
                while data[position] != 0:
                    channel, position = cstring(position)
                    pixel_type = struct.unpack_from('<i', data, position)[0]
                    result['channels'][channel] = {0:'uint32', 1:'float16', 2:'float32'}.get(pixel_type, str(pixel_type))
                    position += 16
            elif name == 'dataWindow':
                x0, y0, x1, y1 = struct.unpack_from('<iiii', data, position)
                result.update(width=x1-x0+1, height=y1-y0+1)
            position = end
        return result
    return dict(container=path.suffix)

options = unreal.FbxExportOption()
for key, value in dict(ascii=False, vertex_color=True, level_of_detail=False,
                       collision=False, export_morph_targets=True,
                       export_preview_mesh=False, map_skeletal_motion_to_root=False).items():
    options.set_editor_property(key, value)
options.set_editor_property('bake_material_inputs', unreal.FbxMaterialBakeMode.DISABLED)

def add_material(mat):
    if not mat or mat.get_path_name() in materials:
        return
    rec = dict(source=mat.get_path_name(), name=mat.get_name(), parameters=[], textures=[])
    materials[rec['source']] = rec
    base = mat.get_base_material()
    rec['baseMaterial'] = base.get_path_name()
    if base.get_editor_property('blend_mode') == unreal.BlendMode.BLEND_MASKED:
        mask = M.get_material_property_input_node(base, unreal.MaterialProperty.MP_OPACITY_MASK)
        if mask and mask.get_class().get_name() == 'MaterialExpressionConstant':
            rec['discardAll'] = mask.get_editor_property('r') < base.get_editor_property('opacity_mask_clip_value')
    if base.get_name() == 'M_MetaHumanSkin_Simplified':
        rec['packedMaskLayout'] = 'LegacySRM'
    profile = get(mat, 'subsurface_profile') if get(mat, 'override_subsurface_profile', False) else get(base, 'subsurface_profile')
    if profile:
        settings = profile.get_editor_property('settings')
        def rgb(name):
            color = settings.get_editor_property(name)
            return [color.r, color.g, color.b]
        rec['subsurfaceProfile'] = dict(source=profile.get_path_name(), burley=settings.get_editor_property('enable_burley'), meanFreePath=get(settings, 'enable_mean_free_path', False),
            surfaceAlbedo=rgb('surface_albedo'), meanFreePathColor=rgb('mean_free_path_color'),
            meanFreePathDistanceCentimetres=settings.get_editor_property('mean_free_path_distance'),worldUnitScale=settings.get_editor_property('world_unit_scale'),
            roughness0=settings.get_editor_property('roughness0'),roughness1=settings.get_editor_property('roughness1'),lobeMix=settings.get_editor_property('lobe_mix'))
    rec['scalars'], rec['vectors'], rec['staticSwitches'] = [], [], []
    for name in M.get_scalar_parameter_names(mat):
        value = M.get_material_instance_scalar_parameter_value(mat, name) if isinstance(mat, unreal.MaterialInstance) else M.get_material_default_scalar_parameter_value(mat, name)
        rec['scalars'].append(dict(name=str(name), value=value))
    for name in M.get_vector_parameter_names(mat):
        value = M.get_material_instance_vector_parameter_value(mat, name) if isinstance(mat, unreal.MaterialInstance) else M.get_material_default_vector_parameter_value(mat, name)
        rec['vectors'].append(dict(name=str(name), value=[value.r, value.g, value.b, value.a]))
    for name in M.get_static_switch_parameter_names(mat):
        value = M.get_material_instance_static_switch_parameter_value(mat, name) if isinstance(mat, unreal.MaterialInstance) else M.get_material_default_static_switch_parameter_value(mat, name)
        rec['staticSwitches'].append(dict(name=str(name), value=bool(value)))
    # Query resolved instance values, including inherited defaults.
    for name in M.get_texture_parameter_names(mat):
        tex = M.get_material_instance_texture_parameter_value(mat, name) if isinstance(mat, unreal.MaterialInstance) else M.get_material_default_texture_parameter_value(mat, name)
        if tex and not isinstance(tex, unreal.Texture2D):
            # Cube maps (e.g. the unified eye's fake reflection) have no 2D exporter; record and skip.
            rec.setdefault('skippedTextures', []).append(dict(name=str(name), texture=tex.get_path_name(), cls=tex.get_class().get_name()))
        elif tex:
            textures[tex.get_path_name()] = tex
            rec['parameters'].append(dict(name=str(name), texture=tex.get_path_name()))
    for tex in M.get_material_used_textures(mat):
        if isinstance(tex, unreal.Texture2D):
            textures[tex.get_path_name()] = tex
            rec['textures'].append(tex.get_path_name())

def mesh(obj, role='body'):
    if any(x['source'] == obj.get_path_name() for x in report['models']):
        return
    skeletal = isinstance(obj, unreal.SkeletalMesh)
    rec = export(obj, 'Meshes', '.fbx', unreal.SkeletalMeshExporterFBX() if skeletal else unreal.StaticMeshExporterFBX(), options)
    rec.update(name=obj.get_name(), role=role, skeletal=skeletal, slots=[])
    for slot in get(obj, 'materials' if skeletal else 'static_materials', []):
        mat = get(slot, 'material_interface')
        rec['slots'].append(dict(name=str(get(slot, 'material_slot_name', '')), material=mat.get_path_name() if mat else ''))
        add_material(mat)
    if skeletal:
        editor = unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
        rec['sectionMaterialIndices'] = [editor.get_lod_material_slot(obj, 0, i)
                                         for i in range(editor.get_num_sections(obj, 0))]
    report['models'].append(rec)

for path in sorted(E.list_assets(SOURCE, recursive=True, include_folder=False)):
    try:
        data = E.find_asset_data(path)
        cls = str(data.asset_class_path.asset_name)
        if cls not in ('SkeletalMesh', 'StaticMesh', 'GroomAsset'):
            continue
        obj = unreal.load_asset(path)
        if cls == 'GroomAsset':
            groom = dict(source=path, cards=[])
            for card in get(obj, 'hair_groups_cards', []):
                card_mesh = get(card, 'imported_mesh')
                if card_mesh and not any(x['source'] == card_mesh.get_path_name() for x in report['models']):
                    mesh(card_mesh, 'hair')
                    groom['cards'].append(card_mesh.get_path_name())
                    record = report['models'][-1]
                    record['cardLod'] = int(get(card, 'lod_index', 0))
                    card_textures = get(card, 'textures')
                    layout = str(get(card_textures, 'layout', ''))
                    tex_list = list(get(card_textures, 'textures', []))
                    record['cardLayout'] = layout
                    if 'LAYOUT0' in layout.upper() or 'LAYOUT1' in layout.upper():
                        names = ['Depth', 'Coverage', 'Tangent', 'Attributes', 'BaseColor', 'Auxiliary']
                    elif 'LAYOUT2' in layout.upper():
                        names = ['TangentCoordU', 'Coverage', 'BaseColor']
                    else:
                        names = []
                    params = []
                    for index, tex in enumerate(tex_list):
                        if tex:
                            textures[tex.get_path_name()] = tex
                            if index < len(names):
                                params.append(dict(name=names[index], texture=tex.get_path_name()))
                    material_key = obj.get_path_name() + ':cards:' + str(record['cardLod'])
                    materials[material_key] = dict(source=material_key, name=obj.get_name() + '_HairCards_LOD' + str(record['cardLod']), parameters=params, textures=[])
                    record['slots'] = [dict(name=s['name'], material=material_key) for s in record['slots']]
            report['grooms'].append(groom)
        else:
            mesh(obj, 'face' if 'facemesh' in obj.get_name().lower() else 'body')
        save()
    except Exception:
        report['errors'].append(dict(asset=path, error=traceback.format_exc()))
        save()

# Include all character textures (animated maps may not be referenced by LOD0 material).
for path in E.list_assets(SOURCE, recursive=True, include_folder=False):
    data = E.find_asset_data(path)
    if str(data.asset_class_path.asset_name) == 'Texture2D':
        obj = unreal.load_asset(path)
        textures[obj.get_path_name()] = obj
for path, tex in sorted(textures.items()):
    try:
        suffix, exporter, source_format = texture_exporter(tex)
        rec = export(tex, 'Textures', suffix, exporter)
        rec.update(name=tex.get_name(), srgb=bool(get(tex, 'srgb', False)),
                   compression=str(get(tex, 'compression_settings', '')),
                   flipGreen=bool(get(tex, 'flip_green_channel', False)),
                   sourceFormat=source_format, storage=image_storage(OUT / rec['file']),
                   exportMethod=exporter.get_class().get_name(),
                   sourceImageOperation='Official source-image export; no requested resize or color-space conversion')
        report['textures'].append(rec)
    except Exception:
        report['errors'].append(dict(asset=path, error=traceback.format_exc()))
    save()
report['materials'] = list(materials.values())
report['complete'] = not report['errors']
report.pop('pendingExport', None)
save()
unreal.log(f'MetaHuman export: {len(report["models"])} models, {len(report["textures"])} textures, {len(report["errors"])} errors -> {OUT}')
