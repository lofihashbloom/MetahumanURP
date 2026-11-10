"""Recover an uncooked legacy UDNAAsset's standalone behavior stream.

This deliberately supports only the v2.1 behavior / v2.4 geometry serialization
layout verified for Kellan. It does not parse arbitrary Unreal packages or cook
formats. Run the output through OpenRigLogic and validate its neutral binding.
"""
import argparse
import hashlib
import json
from pathlib import Path


def recover(asset, output, manifest=None):
    source = Path(asset).resolve()
    data = source.read_bytes()
    signature = b'DNA\x00\x02\x00\x01'
    starts = [i for i in range(len(data)) if data.startswith(signature, i)]
    if len(starts) != 1:
        raise ValueError('Expected exactly one legacy DNA v2.1 behavior stream')
    start = starts[0]
    boundary = data.find(b'ANDDNA\x00\x02\x00\x04', start)
    if boundary < 0:
        raise ValueError('Expected contiguous behavior EOF and v2.4 geometry stream')
    dna = data[start:boundary + 3]
    target = Path(output).resolve()
    if not target.name.endswith('.bytes'):
        raise ValueError('Use a .bytes extension so Unity imports the binary as a TextAsset')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(dna)
    receipt = dict(method='Recovered legacy UDNAAsset standalone behavior stream; geometry remains in source asset',
                   source=str(source), sourceSha256=hashlib.sha256(data).hexdigest(),
                   offset=start, length=len(dna), sha256=hashlib.sha256(dna).hexdigest(),
                   requiresNativeAndNeutralBindingValidation=True)
    target.with_suffix('.receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    if manifest:
        manifest_path = Path(manifest).resolve()
        bundle = json.loads(manifest_path.read_text())
        relative = target.relative_to(manifest_path.parent).as_posix()
        bundle['dna'] = dict(file=relative, sha256=receipt['sha256'], coordinateConvention='LegacyUnrealFbx')
        manifest_path.write_text(json.dumps(bundle, indent=2) + '\n')
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--asset', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--manifest')
    args = parser.parse_args()
    print(json.dumps(recover(args.asset, args.output, args.manifest), indent=2))
