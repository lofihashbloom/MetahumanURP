// Ribbon corners of the deferred groom (GroomDeferredFeature, RasterMode.Compute), shared by the compute rasterizer and
// the hardware draw of long segments: the per-frame camera state and StrandVertex's compensated-rectangle placement.
#ifndef METAHUMAN_GROOM_RIBBON_INCLUDED
#define METAHUMAN_GROOM_RIBBON_INCLUDED
#include "Packages/metahuman-urp/Runtime/Shaders/GroomSegments.hlsl"
StructuredBuffer<MHGroomPoint> _Points;
StructuredBuffer<uint> _Segments;
StructuredBuffer<MHGroomMesh> _Meshes;
float4x4 _ViewMatrix, _ViewProjMatrix;
float3 _CameraPosition;
float4 _ScreenSize;        // width, height, 1/width, 1/height
float _ProjectionScale;    // |P11|
float _Orthographic;

struct Corner { float4 clip; float2 screen; float alpha; };

// StrandVertex, compensated-rectangle profile: the ribbon edge at one end of the segment (mesh: its raster parameters).
Corner MakeCorner(MHGroomPoint pt, MHGroomMesh mesh, float sideSign, float keep)
{
    float4x4 localToWorld = mesh.localToWorld;
    float minimumPixels = mesh.raster.x;
    float3 centre = mul(localToWorld, float4(pt.centre, 1)).xyz;
    float3 tangent = normalize(mul((float3x3)localToWorld, MHGroomOctDecode(MHGroomUnpackUnorm2(pt.tangentOct))));
    float3 sway = MHGroomSwayVector(centre, MHGroomUnpackHalf2(pt.diameterU).y, MHGroomUnpackHalf2(pt.randomCov).x, mesh.swayPivot, mesh.swayAxisAngle, mesh.swayFine, mesh.swayFront);
    centre = MHGroomSwayAt(centre, mesh.swayPivot.xyz, sway);
    tangent = MHGroomSwayRotate(tangent, sway);
    float3 cameraFacing = _ViewMatrix[2].xyz;   // -forward: the view matrix's third row
    float3 side = cross(tangent, cameraFacing);
    if (dot(side, side) < 1e-6) side = cross(tangent, abs(tangent.y) < .9 ? float3(0, 1, 0) : float3(1, 0, 0));
    side = normalize(side);
    float eyeDepth = max(.001, -mul(_ViewMatrix, float4(centre, 1)).z);
    float pixelWorld = 2 * (_Orthographic > .5 ? 1 : eyeDepth) / (_ProjectionScale * _ScreenSize.y);
    float2 diameterU = MHGroomUnpackHalf2(pt.diameterU);
    float2 randomCov = MHGroomUnpackHalf2(pt.randomCov);
    float diameter = diameterU.x / keep;
    float radial = distance(_CameraPosition, centre);
    float minimumAtEyeDepth = pixelWorld * minimumPixels;
    float support = 2 * (_Orthographic > .5 ? 1 : radial) / (_ProjectionScale * _ScreenSize.y) * minimumPixels;
    float rasterWidth = max(diameter, support);
    float k = mesh.info.y == 2 ? mesh.raster.w : mesh.info.y == 1 ? randomCov.y : 1;
    Corner c;
    c.alpha = diameter / max(diameter, minimumAtEyeDepth) * k;
    c.clip = mul(_ViewProjMatrix, float4(centre + side * (sideSign * rasterWidth * .5), 1));
    float2 ndc = c.clip.xy / c.clip.w;
    c.screen = float2(ndc.x * .5 + .5, .5 - ndc.y * .5) * _ScreenSize.xy;
    return c;
}

float Edge(float2 a, float2 b, float2 p) { return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x); }

#endif
