using System;
using UnityEngine;

namespace MetaHuman
{
    /// <summary>Imported character receipt. Does not imply DNA rig evaluation is available.</summary>
    [DisallowMultipleComponent]
    public sealed class MetaHumanCharacter : MonoBehaviour
    {
        public string sourceCharacter;
        public string sourceEngine;
        public string sourceManifest;
        [TextArea] public string importNotes;
        public SkinnedMeshRenderer face;
        public Renderer[] surfaces = Array.Empty<Renderer>();
    }
}
