using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MetaHuman.Rendering;

namespace MetaHuman.Groom
{
    /// <summary>Invalidates the camera's URP TAA history on cuts and explicit groom geometry changes.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class GroomTemporalHistory : MonoBehaviour
    {
        [Min(.001f)] public float cutDistance = .25f;
        [Range(1, 180)] public float cutAngle = 30;
        [Tooltip("Optional installed feature. Changing its temporal/visibility/debug mode invalidates history before camera setup.")]
        public GroomTransparencyFeature groomFeature;
        public int ResetCount { get; private set; }
        Camera target;
        UniversalAdditionalCameraData additional;
        Vector3 position;
        Quaternion rotation;
        Matrix4x4 projection;
        int width, height, lastFrame = -1;
        int featureMode;

        void OnEnable()
        {
            target = GetComponent<Camera>();
            additional = target.GetUniversalAdditionalCameraData();
            lastFrame = -1;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        /// <summary>Call after teleporting a groom or changing its topology, material profile or LOD.</summary>
        public void InvalidateHistory()
        {
            if (!target) target = GetComponent<Camera>();
            if (!additional && target) additional = target.GetUniversalAdditionalCameraData();
            if (additional && additional.antialiasing == AntialiasingMode.TemporalAntiAliasing)
            {
                additional.resetHistory = true;
                ResetCount++;
            }
        }

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != target || lastFrame == Time.frameCount) return;
            // Camera.projectionMatrix is the authored, non-jittered matrix at
            // beginCameraRendering. The pipeline installs its jitter afterwards.
            var currentProjection = camera.projectionMatrix;
            int currentMode = groomFeature ? (groomFeature.settings.temporalReconstruction ? 1 : 0) |
                ((int)groomFeature.settings.visibilityMode << 1) | (groomFeature.settings.debugCoverage ? 4 : 0) : -1;
            bool changed = lastFrame < 0 || Time.frameCount - lastFrame > 1 ||
                Vector3.Distance(position, camera.transform.position) > cutDistance ||
                Quaternion.Angle(rotation, camera.transform.rotation) > cutAngle ||
                width != camera.pixelWidth || height != camera.pixelHeight || featureMode != currentMode;
            for (int i = 0; i < 16 && !changed; i++)
                changed = Mathf.Abs(currentProjection[i] - projection[i]) > 1e-5f;
            if (changed) InvalidateHistory();
            position = camera.transform.position; rotation = camera.transform.rotation;
            projection = currentProjection; width = camera.pixelWidth; height = camera.pixelHeight;
            featureMode = currentMode;
            lastFrame = Time.frameCount;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            InvalidateHistory(); lastFrame = -1;
        }
    }
}
