using System;
using UnityEngine;

namespace MetaHuman.Groom
{
    [Serializable]
    public sealed class HairFiberLookupMetadata
    {
        public int formatVersion, width, height, slices, quadrature;
        public string profileName, profileStatus, absorptionUnits, pixelFormat, generatorSha256;
        public float eta, betaN, logisticScale;
        public float[] sigmaA;
        public bool verifiedNativePigment, perStrandPigmentVariationSupported, perStrandAzimuthRoughnessVariationSupported;
    }

    /// <summary>Intrinsic angular path densities, never coverage or shadow opacity.</summary>
    public sealed class HairFiberLookupTable : ScriptableObject
    {
        public const int ReflectionSlice = 0, TransmissionSlice = 1, InternalReflectionSlice = 2, HigherOrdersSlice = 3;
        public Texture2DArray angularDensity;
        public HairFiberLookupMetadata metadata;
        [TextArea(3, 12)] public string metadataJson;
        public bool Matches(float eta, float betaN, Vector3 sigmaA, float tolerance = 1e-5f)
        {
            if (metadata == null || metadata.sigmaA == null || metadata.sigmaA.Length != 3) return false;
            return Mathf.Abs(metadata.eta - eta) <= tolerance && Mathf.Abs(metadata.betaN - betaN) <= tolerance &&
                Vector3.Distance(new Vector3(metadata.sigmaA[0], metadata.sigmaA[1], metadata.sigmaA[2]), sigmaA) <= tolerance;
        }
    }
}
