#!/usr/bin/env python3
"""Build static hair-to-skin receiver shadow fields from a source groom (.mhgroom).

Independent model: each render-curve segment deposits its projected fiber area (diameter x length x
|tangent x ray|, divided by the light-space angular cell area) into a perspective light-space grid.
The cumulative sum along depth is stored as N+1 boundary-prefix planes (entry 0, terminal last), which
HairReceiverShadow.hlsl turns into Beer/Poisson transmission exp(-density * tau). No engine shadow code
or coefficients are used.

Inputs:
  --groom    .mhgroom exported by GroomExportBridge (Unreal centimetres, Z up)
  --lights   JSON list of {"name": str, "position": [x, y, z]} in the volume's Unity space (metres)
  --output   folder for <name>-OpticalDepth<N>.f32 files and Fields.json
Coordinates: groom points map to Unity as (x, z, -y) * 0.01, i.e. the character root as imported.
Place HairReceiverShadowVolume on that root (unit scale). Fields are rest pose only.
Requires Python 3 and NumPy.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

CURVE = np.dtype([('first', '<i4'), ('count', '<i4'), ('guide', '<i4'), ('group', '<i4'), ('uv', '<f4', 2), ('width', '<f4')])


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def load_groom(path, max_points):
    b = Path(path).read_bytes()
    if b[:8] != b'MHGRM001':
        raise SystemExit('Not an MHGRM001 groom export')
    n, m = np.frombuffer(b, dtype='<i4', count=2, offset=8)
    if len(b) != 16 + 28 * n + 16 * m:
        raise SystemExit('Truncated groom export')
    curves = np.frombuffer(b, dtype=CURVE, count=n, offset=16)
    pts = np.frombuffer(b, dtype='<f4', count=m * 4, offset=16 + n * 28).reshape(m, 4)
    render = curves[curves['guide'] == 0]
    ids = np.concatenate([c['first'] + np.rint(np.linspace(0, c['count'] - 1, min(max_points, c['count']))).astype(int) for c in render])
    ends = np.cumsum(np.minimum(max_points, render['count'])) - 1
    valid = np.ones(len(ids) - 1, dtype=bool)
    valid[ends[:-1]] = False
    maxwidth = max(pts[c['first']:c['first'] + c['count'], 3].max() for c in render)
    return pts, ids, valid, maxwidth


def deposit(coords, weights, n):
    """Trilinear deposition into an n^3 grid (z, y, x order)."""
    q = np.clip(coords * n - .5, 0, n - 1)
    a = np.floor(q).astype(int)
    f = q - a
    out = np.zeros(n ** 3, dtype=float)
    for dz in (0, 1):
        for dy in (0, 1):
            for dx in (0, 1):
                corner = np.array([dx, dy, dz])
                c = np.minimum(a + corner, n - 1)
                w = weights * np.prod(np.where(corner, f, 1 - f), axis=1)
                out += np.bincount((c[:, 2] * n + c[:, 1]) * n + c[:, 0], weights=w, minlength=n ** 3)
    return out.reshape(n, n, n)


def build(groom, lights, output, resolution=128, diameter=.00012, max_points=64, taper=None, tip_scale=.8):
    pts, ids, valid, maxwidth = load_groom(groom, max_points)
    world = pts[ids, :3].astype(float)[:, [0, 2, 1]] * np.array([.01, .01, -.01])
    # Optional per-point taper parameter u in [0,1] (uint8, source point order); diameter scales 1 -> tip_scale.
    u = np.zeros(len(pts)) if taper is None else np.fromfile(taper, dtype=np.uint8).astype(float) / 255
    if len(u) != len(pts):
        raise SystemExit('Taper file must have one byte per groom point')
    d = diameter * (1 - (1 - tip_scale) * u[ids]) * pts[ids, 3] / maxwidth
    p0, p1 = world[:-1][valid], world[1:][valid]
    d0, d1 = d[:-1][valid], d[1:][valid]
    seg = p1 - p0
    length = np.linalg.norm(seg, axis=1)
    tangent = seg / np.maximum(length[:, None], 1e-20)
    center = world.mean(0)
    out = Path(output)
    out.mkdir(parents=True, exist_ok=True)
    rows = []
    for light in lights:
        origin = np.array(light['position'], dtype=float)
        F = center - origin
        F /= np.linalg.norm(F)
        right = np.cross(F, [0, 1, 0])
        right /= np.linalg.norm(right)
        up = np.cross(right, F)
        rel = world - origin
        z = np.sum(rel * F, axis=1)
        angles = np.column_stack([np.sum(rel * right, axis=1) / z, np.sum(rel * up, axis=1) / z])
        lo, hi = angles.min(0) - .005, angles.max(0) + .005
        zmin, zmax = z.min() - .005, z.max() + .005

        def project(points):
            r = points - origin
            dd = np.sum(r * F, axis=1)
            return np.column_stack([((np.sum(r * right, axis=1) / dd) - lo[0]) / (hi[0] - lo[0]),
                                    ((np.sum(r * up, axis=1) / dd) - lo[1]) / (hi[1] - lo[1]), (dd - zmin) / (zmax - zmin)])

        # Subdivide so no sample spans more than half a cell.
        span = np.max(np.abs(project(p1) - project(p0)) * resolution, axis=1)
        split = np.maximum(1, np.ceil(2 * span).astype(int))
        idx = np.repeat(np.arange(len(length)), split)
        start = np.repeat(np.cumsum(split) - split, split)
        t = (np.arange(len(idx)) - start + .5) / split[idx]
        samples = p0[idx] + seg[idx] * t[:, None]
        sample_d = d0[idx] + (d1[idx] - d0[idx]) * t
        rel = samples - origin
        depth = np.sum(rel * F, axis=1)
        ray = rel / np.linalg.norm(rel, axis=1)[:, None]
        sin = np.linalg.norm(np.cross(tangent[idx], ray), axis=1)
        # Perpendicular beam area = z^2 du dv cos(ray, F).
        angular = sample_d * (length[idx] / split[idx]) * sin / (depth ** 2 * np.sum(ray * F, axis=1))
        coords = project(samples)
        cell = np.prod(hi - lo) / (resolution * resolution)
        tau = deposit(coords, angular / cell, resolution)
        prefix = np.concatenate([np.zeros((1, resolution, resolution)), np.cumsum(tau, axis=0)])
        mass = tau.sum() * cell
        if abs(mass / angular.sum() - 1) > 1e-9:
            raise SystemExit('Projected-area conservation failed')
        name = '%s-OpticalDepth%d.f32' % (light['name'], resolution)
        prefix.astype('<f4').tofile(out / name)
        rows.append({'name': light['name'], 'file': name, 'sha256': sha(out / name), 'resolution': resolution,
                     'shapeXYZ': [resolution, resolution, resolution + 1],
                     'originZMin': [*origin.tolist(), float(zmin)], 'rightUMin': [*right.tolist(), float(lo[0])],
                     'upVMin': [*up.tolist(), float(lo[1])], 'forwardZMax': [*F.tolist(), float(zmax)],
                     'scales': [float(1 / (hi[0] - lo[0])), float(1 / (hi[1] - lo[1])), float(1 / (zmax - zmin))],
                     'meanExitTransmission': float(np.exp(-prefix[-1]).mean()), 'maxOpticalDepth': float(prefix.max())})
    receipt = {'groom': str(groom), 'groomSha256': sha(groom), 'diameterMetres': diameter, 'maxPointsPerCurve': max_points,
               'taper': str(taper) if taper else None, 'tipScale': tip_scale, 'resolution': resolution,
               'model': 'Projected fiber area, trilinear deposition, boundary-prefix optical depth, exp(-density*tau)',
               'limitations': ['Static rest geometry', 'Fiber color/transmission not modeled', 'Rebuild after light or groom changes'],
               'fields': rows}
    (out / 'Fields.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--groom', required=True)
    parser.add_argument('--lights', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--resolution', type=int, default=128)
    parser.add_argument('--diameter', type=float, default=.00012, help='Root fiber diameter in metres')
    parser.add_argument('--max-points', type=int, default=64)
    parser.add_argument('--taper', help='Optional uint8 per-point taper parameter file')
    parser.add_argument('--tip-scale', type=float, default=.8)
    a = parser.parse_args()
    lights = json.loads(Path(a.lights).read_text())
    r = build(a.groom, lights, a.output, a.resolution, a.diameter, a.max_points, a.taper, a.tip_scale)
    print(json.dumps([{k: f[k] for k in ('name', 'file', 'meanExitTransmission')} for f in r['fields']], indent=2))
