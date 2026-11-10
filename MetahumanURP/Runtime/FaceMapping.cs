using System;
using UnityEngine;

namespace MetaHuman
{
    /// <summary>Explicit mapping for an already baked rig, not an approximation of DNA RigLogic.</summary>
    [CreateAssetMenu(menuName = "MetaHuman/Baked Face Mapping")]
    public sealed class FaceMapping : ScriptableObject
    {
        public BlendShapeBinding[] blendShapes = Array.Empty<BlendShapeBinding>();
        public WrinkleBinding[] wrinkles = Array.Empty<WrinkleBinding>();

        [Serializable]
        public struct BlendShapeBinding
        {
            public string control;
            public string blendShape;
            [Tooltip("Unity blend shape weight per unit input, commonly 100.")]
            public float scale;
        }

        [Serializable]
        public struct WrinkleBinding
        {
            public string control;
            [Range(0, 2)] public int map;
            public float scale;
        }
    }
}
