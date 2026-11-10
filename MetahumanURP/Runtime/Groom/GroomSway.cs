using System;
using UnityEngine;

namespace MetaHuman.Groom
{
    /// <summary>
    /// Light, cheap groom sway (secondary motion): the groom lags behind its head, overshoots a little and settles, and
    /// hangs slightly toward gravity when the head tilts. A second, looser spring drives loose strands: each strand mixes
    /// the two by its own random value (and spreads the main sway by `variation`), and strands in front of the pivot
    /// (the fringe) get `frontBoost` more, so fine front strands visibly move while the mass of the hair stays calm. The whole groom is one small rotation about a pivot near the
    /// crown, weighted root (0) to tip (1) along each strand in the strand shaders (GroomSway.hlsl), driven by a damped
    /// spring here: θ'' = ω0² (θgravity − θ) − 2ζω0 θ' − k_angular α_head − k_linear (d × a_pivot) / L, with ω0 = 2π
    /// frequency, ζ = damping, d the hair mass direction and L its lever. CPU: a few dozen operations per frame per
    /// component; GPU: a few ALU per strand point inside the existing passes (no extra pass or buffer).
    /// Not a strand simulation: no collision with the head or shoulders, no per-strand dynamics, and the precomputed
    /// self-shadow (GroomShadowVolume) and receiver-shadow volumes do not follow the sway.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GroomSway : MonoBehaviour
    {
        public static readonly int PivotId = Shader.PropertyToID("_MHGroomSwayPivot"), AxisAngleId = Shader.PropertyToID("_MHGroomSwayAxisAngle"),
            PrevPivotId = Shader.PropertyToID("_MHGroomSwayPrevPivot"), PrevAxisAngleId = Shader.PropertyToID("_MHGroomSwayPrevAxisAngle"),
            FineId = Shader.PropertyToID("_MHGroomSwayFine"), FrontId = Shader.PropertyToID("_MHGroomSwayFront"), PrevFineId = Shader.PropertyToID("_MHGroomSwayPrevFine");

        [Tooltip("The head the groom follows rigidly (the strands' parent).")] public Transform head;
        [Tooltip("Strand renderers that sway: head hair. Brows, lashes and skin-bound facial hair should not.")] public Renderer[] renderers = Array.Empty<Renderer>();
        // Offsets are in world axes at the head's rest pose (the pose at the first step after ResetState, head upright),
        // relative to the head's position, then carried rigidly by the head: rig bone axes (a MetaHuman head bone's local
        // Y is not up) never enter the model.
        [Tooltip("Pivot from the head (m, world axes at rest): near the crown, where the groom swings from.")] public Vector3 pivot = new Vector3(0, .08f, 0);
        [Tooltip("Hair mass from the pivot (m, world axes at rest): lever for linear acceleration.")] public Vector3 massOffset = new Vector3(0, -.12f, 0);
        [Tooltip("Natural frequency (Hz).")] [Range(.2f, 6)] public float frequency = 1.6f;
        [Tooltip("Damping ratio (1 = critical).")] [Range(0, 1.5f)] public float damping = .35f;
        [Tooltip("Share of the head's angular acceleration the hair resists.")] [Range(0, 2)] public float angularInertia = .8f;
        [Tooltip("Share of the pivot's linear acceleration the hair resists.")] [Range(0, 2)] public float linearInertia = 1;
        [Tooltip("Share of a head tilt (from the pose at reset) the hair hangs back toward gravity.")] [Range(0, 1)] public float gravity = .25f;
        [Tooltip("Largest sway at the tips (degrees).")] [Range(0, 30)] public float maxAngle = 8;
        [Tooltip("Root-to-tip weight exponent: higher keeps more of the strand rigid.")] [Range(.25f, 4)] public float tipExponent = 1.5f;
        [Tooltip("Overall sway scale (0 = off).")] [Range(0, 2)] public float amount = 1;
        [Header("Loose strands")]
        [Tooltip("Second, looser spring (Hz): each strand follows it by its own random value, so fine and loose strands move more and not as one block.")] [Range(.2f, 6)] public float fineFrequency = .85f;
        [Tooltip("Damping ratio of the loose spring.")] [Range(0, 1.5f)] public float fineDamping = .2f;
        [Tooltip("Share of a head tilt the loose strands hang back toward gravity.")] [Range(0, 1)] public float fineGravity = .6f;
        [Tooltip("Per-strand spread of the main sway (0 = every strand alike, 1 = 0 to 2x).")] [Range(0, 1)] public float variation = .6f;
        [Tooltip("Extra sway for strands in front of the pivot (the fringe over the face): x(1 + frontBoost x facing).")] [Range(0, 6)] public float frontBoost = 2;
        [Tooltip("Face direction (world axes at rest): front strands lie this way from the pivot.")] public Vector3 forward = new Vector3(0, 0, 1);
        [Tooltip("Integrate in LateUpdate from Time.deltaTime in play mode. Offline renders call Step and Apply per frame.")] public bool autoStep = true;

        /// <summary>The current sway at the tips: a world rotation vector (radians), before the amount scale.</summary>
        public Vector3 Angle => angle;
        /// <summary>The loose spring's sway (world rotation vector, radians), before the amount scale.</summary>
        public Vector3 FineAngle => fineAngle;
        Vector3 angle, velocity, fineAngle, fineVelocity, lastAngularVelocity, lastPivotVelocity, lastPivot, restDownLocal, pivotLocal, massLocal, forwardLocal;
        Quaternion lastRotation;
        int primed;
        Vector4 appliedPivot, appliedAxisAngle, appliedFine;
        bool hasApplied;
        MaterialPropertyBlock block;

        /// <summary>Forget the motion history: the next steps measure the head again from rest (no kick).</summary>
        public void ResetState()
        {
            angle = velocity = fineAngle = fineVelocity = lastAngularVelocity = lastPivotVelocity = Vector3.zero;
            primed = 0; hasApplied = false;
        }

        void OnEnable() => ResetState();
        void OnDisable() => Clear();

        void LateUpdate()
        {
            if (!autoStep || !Application.isPlaying) return;
            Step(Time.deltaTime);
            Apply();
        }

        /// <summary>Advance the spring by dt seconds from the head's current pose (substeps of at most 1/240 s).</summary>
        public void Step(float dt)
        {
            if (!head || !(dt > 0)) return;
            var rotation = head.rotation;
            // Two priming steps: the first records the rest pose, the second the velocities (no acceleration spike at start).
            if (primed == 0)
            {
                var inverse = Quaternion.Inverse(rotation);
                pivotLocal = inverse * pivot; massLocal = inverse * massOffset; restDownLocal = inverse * Vector3.down;
                forwardLocal = inverse * (forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward);
                lastRotation = rotation; lastPivot = head.position + rotation * pivotLocal; primed = 1; return;
            }
            var pivotWS = head.position + rotation * pivotLocal;
            var omega = AngularVelocity(lastRotation, rotation, dt);
            var pivotVelocity = (pivotWS - lastPivot) / dt;
            lastRotation = rotation; lastPivot = pivotWS;
            if (primed == 1) { lastAngularVelocity = omega; lastPivotVelocity = pivotVelocity; primed = 2; return; }
            var alpha = (omega - lastAngularVelocity) / dt;
            var accel = (pivotVelocity - lastPivotVelocity) / dt;
            lastAngularVelocity = omega; lastPivotVelocity = pivotVelocity;

            // Gravity: in head space, world down moved from its rest direction by the tilt; the hair turns `gravity` of the
            // way after it (a yaw about world up leaves it in place), expressed back in world space.
            var downLocal = Quaternion.Inverse(rotation) * Vector3.down;
            var tilt = rotation * RotationVector(Quaternion.FromToRotation(restDownLocal, downLocal));
            var target = gravity * tilt; var fineTarget = fineGravity * tilt;
            var mass = rotation * massLocal;
            float lever = Mathf.Max(mass.magnitude, .01f);
            var drive = -angularInertia * alpha - linearInertia * Vector3.Cross(mass / lever, accel) / lever;
            float w0 = 2 * Mathf.PI * frequency, zeta = damping, max = maxAngle * Mathf.Deg2Rad, w1 = 2 * Mathf.PI * fineFrequency, zeta1 = fineDamping;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 240), 1, 64); float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                velocity += (w0 * w0 * (target - angle) - 2 * zeta * w0 * velocity + drive) * h;
                angle += velocity * h;
                fineVelocity += (w1 * w1 * (fineTarget - fineAngle) - 2 * zeta1 * w1 * fineVelocity + drive) * h;
                fineAngle += fineVelocity * h;
                Clamp(ref angle, ref velocity, max); Clamp(ref fineAngle, ref fineVelocity, max);
            }
        }

        static void Clamp(ref Vector3 a, ref Vector3 v, float max)
        {
            float m = a.magnitude;
            if (m > max) { var n = a / m; a = n * max; v -= n * Mathf.Max(0, Vector3.Dot(v, n)); }
        }

        /// <summary>Write this frame's sway (and the previous frame's, for motion vectors) to the renderers' property blocks.</summary>
        public void Apply()
        {
            var pivotWS = !head ? transform.position : primed > 0 ? head.position + head.rotation * pivotLocal : head.position + pivot;
            var current = new Vector4(pivotWS.x, pivotWS.y, pivotWS.z, tipExponent);
            var rotationVector = angle * amount;
            var axisAngle = new Vector4(rotationVector.x, rotationVector.y, rotationVector.z, amount > 0 ? 1 : 0);
            var loose = fineAngle * amount; var fine = new Vector4(loose.x, loose.y, loose.z, variation);
            var forwardWS = head && primed > 0 ? head.rotation * forwardLocal : (forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward);
            var front = new Vector4(forwardWS.x, forwardWS.y, forwardWS.z, frontBoost);
            var prevPivot = hasApplied ? appliedPivot : current; var prevAxisAngle = hasApplied ? appliedAxisAngle : axisAngle; var prevFine = hasApplied ? appliedFine : fine;
            ForEachBlock(b =>
            {
                b.SetVector(PivotId, current); b.SetVector(AxisAngleId, axisAngle); b.SetVector(FineId, fine); b.SetVector(FrontId, front);
                b.SetVector(PrevPivotId, prevPivot); b.SetVector(PrevAxisAngleId, prevAxisAngle); b.SetVector(PrevFineId, prevFine);
            });
            appliedPivot = current; appliedAxisAngle = axisAngle; appliedFine = fine; hasApplied = true;
        }

        /// <summary>Turn the sway off on the renderers (rotation and flag zero).</summary>
        public void Clear()
        {
            ForEachBlock(b => { b.SetVector(AxisAngleId, Vector4.zero); b.SetVector(PrevAxisAngleId, Vector4.zero); b.SetVector(FineId, Vector4.zero); b.SetVector(PrevFineId, Vector4.zero); });
            hasApplied = false;
        }

        void ForEachBlock(Action<MaterialPropertyBlock> set)
        {
            if (renderers == null) return;
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (!r) continue;
                // Per material index, as GroomShadowVolume writes strand blocks and GroomDeferredFeature reads them.
                for (int i = 0; i < r.sharedMaterials.Length; i++) { r.GetPropertyBlock(block, i); set(block); r.SetPropertyBlock(block, i); }
            }
        }

        /// <summary>World angular velocity (rad/s) taking rotation a to b in dt.</summary>
        public static Vector3 AngularVelocity(Quaternion a, Quaternion b, float dt) => RotationVector(b * Quaternion.Inverse(a)) / dt;

        /// <summary>Rotation vector (axis x angle in radians, shortest way) of a quaternion.</summary>
        public static Vector3 RotationVector(Quaternion q)
        {
            if (q.w < 0) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
            var v = new Vector3(q.x, q.y, q.z); float s = v.magnitude;
            if (s < 1e-8f) return 2 * v;
            return v / s * (2 * Mathf.Atan2(s, q.w));
        }
    }
}
