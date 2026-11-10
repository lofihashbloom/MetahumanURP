using UnityEngine;

namespace MetaHuman.Groom
{
    /// <summary>
    /// Static light-space optical-depth field of projected hair fiber area for one light.
    /// The texture stores N+1 boundary-prefix depth planes (N x N x N+1, RFloat); the last plane is the
    /// full terminal optical depth. Frames are in the owning <see cref="HairReceiverShadowVolume"/>'s space.
    /// Built offline from source groom curves (Tools~/Hair/build_receiver_shadow_fields.py); it does not
    /// follow animated hair or head motion.
    /// </summary>
    [CreateAssetMenu(menuName = "MetaHuman/Hair Receiver Shadow Field", fileName = "HairReceiverShadowField")]
    public sealed class HairReceiverShadowField : ScriptableObject
    {
        public Texture3D opticalDepth;
        [Tooltip("Light position (xyz) and minimum field depth (w), volume space, metres.")]
        public Vector4 originZMin;
        [Tooltip("Light-space right axis (xyz) and minimum projected u (w).")]
        public Vector4 rightUMin;
        [Tooltip("Light-space up axis (xyz) and minimum projected v (w).")]
        public Vector4 upVMin;
        [Tooltip("Light-space forward axis (xyz) and maximum field depth (w).")]
        public Vector4 forwardZMax;
        [Tooltip("1/(u range), 1/(v range), 1/(depth range).")]
        public Vector3 scales;
        public string sourceLight;
        public string sourceGroomSha256;

        public int Resolution => opticalDepth ? opticalDepth.width : 0;
        public Vector3 LightPosition => originZMin;

        /// <summary>Returns null when usable, otherwise the reason the field must not be bound.</summary>
        public string Validate()
        {
            var t = opticalDepth;
            if (!t) return "Missing optical-depth texture";
            if (t.format != TextureFormat.RFloat || t.mipmapCount != 1) return "Optical depth must be RFloat without mips";
            if (t.width < 2 || t.height != t.width || t.depth != t.width + 1) return "Optical depth must be N x N x (N+1) boundary-prefix planes";
            if (t.filterMode != FilterMode.Bilinear || t.wrapMode != TextureWrapMode.Clamp) return "Optical depth must use bilinear clamp sampling";
            if (!(scales.x > 0 && scales.y > 0 && scales.z > 0) || !(forwardZMax.w > originZMin.w)) return "Invalid field extent";
            if (Mathf.Abs(((Vector3)forwardZMax).sqrMagnitude - 1) > 1e-3f || Mathf.Abs(((Vector3)rightUMin).sqrMagnitude - 1) > 1e-3f
                || Mathf.Abs(((Vector3)upVMin).sqrMagnitude - 1) > 1e-3f) return "Field axes must be unit length";
            return null;
        }
    }
}
