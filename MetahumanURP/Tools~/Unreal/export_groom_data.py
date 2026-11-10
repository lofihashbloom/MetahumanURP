"""Read-only original groom data export via our independently authored API bridge."""
import os
import hashlib
import json
from pathlib import Path
import unreal

asset_path = os.environ['METAHUMAN_GROOM_ASSET']
OUT = Path(os.environ['METAHUMAN_GROOM_EXPORT_DIR']).resolve()
OUT.mkdir(parents=True, exist_ok=True)
package = asset_path.split('.')[0]
assert package.startswith('/Game/'), 'Only project-owned /Game grooms are supported by this helper'
source_file = Path(unreal.Paths.project_content_dir()) / (package[len('/Game/'):] + '.uasset')
filename = package.rsplit('/', 1)[-1] + '.mhgroom'
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
before = sha(source_file)
groom = unreal.load_asset(asset_path)
assert groom, asset_path
assert unreal.GroomDataExportLibrary.export_groom_data(asset_path, str(OUT / filename))
rendering = []
for group in groom.get_editor_property('hair_groups_rendering'):
    item = {}
    for name in ('geometry_settings', 'material'):
        try:
            value = group.get_editor_property(name)
            if name == 'geometry_settings':
                item[name] = {key: value.get_editor_property(key) for key in ('hair_width', 'hair_root_scale', 'hair_tip_scale', 'hair_width_override')}
            else: item[name] = value.get_path_name() if value else None
        except Exception as e: item[name] = str(e)
    rendering.append(item)
after = sha(source_file)
assert before == after, 'Source asset was unexpectedly changed'
receipt = dict(source=asset_path, sourceSha256=before, sourceUnchanged=True,
    binarySha256=sha(OUT/filename), engine=unreal.SystemLibrary.get_engine_version(),
    format='MHGRM001', coordinateSpace='Original groom asset-local centimetres; no renderer geometry or groom-binding transform baked',
    rendering=rendering, assetsSaved=False, implementation='Original public-API data exporter; no Unreal renderer implementation copied')
(OUT/'GroomExportReceipt.json').write_text(json.dumps(receipt, indent=2))
unreal.log('METAHUMAN_GROOM_EXPORT_COMPLETE ' + json.dumps(receipt))
