using UnityEngine;

namespace MetaHuman.Groom
{
    public enum GroomRepresentation { Unmanaged = -1, Strands = 0, Cards = 1, Culled = 2 }

    /// <summary>Screen-height selection independent of resolution, render scale and supersampling.</summary>
    public static class GroomLodMath
    {
        public static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        public static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), 2 * new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
        }

        /// <summary>Conservative uncropped vertical projection of an AABB. Near-plane intersection requires full detail.</summary>
        public static bool TryScreenHeight(Bounds world, Camera camera, out float height)
        {
            height = 0;
            if (!camera || camera.stereoEnabled || camera.cameraType != CameraType.Game) return false;
            var view = camera.worldToCameraMatrix;
            var projection = camera.projectionMatrix;
            float minDepth = float.PositiveInfinity, maxDepth = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 v = view.MultiplyPoint3x4(p);
                float depth = -v.z;
                if (!IsFinite(depth)) return false;
                minDepth = Mathf.Min(minDepth, depth); maxDepth = Mathf.Max(maxDepth, depth);
                Vector4 clip = projection * new Vector4(v.x, v.y, v.z, 1);
                if (!IsFinite(clip.y) || !IsFinite(clip.w)) return false;
                if (Mathf.Abs(clip.w) > 1e-8f) { float y = clip.y / clip.w; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            }
            if (maxDepth <= 0 || minDepth > camera.farClipPlane) return true;
            if (minDepth <= camera.nearClipPlane) { height = 1; return true; }
            height = Mathf.Clamp01((maxY - minY) * .5f);
            return IsFinite(height);
        }

        public static GroomRepresentation Select(float height, GroomRepresentation previous, float strandThreshold,
            float cullThreshold, float hysteresis, bool allowCulling, bool hasStrands, bool hasCards)
        {
            float h = Mathf.Clamp(hysteresis, 0, .49f);
            bool culled = allowCulling && height < cullThreshold * (previous == GroomRepresentation.Culled ? 1 + h : 1 - h);
            if (culled) return GroomRepresentation.Culled;
            if (!hasStrands) return hasCards ? GroomRepresentation.Cards : GroomRepresentation.Unmanaged;
            if (!hasCards) return GroomRepresentation.Strands;
            float boundary = strandThreshold;
            if (previous == GroomRepresentation.Strands) boundary *= 1 - h;
            if (previous == GroomRepresentation.Cards) boundary *= 1 + h;
            return height >= boundary ? GroomRepresentation.Strands : GroomRepresentation.Cards;
        }
    }
}
