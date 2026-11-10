#ifndef METAHUMAN_HAIR_FIBER_SCATTERING_INCLUDED
#define METAHUMAN_HAIR_FIBER_SCATTERING_INCLUDED

// Independently authored fiber mathematics from published scattering models.
// References/provenance: Validation/GroomMatched/HairScatteringPlan-v10.md.
// No Unreal renderer implementation is included or translated here.
//
// Functions return cosine-weighted angular response K, not a surface BRDF.
// The URP light adapter applies PI exactly once and no ribbon NdotL factor.
// Coverage, shadowing, optical-depth compositing and pigment-control adaptation
// are deliberately outside this module. All angular/energy arithmetic is float.

#define MH_FIBER_PI 3.14159265358979323846

struct MHHairFiberAngles
{
    float sinLight, cosLight;
    float sinView, cosView;
    float azimuth;
    float cosDifference;
};

MHHairFiberAngles MHHairFiberGetAngles(float3 tangent, float3 toLight, float3 toView)
{
    MHHairFiberAngles a;
    float3 t = normalize(tangent);
    float3 l = normalize(toLight);
    float3 v = normalize(toView);
    a.sinLight = clamp(dot(t, l), -1.0, 1.0);
    a.sinView = clamp(dot(t, v), -1.0, 1.0);
    a.cosLight = sqrt(max(0.0, 1.0 - a.sinLight * a.sinLight));
    a.cosView = sqrt(max(0.0, 1.0 - a.sinView * a.sinView));
    float3 lp = l - t * a.sinLight;
    float3 vp = v - t * a.sinView;
    float x = dot(lp, vp);
    float y = dot(t, cross(lp, vp));
    // Relative azimuth is undefined at an axial direction; choose a finite
    // convention there. The angular distribution remains bounded.
    a.azimuth = (a.cosLight * a.cosView > 1e-7) ? atan2(y, x) : 0.0;
    a.cosDifference = sqrt(saturate(0.5 * (1.0 + a.cosLight * a.cosView
                                               + a.sinLight * a.sinView)));
    return a;
}

float MHHairFiberLogI0(float x)
{
    x = abs(x);
    if (x > 12.0)
    {
        float r = rcp(x);
        float correction = 1.0 + r * (0.125 + r * (0.0703125
                            + r * (0.0732421875 + r * 0.112152099609375)));
        return x - 0.5 * log(2.0 * MH_FIBER_PI * x) + log(correction);
    }
    float q = 0.25 * x * x;
    float term = 1.0;
    float sum = 1.0;
    [unroll] for (int k = 1; k <= 16; ++k)
    {
        term *= q / float(k * k);
        sum += term;
    }
    return log(sum);
}

// Spherical-Gaussian convolution longitudinal density (d'Eon et al.2011, Eq7).
// At zero shift, integral M(thetaLight,thetaView)*cos(thetaView)dthetaView = 1.
// variance is squared angular width, not an unlabelled artist roughness value.
float MHHairFiberLongitudinal(float sinLight, float cosLight,
                             float sinView, float cosView, float variance)
{
    float v = clamp(variance, 1e-4, 4.0);
    float inverse = rcp(v);
    float b = sinLight * sinView * inverse;
    float x = abs(cosLight * cosView) * inverse;
    // log(2*v*sinh(1/v)) without evaluating an overflowing sinh.
    float logDenominator = log(v) + inverse + log(max(1.0 - exp(-2.0 * inverse), 1e-20));
    float logDensity = MHHairFiberLogI0(x) - b - logDenominator;
    return exp(min(logDensity, 80.0));
}

// Shift is the total longitudinal direction-sum shift, in radians. Its mapping
// from cuticle tilt must be declared by the model adapter; it is not a source
// artist roughness or a world-space tangent offset.
float MHHairFiberShiftedLongitudinal(MHHairFiberAngles a, float variance, float shift)
{
    float s, c;
    sincos(shift, s, c);
    float sinShifted = a.sinLight * c - a.cosLight * s;
    float cosShifted = a.cosLight * c + a.sinLight * s;
    return MHHairFiberLongitudinal(sinShifted, cosShifted, a.sinView, a.cosView, variance);
}

