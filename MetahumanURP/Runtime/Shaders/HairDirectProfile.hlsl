// Independent profile inferred from public component-isolated unit renders.
// Profile/provenance: Docs/HairRendering.md. No engine renderer code.
// Returns cosine-weighted angular kernels; URP applies PI once.
#ifndef METAHUMAN_HAIR_DIRECT_PROFILE_INCLUDED
#define METAHUMAN_HAIR_DIRECT_PROFILE_INCLUDED
// Empirical matching profile inferred only from public unit-material captures.
// TT:26 train/128 excluded; TRT:40 train/149 excluded. No head/exposure fit.
// Eta1.55; validated roughness .261472..478582. Local scattering absent.
// Unit-area sine Gaussians and von Mises phases; no engine implementation copied.

float MHHairProfileGaussian(float coordinate,float sigma)
{
    sigma=max(sigma,.01);
    return exp(-.5*coordinate*coordinate/(sigma*sigma))/(2.506628274631*sigma);
}
float MHHairProfileSchlick(float cosine,float eta)
{
    float ratio=(max(eta,1.001)-1)/(max(eta,1.001)+1);
    float f0=ratio*ratio;
    return f0+(1-f0)*pow(1-saturate(cosine),5);
}
void MHHairProfilePathKernels(MHHairFiberAngles a,float roughness,float3 color,float eta,
                    out float3 tt,out float3 trt)
{
    float cosPhi=cos(a.azimuth);
    float cosD=max(a.cosDifference,1e-4);
    float etaPrime2=max(eta*eta-1+cosD*cosD,1e-6)/(cosD*cosD);
    float impactA=min(1.45997177025*(1+cosPhi),0.913007481681);
    float powerTT=.5*sqrt(max(0,1-impactA/etaPrime2))/cosD;
    float impactF=min(1.06536637061*(1+cosPhi),0.938252421007);
    float fTT=MHHairProfileSchlick(cosD*sqrt(max(0,1-impactF)),eta);
    float mTT=MHHairProfileGaussian(a.sinLight+a.sinView-0.0348994967025,.5*roughness*roughness);
    float nTT=0.741595243156*exp(3.7450972791*(-cosPhi-1));
    tt=mTT*nTT*0.95464676744*(1-fTT)*(1-fTT)*pow(saturate(color),powerTT);

    float mTRT=MHHairProfileGaussian(a.sinLight+a.sinView-0.13917310096,2*roughness*roughness);
    float nTRT=1.63245381749*exp(17*(cosPhi-1));
    float fTRT=MHHairProfileSchlick(0.606*cosD,eta);
    trt=mTRT*nTRT*fTRT*(1-fTRT)*(1-fTRT)*pow(saturate(color),.8/cosD);
}


#endif
