"""Build the bridge against the exact OpenRigLogic source used by this package.

Requires CMake and a C++ compiler. Does not download sources or install tools.
"""
import argparse
import hashlib
import json
import platform
import shutil
import subprocess
from pathlib import Path

PIN = 'd3d329bbd6992ac38adc1c1b24203ceb998f75c0'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source', type=Path, required=True)
parser.add_argument('--build-dir', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--cmake', default='cmake')
args = parser.parse_args()
source = args.source.resolve()
revision = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
if revision != PIN:
    raise SystemExit(f'Expected OpenRigLogic commit {PIN}, found {revision}. No silent version fallback.')
if subprocess.run(['git', '-C', str(source), 'diff', '--quiet', 'HEAD', '--']).returncode:
    raise SystemExit('Tracked OpenRigLogic sources differ from the pinned revision; use a clean checkout for a reproducible build.')
build = args.build_dir.resolve()
command = [args.cmake, '-S', str(Path(__file__).parent.resolve()), '-B', str(build),
           '-DOPEN_RIGLOGIC_SOURCE=' + str(source), '-DCMAKE_BUILD_TYPE=Release']
if platform.system() == 'Darwin' and platform.machine() == 'arm64':
    command.append('-DRL_AUTODETECT_NEON=ON')
subprocess.run(command, check=True)
subprocess.run([args.cmake, '--build', str(build), '--config', 'Release', '--parallel', '2'], check=True)
extension = {'Darwin': '.dylib', 'Windows': '.dll', 'Linux': '.so'}[platform.system()]
products = list(build.glob('*metahuman_riglogic' + extension)) + list((build / 'Release').glob('*metahuman_riglogic' + extension))
if len(products) != 1:
    raise SystemExit(f'Expected one native bridge product, found {products}')
args.output.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(products[0], args.output)
args.output.with_suffix(args.output.suffix + '.build.json').write_text(json.dumps(dict(
    openRigLogicCommit=revision, platform=platform.system(), architecture=platform.machine(),
    abiVersion=1, binarySha256=hashlib.sha256(args.output.read_bytes()).hexdigest()), indent=2) + '\n')
print(args.output.resolve())