float MHHairFiberFresnel(float cosineIncident, float eta)
{
    float ci = saturate(abs(cosineIncident));
    eta = max(eta, 1.001);
    float st2 = (1.0 - ci * ci) / (eta * eta);
    float ct = sqrt(max(0.0, 1.0 - st2));
    float rs = (ci - eta * ct) / max(ci + eta * ct, 1e-7);
    float rp = (eta * ci - ct) / max(eta * ci + ct, 1e-7);
    return saturate(0.5 * (rs * rs + rp * rp));
}

// Far-field circular-fiber surface reflection: impact-parameter Jacobian and
// Fresnel. No pigment tint and no arbitrary second reflection are added.
float MHHairFiberReflectionAzimuth(MHHairFiberAngles a, float eta)
{
    float cosHalfAzimuth = max(0.0, cos(0.5 * a.azimuth));
    float fresnel = MHHairFiberFresnel(a.cosDifference * cosHalfAzimuth, eta);
    return 0.25 * cosHalfAzimuth * fresnel;
}

float3 MHHairFiberEvaluateR(MHHairFiberAngles a, float varianceR,
                          float directionSumShiftR, float eta, float reflectionScale)
{
    float m = MHHairFiberShiftedLongitudinal(a, varianceR, directionSumShiftR);
    float n = MHHairFiberReflectionAzimuth(a, eta);
    return (m * n * saturate(reflectionScale)).xxx;
}

// Independent projected-sine Gaussian candidate inferred from public rendered
// unit-Hair response measurements, not translated renderer implementation.
// The half-normal condition T.H=constant motivates the pair-dependent offset.
// Symmetric in L/V; truncated sine-domain tails and the changing offset mean
// this is NOT the normalized spherical-convolution model above. The measured
// unit-Fresnel mass defect and declared dielectric tests are documented in the
// host HairUnitResponse-v11 report. No fitted radiance multiplier is applied.
float3 MHHairFiberEvaluateProjectedSineR(MHHairFiberAngles a, float roughness,
                                       float sineOffset, float eta, float reflectionScale)
{
    float cosHalfAzimuth = max(0.0, cos(0.5 * a.azimuth));
    float cosLightView = a.sinLight * a.sinView + a.cosLight * a.cosView * cos(a.azimuth);
    float halfNormalLength = sqrt(saturate(0.5 * (1.0 + cosLightView)));
    float width = max(roughness * roughness * 1.4142135623730951 * cosHalfAzimuth, 0.01);
    float coordinate = (a.sinLight + a.sinView - sineOffset * halfNormalLength) / width;
    float longitudinal = exp(-0.5 * coordinate * coordinate) / (2.5066282746310005 * width);
    float dielectric = (eta - 1.0) / max(eta + 1.0, 1e-4);
    float f0 = dielectric * dielectric;
    float cosineIncident = saturate(a.cosDifference * cosHalfAzimuth);
    float schlick = f0 + (1.0 - f0) * pow(1.0 - cosineIncident, 5.0);
    return (longitudinal * 0.25 * cosHalfAzimuth * schlick * saturate(reflectionScale)).xxx;
}

