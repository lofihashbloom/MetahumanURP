using UnityEngine;

namespace MetaHuman.Dna
{
    [CreateAssetMenu(menuName = "MetaHuman/DNA Rig Definition")]
    public sealed class DnaRigDefinition : ScriptableObject
    {
        public TextAsset dnaBytes;
        [Tooltip("Verified for legacy UDNAAsset DNA and the Unreal FBX exporter: position (x,z,y), quaternion (-x,-z,-y,w).")]
        public DnaCoordinateConvention coordinateConvention = DnaCoordinateConvention.LegacyUnrealFbx;
        public string sourceSha256;
    }

    public enum DnaCoordinateConvention { LegacyUnrealFbx }

    public static class DnaCoordinates
    {
        public static Vector3 Translation(float x, float y, float z) => new Vector3(x, z, y) * 0.01f;
        public static Vector3 Scale(float x, float y, float z) => new Vector3(x, z, y);
        public static Quaternion Rotation(float x, float y, float z, float w) => new Quaternion(-x, -z, -y, w).normalized;
    }
}
