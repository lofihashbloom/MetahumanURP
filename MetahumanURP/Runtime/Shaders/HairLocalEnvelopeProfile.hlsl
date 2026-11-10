#ifndef MH_HAIR_LOCAL_ENVELOPE_PROFILE_INCLUDED
#define MH_HAIR_LOCAL_ENVELOPE_PROFILE_INCLUDED
// Original random-walk Gaussian response envelope, learned only from9nativeunitGrey004rows.
// No renderer function or nativeLUT coefficients copied. Roughness .37 reference only.
// Returns K; caller applies PI once. Positivefinite and localalone mass bound documented.
// Incident-prefactor makes response nonreciprocal. This is a material-profile adapter.
float3 MHHairLocalEnvelope037(float sinLight,float sinView,float3 sourceColor)
{
 float sl=clamp(sinLight,-1.0,1.0),sv=clamp(sinView,-1.0,1.0);
 float chi=sl*sl+sv*sv;
 float floorRaw=0.000563946935892+chi*(2.63423367763e-5+chi*0.000314610886343);
 float h=.5*(asin(sl)+asin(sv));
 float response=exp(-3.875773705145596+0.6994316213574591*sl*sl+-0.6614758278994738*sl*sl*sl*sl-.5*4.642935073400122*h*h);
 float3 t=saturate((saturate(sourceColor)-0.0009765625)/(1.0-0.0009765625));
 float3 colorEnergy=1.0-pow(1.0-t,2.19086152435);
 return (floorRaw+response*(colorEnergy/0.08359308130661813))*0.3183098861837907;
}
#endif
