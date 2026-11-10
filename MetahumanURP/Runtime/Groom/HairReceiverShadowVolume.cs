using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Groom
{
    /// <summary>
    /// Binds up to four static hair optical-depth fields to the exact Lights they were built for.
    /// Skin materials with MH_HAIR_RECEIVER_SHADOW then attenuate those lights' direct contribution.
    /// Requires HairReceiverShadowFeature on the URP renderer and per-pixel additional lights.
    /// Place on the transform the fields were built in (usually the character root, unit scale).
    /// A slot is skipped when its light moved from the field's light position; rebuild fields after
    /// light or groom changes. The field is rest pose: it does not follow head or hair animation.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class HairReceiverShadowVolume : MonoBehaviour
    {
        public const int SlotCount = 4;
        public Light[] lights = new Light[SlotCount];
        public HairReceiverShadowField[] fields = new HairReceiverShadowField[SlotCount];
        [Tooltip("Optical-depth multiplier per unit projected fiber area. 0.7 matches the validated Ada setup.")]
        [Min(0)] public float density = .7f;
        [Tooltip("Maximum distance (metres) between a light and the position its field was built for.")]
        [Min(0)] public float lightPositionTolerance = .01f;

        static readonly List<HairReceiverShadowVolume> enabledVolumes = new List<HairReceiverShadowVolume>();

        /// <summary>The most recently enabled volume. Only one volume is bound per frame.</summary>
        public static HairReceiverShadowVolume Active => enabledVolumes.Count > 0 ? enabledVolumes[enabledVolumes.Count - 1] : null;

        public struct Slot
        {
            public Light light;
            public HairReceiverShadowField field;
            public Vector4 originZMin, rightUMin, upVMin, forwardZMax, scales;
            public string error;
            public bool Valid => error == null;
        }

        void OnEnable() { enabledVolumes.Remove(this); enabledVolumes.Add(this); }

        void OnDisable()
        {
            enabledVolumes.Remove(this);
            if (enabledVolumes.Count == 0) Shader.SetGlobalFloat(HairReceiverShadowIds.Enabled, 0);
        }

        void OnValidate()
        {
            if (lights == null || lights.Length != SlotCount) System.Array.Resize(ref lights, SlotCount);
            if (fields == null || fields.Length != SlotCount) System.Array.Resize(ref fields, SlotCount);
        }

        /// <summary>Resolves world-space frames for every slot; invalid slots carry the reason.</summary>
        public Slot[] ResolveSlots()
        {
            var result = new Slot[SlotCount];
            var scale = transform.lossyScale;
            bool unitScale = Mathf.Abs(scale.x - 1) < 1e-3f && Mathf.Abs(scale.y - 1) < 1e-3f && Mathf.Abs(scale.z - 1) < 1e-3f;
            var seen = new HashSet<Light>();
            for (int i = 0; i < SlotCount; i++)
            {
                var light = lights != null && i < lights.Length ? lights[i] : null;
                var field = fields != null && i < fields.Length ? fields[i] : null;
                ref var slot = ref result[i];
                slot.light = light; slot.field = field;
                if (!light && !field) { slot.error = "Empty"; continue; }
                if (!light || !field) { slot.error = "Light and field must both be assigned"; continue; }
                if (!seen.Add(light)) { slot.error = "Light is bound to more than one slot"; continue; }
                slot.error = field.Validate();
                if (slot.error != null) continue;
                if (!unitScale) { slot.error = "Volume transform must have unit scale"; continue; }
                slot = Transform(slot, transform.localToWorldMatrix);
                if (Vector3.Distance(light.transform.position, slot.originZMin) > lightPositionTolerance)
                    slot.error = "Light moved from the field's light position; rebuild the field";
            }
            return result;
        }

        /// <summary>Rigidly transforms a field's volume-space frame (depth ranges are unchanged).</summary>
        public static Slot Transform(Slot slot, Matrix4x4 localToWorld)
        {
            var f = slot.field;
            Vector3 Dir(Vector4 v) => localToWorld.MultiplyVector(v).normalized;
            Vector3 origin = localToWorld.MultiplyPoint3x4(f.originZMin);
            slot.originZMin = new Vector4(origin.x, origin.y, origin.z, f.originZMin.w);
            var r = Dir(f.rightUMin); slot.rightUMin = new Vector4(r.x, r.y, r.z, f.rightUMin.w);
            var u = Dir(f.upVMin); slot.upVMin = new Vector4(u.x, u.y, u.z, f.upVMin.w);
            var d = Dir(f.forwardZMax); slot.forwardZMax = new Vector4(d.x, d.y, d.z, f.forwardZMax.w);
            slot.scales = f.scales;
            return slot;
        }
    }

    static class HairReceiverShadowIds
    {
        public static readonly int Enabled = Shader.PropertyToID("_MHReceiverEnabled");
    }
}
