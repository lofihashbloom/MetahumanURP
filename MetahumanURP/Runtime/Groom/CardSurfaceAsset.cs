using System;
using UnityEngine;

namespace MetaHuman.Groom
{
    [CreateAssetMenu(menuName = "MetaHuman/Card Surface Binding")]
    public sealed class CardSurfaceAsset : ScriptableObject
    {
        [Serializable] public struct Attachment
        {
            public int a, b, c;
            public Vector3 barycentric;
            public float normalSign;
            public Vector3 offset, normal, tangent;
            public float tangentSign;
        }
        public Mesh faceMesh;
        public Mesh fittedMesh;
        public Attachment[] attachments = Array.Empty<Attachment>();
        public string sourceCardName;
        public float maximumProjectionDistance;
        public float meanProjectionDistance;
        public float clearance;
    }

    public static class CardSurfaceMath
    {
        public static Quaternion Frame(Vector3 a, Vector3 b, Vector3 c, float normalSign)
        {
            var tangent = b - a;
            var normal = Vector3.Cross(tangent, c - a) * normalSign;
            if (tangent.sqrMagnitude < 1e-16f || normal.sqrMagnitude < 1e-20f)
                throw new InvalidOperationException("A card attachment triangle collapsed.");
            // Unity's Normalize discards vectors below 1e-5; valid facial
            // triangles can have cross products well below that threshold.
            normal *= 1f / Mathf.Sqrt(normal.sqrMagnitude);
            tangent *= 1f / Mathf.Sqrt(tangent.sqrMagnitude);
            return Quaternion.LookRotation(normal, Vector3.Cross(normal, tangent));
        }

        public static Vector3 Position(CardSurfaceAsset.Attachment attachment, Vector3 a, Vector3 b, Vector3 c)
        {
            return a * attachment.barycentric.x + b * attachment.barycentric.y + c * attachment.barycentric.z
                + Frame(a, b, c, attachment.normalSign) * attachment.offset;
        }
    }
}