// Conditional reflected-view approximation. A tilted radial facet reflects -V
// to an ideal incident direction; a Gaussian measures incident sine departure.
// This independent reflection-law construction matches the public unit-response
// measurements more closely than the symmetric half-normal approximation. Its
// conditional measure is not reciprocal and has a documented unit-Fresnel mass
// defect. It is an explicit matching option, not a normalized physical BSDF.
float3 MHHairFiberEvaluateFacetSineR(MHHairFiberAngles a, float roughness,
                                   float sineOffset, float eta, float reflectionScale)
{
    float cosHalfAzimuth = max(0.0, cos(0.5 * a.azimuth));
    // Stable half-angle identities recover the facet from its signed sin(2a)
    // profile, avoiding an inverse-trig round trip in narrow tail evaluations.
    float offset = clamp(sineOffset, -0.99, 0.99);
    float cosDoubleTilt = sqrt(max(0.0, 1.0 - offset * offset));
    float cosTilt = sqrt(0.5 * (1.0 + cosDoubleTilt));
    float sinTilt = -offset / (2.0 * cosTilt);
    float3 facet = float3(cosHalfAzimuth * cosTilt, sin(0.5 * a.azimuth) * cosTilt, -sinTilt);
    float3 view = float3(a.cosView, 0, a.sinView);
    float idealIncidentSine = reflect(-view, facet).z;
    float width = max(roughness * roughness * 1.4142135623730951 * cosHalfAzimuth, 0.01);
    float coordinate = (a.sinLight - idealIncidentSine) / width;
    float longitudinal = exp(-0.5 * coordinate * coordinate) / (2.5066282746310005 * width);
    float cosLightView = a.sinLight * a.sinView + a.cosLight * a.cosView * cos(a.azimuth);
    float cosineIncident = sqrt(saturate(0.5 * (1.0 + cosLightView)));
    float dielectric = (eta - 1.0) / max(eta + 1.0, 1e-4);
    float f0 = dielectric * dielectric;
    float schlick = f0 + (1.0 - f0) * pow(1.0 - cosineIncident, 5.0);
    return (longitudinal * 0.25 * cosHalfAzimuth * schlick * saturate(reflectionScale)).xxx;
}

// Published RGB pigment cross-sections, expressed in the declared model's
// diameter-normalized absorption convention. Concentrations must come from a
// separately verified artist-data adapter; hairMelanin is not itself cE or cP.
float3 MHHairFiberAbsorption(float eumelaninConcentration, float pheomelaninConcentration,
                           float3 additionalDyeAbsorption)
{
    return max(eumelaninConcentration, 0.0) * float3(0.419, 0.697, 1.37)
         + max(pheomelaninConcentration, 0.0) * float3(0.187, 0.4, 1.05)
         + max(additionalDyeAbsorption, 0.0);
}

float3 MHHairFiberTransmission(float3 absorption, float diameterNormalizedPathLength)
{
    return exp(-max(absorption, 0.0) * max(diameterNormalizedPathLength, 0.0));
}

void MHHairFiberPathEnergies(float fresnel, float3 transmission,
                            out float3 reflection, out float3 through,
                            out float3 internalReflection, out float3 higherOrders)
{
    float f = saturate(fresnel);
    float3 t = saturate(transmission);
    reflection = f.xxx;
    through = (1.0 - f) * (1.0 - f) * t;
    internalReflection = through * f * t;
    higherOrders = internalReflection * f * t / max(1.0 - f * t, 1e-7);
}

// TT/TRT azimuth inputs are RGB path-weighted angular densities generated by an
// independent material-table quadrature. They already contain Fresnel/absorption;
// do not multiply those factors again. R-only can use the analytic function above.
float3 MHHairFiberEvaluateThreePaths(MHHairFiberAngles a, float3 variances,
                                    float3 directionSumShifts,
                                    float3 azimuthR, float3 azimuthTT, float3 azimuthTRT)
{
    float mr = MHHairFiberShiftedLongitudinal(a, variances.x, directionSumShifts.x);
    float mtt = MHHairFiberShiftedLongitudinal(a, variances.y, directionSumShifts.y);
    float mtrt = MHHairFiberShiftedLongitudinal(a, variances.z, directionSumShifts.z);
    return mr * max(azimuthR, 0.0) + mtt * max(azimuthTT, 0.0) + mtrt * max(azimuthTRT, 0.0);
}

#endif
