#!/usr/bin/env python3
"""Independent far-field fiber angular tables from published cylinder optics.

Requires NumPy only. No Unreal/Unity renderer or source asset implementation is
used. Output .mhfiber is linear RGBAHalf: slices R,TT,TRT,higher-internal-orders.
The table is fixed to eta, azimuth roughness and absorption; no opacity is baked.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import numpy as np

MAGIC=b'MHFBR001'
VERSION=1
PI=math.pi

def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def sigmoid(x):
    return 1/(1+np.exp(-np.clip(x,-80,80)))

def logistic_cdf(x,scale):
    low=sigmoid(-PI/scale);normal=sigmoid(PI/scale)-low
    return (sigmoid(np.clip(x,-PI,PI)/scale)-low)/normal

def detector_bin_density(phi,phase,scale,bin_width):
    # Periodic cell-integrated density. Its discrete angular integral is exactly
    # normalized even when a narrow detector occupies fewer than two texels.
    d=(phi-phase+PI)%(2*PI)-PI
    lo=d-bin_width*.5;hi=d+bin_width*.5
    mass=logistic_cdf(hi,scale)-logistic_cdf(lo,scale)
    mass+=np.where(hi>PI,logistic_cdf(hi-2*PI,scale),0)
    mass+=np.where(lo<-PI,1-logistic_cdf(lo+2*PI,scale),0)
    return np.maximum(mass,0)/bin_width

def fresnel(cosine,eta):
    ci=np.clip(cosine,0,1);ct=np.sqrt(np.maximum(1-(1-ci*ci)/(eta*eta),0))
    rs=(ci-eta*ct)/np.maximum(ci+eta*ct,1e-15)
    rp=(eta*ci-ct)/np.maximum(eta*ci+ct,1e-15)
    return .5*(rs*rs+rp*rp)

def profile_absorption(args):
    if args.sigma_a is not None:return np.asarray(args.sigma_a),dict(mode='diameterNormalizedSigmaA',input=args.sigma_a)
    if args.pigments is not None:
        ce,cp=args.pigments
        return ce*np.array([.419,.697,1.37])+cp*np.array([.187,.4,1.05]),dict(mode='publishedPigmentConcentrations',eumelanin=ce,pheomelanin=cp)
    color=np.asarray(args.reflectance)
    # Chiang etal.2016 Eq9. Input is an explicitly declared appearance reflectance,
    # not a claim that arbitrary native BaseColor has the same albedo semantics.
    b=args.beta_n;denom=5.969-.215*b+2.532*b*b-10.73*b**3+5.574*b**4+.245*b**5
    return (np.log(np.clip(color,1e-6,1))/denom)**2,dict(mode='publishedReflectanceInversion',input=args.reflectance,equation='Chiang2016 Eq9',nativeBaseColorEquivalenceClaimed=False)

def path_data(theta,eta,absorption,h):
    cd=math.cos(theta);sd=math.sin(theta)
    eta_prime=math.sqrt(max(eta*eta-sd*sd,0))/max(cd,1e-12)
    gi=np.arcsin(h);gt=np.arcsin(np.clip(h/eta_prime,-1,1))
    cos_t=math.sqrt(max(1-(sd/eta)**2,1e-12))
    length=np.cos(gt)/cos_t # distance in units of physical fiber diameter
    t=np.exp(-length[:,None]*absorption[None,:])
    f=fresnel(max(cd,0)*np.sqrt(np.maximum(1-h*h,0)),eta)[:,None]
    ar=np.repeat(f,3,axis=1);att=(1-f)**2*t;atrt=att*f*t
    tail=atrt*f*t/np.maximum(1-f*t,1e-15)
    return [ar,att,atrt,tail],gi,gt

def row(theta,eta,absorption,scale,width,h,weights):
    phi=-PI+(np.arange(width)+.5)*(2*PI/width)
    energies,gi,gt=path_data(theta,eta,absorption,h)
    output=np.zeros((4,width,3),np.float64);mass=np.zeros((4,3),np.float64)
    for p in range(4):
        weighted=energies[p]*weights[:,None]*.5
        mass[p]=np.sum(weighted,axis=0)
        if p==3:
            output[p]=mass[p]/(2*PI) # declared uniform higher-order azimuth closure
        else:
            phase=2*p*gt-2*gi+p*PI
            detector=detector_bin_density(phi[:,None],phase[None,:],scale,2*PI/width)
            output[p]=np.sum(detector[:,:,None]*weighted[None,:,:],axis=1)
    return output,mass

def generate(args):
    absorption,adapter=profile_absorption(args)
    if not np.isfinite(absorption).all() or (absorption<0).any():raise ValueError('Invalid absorption.')
    if args.status=='source-input' and not args.source_receipt:raise ValueError('source-input requires explicit source receipt.')
    if args.eta<=1 or not 0<args.beta_n<=1:raise ValueError('eta>1 and betaN in(0,1] required.')
    if not 32<=args.width<=2048 or not 16<=args.height<=1024 or not 16<=args.quadrature<=512:raise ValueError('Grid/quadrature outside bounded prototype domain.')
    # Chiang et al. 2016, Eq. 8 gives the logistic scale as sqrt(pi/8) x this polynomial. The lookups shipped with the
    # package were generated without that factor (--logistic-factor 1), so their azimuthal lobes are about 1.6x wider
    # than the paper's; the hair look was tuned and validated on those tables. Pass --logistic-factor 0.6267 for the
    # paper's width (the tables and anything fitted to them must then be regenerated together).
    scale=args.logistic_factor*(.265*args.beta_n+1.194*args.beta_n**2+5.372*args.beta_n**22)
    h,w=np.polynomial.legendre.leggauss(args.quadrature)
    table=np.zeros((4,args.height,args.width,4),np.float64);expected=[]
    for y,theta in enumerate(np.linspace(0,PI*.5,args.height)):
        densities,mass=row(theta,args.eta,absorption,scale,args.width,h,w)
        table[:,y,:,:3]=densities;expected.append(mass)
    expected=np.stack(expected,axis=1)
    packed=table.astype('<f2')
    if not np.isfinite(packed).all():raise ValueError('Half precision overflow.')
    actual=packed[:,:,:,:3].astype(np.float64).sum(axis=2)*(2*PI/args.width)
    row_error=float(np.max(np.abs(actual-expected)))
    sum_energy=expected.sum(axis=0)
    # Independent dense midpoint impact integration at selected theta rows.
    dense_h=-1+(np.arange(8192)+.5)*(2/8192);dense_w=np.full(8192,2/8192)
    comparisons=[]
    # Samples span path lobes, wrapping boundary and caustic neighborhoods.
    sample_indices=np.unique(np.rint(np.linspace(0,args.width-1,33)).astype(int))
    for y in [0,args.height//2,args.height-1]:
        theta=y/(args.height-1)*PI*.5
        # Evaluate only the selected bins with the same exact bin integrals and
        # independently sampled uniform h, instead of reusing Gauss nodes.
        e,gi,gt=path_data(theta,args.eta,absorption,dense_h)
        phi=(-PI+(sample_indices+.5)*(2*PI/args.width))[:,None]
        for p in range(3):
            det=detector_bin_density(phi,(2*p*gt-2*gi+p*PI)[None,:],scale,2*PI/args.width)
            reference=np.sum(det[:,:,None]*e[p][None,:,:],axis=1)/8192
            delta=table[p,y,sample_indices,:3]-reference
            comparisons.append(dict(row=int(y),path=p,maxAbsoluteDensityError=float(abs(delta).max()),rmsDensityError=float(np.sqrt(np.mean(delta**2)))))
    quadrature_error=max(x['maxAbsoluteDensityError'] for x in comparisons)
    is_unit=bool(np.max(abs(absorption))<1e-12)
    energy_error=float(np.max(abs(sum_energy-1))) if is_unit else None
    validation=dict(rowMassMaxHalfQuantizationError=row_error,maximumTotalPathEnergy=float(sum_energy.max()),minimumTotalPathEnergy=float(sum_energy.min()),
        unitAbsorptionEnergyError=energy_error,denseMidpointComparisons=comparisons,maximumDensityErrorVsDenseMidpoint=quadrature_error,
        passed=bool(row_error<5e-4 and sum_energy.min()>=0 and sum_energy.max()<=1+1e-10 and (not is_unit or energy_error<1e-10) and quadrature_error<.003),
        scope='Azimuth/path energy only. Full BSDF furnace requires longitudinal kernel, cuticle shifts and runtime light adapter validation; unequal longitudinal lobe widths are not validated here.')
    source=None
    if args.source_receipt:source=dict(file=str(Path(args.source_receipt).resolve()),sha256=digest(args.source_receipt))
    metadata=dict(formatVersion=VERSION,profileName=args.name,profileStatus=args.status,verifiedNativePigment=False,
        width=args.width,height=args.height,slices=4,eta=args.eta,betaN=args.beta_n,logisticScale=scale,sigmaA=absorption.tolist(),
        absorptionUnits='Inverse physical fiber diameter',absorptionAdapter=adapter,sourceReceipt=source,
        azimuthDomainRadians=[-PI,PI],inclinationDifferenceDomainRadians=[0,PI*.5],xSampling='Periodic cell averages atcenter(-pi+(x+.5)*2pi/width)',
        ySampling='Endpoint rows thetaDifference=y*pi/(2*(height-1)); runtime V=(.5+normalizedY*(height-1))/height',
        pixelFormat='RGBAHalf little-endian; slices R/TT/TRT/higherOrders; alpha0 is NOT opacity',quadrature=args.quadrature,
        higherOrderClosure='Uniform azimuth, all internal paths>=3; single-fiber internal scattering only',
        perStrandPigmentVariationSupported=False,perStrandAzimuthRoughnessVariationSupported=False,
        rebakeWhen=['eta','betaN','sigmaA or pigment/reflectance adapter input','azimuth detector/closure','grid or quadrature'],
        generatorSha256=digest(__file__),validation=validation)
    if args.status!='source-input':metadata['nativeMatchClaimed']=False
    if not validation['passed']:raise ValueError('Validation failed: '+json.dumps(validation))
    encoded=json.dumps(metadata,separators=(',',':'),allow_nan=False).encode()
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(MAGIC+struct.pack('<IIII',args.width,args.height,4,len(encoded))+encoded+packed.tobytes())
    receipt=metadata|dict(file=str(args.output.resolve()),bytes=args.output.stat().st_size,sha256=digest(args.output))
    args.output.with_suffix('.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps({k:receipt[k] for k in ['profileName','profileStatus','sigmaA','bytes','sha256','validation']},indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    group=parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--sigma-a',nargs=3,type=float);group.add_argument('--pigments',nargs=2,type=float);group.add_argument('--reflectance',nargs=3,type=float)
    parser.add_argument('--eta',type=float,default=1.55);parser.add_argument('--beta-n',type=float,default=.37)
    parser.add_argument('--logistic-factor',type=float,default=1.0,help='multiplier on the Chiang Eq. 8 polynomial; 1.0 reproduces the shipped tables, 0.6267 (sqrt(pi/8)) is the published scale')
    parser.add_argument('--width',type=int,default=512);parser.add_argument('--height',type=int,default=128);parser.add_argument('--quadrature',type=int,default=64)
    parser.add_argument('--name',required=True);parser.add_argument('--status',choices=['unit-validation','diagnostic','source-input'],required=True)
    parser.add_argument('--source-receipt',type=Path);parser.add_argument('--output',type=Path,required=True)
    generate(parser.parse_args())
