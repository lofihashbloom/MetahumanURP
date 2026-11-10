#!/usr/bin/env python3
"""Validate serialized angular data and a declared zero-shift/common-M furnace.

NumPy CPU test only; this is not Unity importer or GPU sampling validation.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import numpy as np


def run(path):
    data=path.read_bytes()
    if data[:8]!=b'MHFBR001':raise ValueError('Bad magic')
    width,height,slices,length=struct.unpack_from('<IIII',data,8)
    metadata=json.loads(data[24:24+length])
    table=np.frombuffer(data,offset=24+length,dtype='<f2').astype(np.float64).reshape(slices,height,width,4)
    if not np.isfinite(table).all() or table.min()<0:raise ValueError('Invalid density data')
    mass=table[:,:,:,:3].sum(axis=2)*(2*np.pi/width)
    theta_rows=np.linspace(0,np.pi/2,height)
    s,w=np.polynomial.legendre.leggauss(512);ci=np.sqrt(1-s*s);theta_i=np.arcsin(s)
    furnace=[];unequal=[]
    for view in [0,30,60,85]:
        so=np.sin(np.radians(view));co=np.cos(np.radians(view));theta_d=abs(theta_i-np.radians(view))*.5
        sampled_mass=np.stack([[np.interp(theta_d,theta_rows,mass[p,:,c]) for c in range(3)] for p in range(4)],axis=0)
        for variance in [.01,.1,.5]:
            # Independent direct double-precision Bessel expression, without the
            # shader's asymptotic LogI0 implementation.
            m=np.exp(-s*so/variance)*np.i0(ci*co/variance)/(2*variance*np.sinh(1/variance))
            value=np.sum(w[None,:]*m[None,:]*sampled_mass.sum(axis=0),axis=1)
            furnace.append(dict(viewDegrees=view,commonVariance=variance,rgb=value.tolist()))
            variances=[variance,variance*.25,variance*4,variance*4]
            response=[]
            for p,v in enumerate(variances):
                mp=np.exp(-s*so/v)*np.i0(ci*co/v)/(2*v*np.sinh(1/v))
                response.append(np.sum(w[None,:]*mp[None,:]*sampled_mass[p],axis=1))
            unequal.append(dict(viewDegrees=view,baseVariance=variance,pathVariances=variances,rgb=np.sum(response,axis=0).tolist()))
    unit=max(abs(np.asarray(metadata['sigmaA'])))<1e-12
    common=np.array([q['rgb'] for q in furnace]);different=np.array([q['rgb'] for q in unequal])
    reciprocity=float(np.max(abs(table[:,:,:,:3]-table[:,:,::-1,:3])))
    report=dict(file=str(path.resolve()),sha256=hashlib.sha256(data).hexdigest(),profileName=metadata['profileName'],
        gpuValidated=False,importerValidated=False,finiteAndNonnegative=True,
        azimuthReversalSymmetryError=reciprocity,commonVarianceFurnace=furnace,
        commonVarianceMaxEnergy=float(common.max()),commonVarianceMinEnergy=float(common.min()),
        unitCommonVarianceMaxFurnaceError=float(np.max(abs(common-1))) if unit else None,
        unequalWidthDiagnostic=unequal,unequalWidthMaximumEnergy=float(different.max()),
        unequalWidthUnitMaximumFurnaceError=float(np.max(abs(different-1))) if unit else None,
        scope='Common longitudinal variance and zero cuticle shift are validated; independently changing per-path widths is a separate diagnostic, not an energy-conservation claim.',
        passed=bool(reciprocity<1e-5 and common.max()<=1.001 and common.min()>=0 and (not unit or np.max(abs(common-1))<.001)))
    path.with_name(path.stem+'-Validation.json').write_text(json.dumps(report,indent=2,allow_nan=False)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ['commonVarianceFurnace','unequalWidthDiagnostic']},indent=2))
    if not report['passed']:raise SystemExit(1)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('table',type=Path);run(parser.parse_args().table)
