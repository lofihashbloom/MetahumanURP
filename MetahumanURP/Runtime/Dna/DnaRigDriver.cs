using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Dna
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class DnaRigDriver : MonoBehaviour
    {
        public DnaRigDefinition definition;
        public SkinnedMeshRenderer face;
        public bool previewInEditor;
        [Min(0)] public int lod;
        [Tooltip("Apply the rig's corrective blend-shape outputs to the face mesh's matching blend shapes.")]
        public bool applyBlendShapes = true;
        [Tooltip("Layer the rig's deltas for body joints the DNA also lists (neck, head, spine, clavicles, arms) on their current animated pose instead of the DNA neutral, so body animation keeps driving them. FACIAL_ joints always use neutral + delta.")]
        public bool layerBodyJoints = true;
        NativeRig rig;
        int[] shapeChannels;
        float[] restShapeWeights;
        Transform[] joints;
        Vector3[] bindPositions, bindScales;
        Quaternion[] bindRotations;
        // Last local pose this driver wrote to each layered body joint; a different pose means something else animated it.
        Vector3[] writtenPositions, writtenScales;
        Quaternion[] writtenRotations;
        Vector3[] basePositions, baseScales;
        Quaternion[] baseRotations;
        bool[] written;
        readonly Dictionary<string, int> controls = new Dictionary<string, int>(StringComparer.Ordinal);
        float[] inputs;
        // Set when automatic setup/evaluation throws; LateUpdate stays idle until re-enable, validation or Initialize.
        bool automaticFailed;
        public NativeRig Rig => rig;
        public int BoundJointCount { get; private set; }
        public float MaximumBindPositionError { get; private set; }
        public float MaximumBindRotationError { get; private set; }
        public int EvaluationCount { get; private set; }
        public int BoundBlendShapeCount { get; private set; }
        /// <summary>Face mesh blend shapes that no rig channel drives (left at their rest weight).</summary>
        public IReadOnlyList<string> UnmatchedBlendShapes { get; private set; } = Array.Empty<string>();

        void OnEnable()
        {
            automaticFailed = false;
            if (definition && definition.dnaBytes && face)
            {
                try { Initialize(); }
                catch (Exception exception) { automaticFailed = true; Debug.LogError("MetaHuman DNA initialization failed: " + exception.Message, this); }
            }
        }

        void OnValidate() => automaticFailed = false;

        public void Initialize()
        {
            Shutdown();
            if (!definition || !definition.dnaBytes || !face || !face.rootBone)
                throw new InvalidOperationException("Assign a DNA definition and the imported face renderer.");
            rig = new NativeRig(definition.dnaBytes.bytes);
            try
            {
                var hierarchy = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var bone in face.rootBone.GetComponentsInChildren<Transform>(true))
                    if (!hierarchy.TryAdd(bone.name, bone)) throw new InvalidOperationException("Duplicate joint name: " + bone.name);
                joints = new Transform[rig.JointNames.Length];
                bindPositions = new Vector3[joints.Length];
                bindRotations = new Quaternion[joints.Length];
                bindScales = new Vector3[joints.Length];
                writtenPositions = new Vector3[joints.Length];
                writtenRotations = new Quaternion[joints.Length];
                writtenScales = new Vector3[joints.Length];
                written = new bool[joints.Length];
                basePositions = new Vector3[joints.Length];
                baseRotations = new Quaternion[joints.Length];
                baseScales = new Vector3[joints.Length];
                inputs = new float[rig.RawControlNames.Length];
                for (int i = 0; i < inputs.Length; i++) controls.Add(rig.RawControlNames[i], i);
                var neutral = rig.NeutralJoints;
                BoundJointCount = 0;
                MaximumBindPositionError = MaximumBindRotationError = 0;
                for (int i = 0; i < joints.Length; i++)
                {
                    int parent = rig.JointParents[i];
                    // A DNA root may be global while the FBX embeds it under a body spine.
                    // Keep that attachment/root transform owned by the body animation.
                    if (parent == i) continue;
                    if (!hierarchy.TryGetValue(rig.JointNames[i], out var joint))
                        throw new InvalidOperationException("DNA joint missing from the imported face: " + rig.JointNames[i]);
                    if (parent < 0 || parent >= joints.Length || !joint.parent || joint.parent.name != rig.JointNames[parent])
                        throw new InvalidOperationException("DNA/FBX hierarchy mismatch: " + joint.name);
                    int offset = i * 10;
                    float pError = Vector3.Distance(joint.localPosition, DnaCoordinates.Translation(neutral[offset], neutral[offset + 1], neutral[offset + 2]));
                    float rError = Quaternion.Angle(joint.localRotation, DnaCoordinates.Rotation(neutral[offset + 3], neutral[offset + 4], neutral[offset + 5], neutral[offset + 6]));
                    MaximumBindPositionError = Mathf.Max(MaximumBindPositionError, pError);
                    MaximumBindRotationError = Mathf.Max(MaximumBindRotationError, rError);
                    if (pError > 0.0001f || rError > 0.1f)
                        throw new InvalidOperationException($"DNA coordinate/bind mismatch at {joint.name}: {pError:G3} m, {rError:G3} degrees. Import the matching DNA and FBX.");
                    joints[i] = joint;
                    bindPositions[i] = joint.localPosition;
                    bindRotations[i] = joint.localRotation;
                    bindScales[i] = joint.localScale;
                    BoundJointCount++;
                }
                var mesh = face.sharedMesh;
                var meshShapes = new string[mesh ? mesh.blendShapeCount : 0];
                for (int i = 0; i < meshShapes.Length; i++) meshShapes[i] = mesh.GetBlendShapeName(i);
                shapeChannels = MapBlendShapes(meshShapes, rig.BlendShapeNames);
                restShapeWeights = new float[meshShapes.Length];
                var unmatched = new List<string>();
                BoundBlendShapeCount = 0;
                for (int i = 0; i < meshShapes.Length; i++)
                {
                    restShapeWeights[i] = face.GetBlendShapeWeight(i);
                    if (shapeChannels[i] >= 0) BoundBlendShapeCount++; else unmatched.Add(meshShapes[i]);
                }
                UnmatchedBlendShapes = unmatched;
                rig.Evaluate(inputs, Mathf.Clamp(lod, 0, rig.LodCount - 1));
                automaticFailed = false;
            }
            catch { Shutdown(); throw; }
        }

        public void SetRawControl(string name, float value)
        {
            if (rig == null) Initialize();
            if (!controls.TryGetValue(name, out int index)) throw new ArgumentException("Unknown DNA raw control: " + name);
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            inputs[index] = value;
        }

        public void ClearControls() { if (inputs != null) Array.Clear(inputs, 0, inputs.Length); }

        public void EvaluateNow()
        {
            if (rig == null) Initialize();
            rig.Evaluate(inputs, Mathf.Clamp(lod, 0, rig.LodCount - 1));
            EvaluationCount++;
            float[] neutral = rig.NeutralJoints, delta = rig.JointDeltas;
            // Which joints belong to the body: a string test per joint, done once per rig instead of every frame.
            if (bodyJoint == null || bodyJoint.Length != joints.Length)
            {
                bodyJoint = new bool[joints.Length];
                for (int i = 0; i < joints.Length; i++) bodyJoint[i] = IsBodyJoint(rig.JointNames[i]);
            }
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (!joint) continue;
                int o = i * 10;
                var n = DnaCoordinates.Rotation(neutral[o + 3], neutral[o + 4], neutral[o + 5], neutral[o + 6]);
                var d = DnaCoordinates.Rotation(delta[o + 3], delta[o + 4], delta[o + 5], delta[o + 6]);
                if (layerBodyJoints && bodyJoint[i])
                {
                    // Base = the joint's animated local pose. When nothing else wrote the joint since our last
                    // write, keep the previous base so deltas never accumulate.
                    if (!written[i] || !SamePose(joint, writtenPositions[i], writtenRotations[i], writtenScales[i]))
                    {
                        basePositions[i] = joint.localPosition; baseRotations[i] = joint.localRotation; baseScales[i] = joint.localScale;
                    }
                    // DNA deltas are relative to the neutral joint: translation and scale add, rotation composes as base * d.
                    joint.localPosition = basePositions[i] + DnaCoordinates.Translation(delta[o], delta[o + 1], delta[o + 2]);
                    joint.localRotation = baseRotations[i] * d;
                    joint.localScale = baseScales[i] + DnaCoordinates.Scale(neutral[o + 7] + delta[o + 7], neutral[o + 8] + delta[o + 8], neutral[o + 9] + delta[o + 9]) - DnaCoordinates.Scale(neutral[o + 7], neutral[o + 8], neutral[o + 9]);
                    writtenPositions[i] = joint.localPosition; writtenRotations[i] = joint.localRotation; writtenScales[i] = joint.localScale; written[i] = true;
                    continue;
                }
                joint.localPosition = DnaCoordinates.Translation(neutral[o] + delta[o], neutral[o + 1] + delta[o + 1], neutral[o + 2] + delta[o + 2]);
                joint.localRotation = n * d;
                joint.localScale = DnaCoordinates.Scale(neutral[o + 7] + delta[o + 7], neutral[o + 8] + delta[o + 8], neutral[o + 9] + delta[o + 9]);
            }
            if (applyBlendShapes && shapeChannels != null)
            {
                // Rig outputs are 0-1; Unity blend-shape weights are 0-100 (single full-weight frame).
                float[] weights = rig.BlendShapes;
                for (int i = 0; i < shapeChannels.Length; i++)
                    if (shapeChannels[i] >= 0) face.SetBlendShapeWeight(i, weights[shapeChannels[i]] * 100f);
            }
        }

        /// <summary>DNA joints that belong to the body skeleton (everything except FACIAL_ joints).</summary>
        bool[] bodyJoint;

        public static bool IsBodyJoint(string name) => !name.StartsWith("FACIAL_", StringComparison.Ordinal);

        // Exact: the written pose is read back after the write, so an untouched joint returns it unchanged. A tolerance
        // takes slow animation (under 0.05 degrees a frame) for no animation and holds the joint still.
        static bool SamePose(Transform t, Vector3 position, Quaternion rotation, Vector3 scale) =>
            t.localPosition.Equals(position) && t.localRotation.Equals(rotation) && t.localScale.Equals(scale);

        /// <summary>
        /// Rig channel for each mesh blend shape, or -1. Imported MetaHuman shapes are named
        /// "&lt;mesh&gt;_lodN_mesh__&lt;channel&gt;" (e.g. head_lod0_mesh__brow_down_L) while the DNA names the channel only;
        /// one channel can drive shapes on several meshes. Names without the mesh prefix match as-is.
        /// </summary>
        public static int[] MapBlendShapes(IReadOnlyList<string> meshShapes, IReadOnlyList<string> rigChannels)
        {
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rigChannels.Count; i++) byName[rigChannels[i]] = i;
            var result = new int[meshShapes.Count];
            for (int i = 0; i < meshShapes.Count; i++)
            {
                string name = meshShapes[i] ?? "";
                int split = name.IndexOf("_mesh__", StringComparison.Ordinal);
                if (split >= 0) name = name.Substring(split + "_mesh__".Length);
                result[i] = byName.TryGetValue(name, out int channel) ? channel : -1;
            }
            return result;
        }

        void LateUpdate()
        {
            if ((!Application.IsPlaying(gameObject) && !previewInEditor) || !definition || !face || automaticFailed) return;
            try { EvaluateNow(); }
            catch (Exception exception)
            {
                automaticFailed = true;
                Debug.LogError("MetaHuman DNA evaluation failed; automatic updates paused until the driver is re-enabled: " + exception.Message, this);
            }
        }

        public void Shutdown()
        {
            if (joints != null)
                for (int i = 0; i < joints.Length; i++)
                    if (joints[i]) joints[i].SetLocalPositionAndRotation(bindPositions[i], bindRotations[i]);
            if (joints != null)
                for (int i = 0; i < joints.Length; i++) if (joints[i]) joints[i].localScale = bindScales[i];
            if (shapeChannels != null && face)
                for (int i = 0; i < shapeChannels.Length && i < restShapeWeights.Length; i++)
                    if (shapeChannels[i] >= 0) face.SetBlendShapeWeight(i, restShapeWeights[i]);
            shapeChannels = null;
            rig?.Dispose(); rig = null;
            joints = null;
            controls.Clear();
            inputs = null;
        }
        void OnDisable() => Shutdown();
        void OnDestroy() => Shutdown();
    }
}
