using System;
using UnityEngine;

namespace MetaHuman.Dna
{
    /// <summary>Small, explicit raw-control sequence for integration testing and look development.</summary>
    [DisallowMultipleComponent]
    public sealed class DnaExpressionSequence : MonoBehaviour
    {
        [Serializable] public struct Control { public string name; public float value; }
        [Serializable] public struct Pose
        {
            public string name;
            [Min(0.1f)] public float duration;
            public Control[] controls;
        }
        public DnaRigDriver driver;
        public Pose[] poses = Array.Empty<Pose>();
        [Min(0)] public float fadeSeconds = 0.3f;
        public bool loop = true;
        public string CurrentPose { get; private set; }
        public int UpdatedFrames { get; private set; }
        public string StablePose { get; private set; }
        float elapsed;
        int poseIndex;

        void OnEnable() { elapsed = 0; poseIndex = 0; UpdatedFrames = 0; }
        void Update()
        {
            if (!driver || poses == null || poses.Length == 0) return;
            elapsed += Time.deltaTime;
            while (elapsed >= Mathf.Max(0.1f, poses[poseIndex].duration))
            {
                if (poseIndex + 1 == poses.Length && !loop) { enabled = false; return; }
                elapsed -= Mathf.Max(0.1f, poses[poseIndex].duration);
                poseIndex = (poseIndex + 1) % poses.Length;
            }
            var pose = poses[poseIndex];
            CurrentPose = pose.name;
            float duration = Mathf.Max(0.1f, pose.duration);
            float weight = fadeSeconds <= 0 ? 1 : Mathf.Clamp01(Mathf.Min(elapsed, duration - elapsed) / fadeSeconds);
            StablePose = weight >= 0.999f ? pose.name : "Transition";
            driver.ClearControls();
            if (pose.controls != null)
                foreach (var control in pose.controls) driver.SetRawControl(control.name, control.value * weight);
            UpdatedFrames++;
        }
        void OnDisable() { if (driver) driver.ClearControls(); }
    }
}
