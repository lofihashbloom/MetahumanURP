using System;
using System.Linq;
using MetaHuman.Dna;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class DnaTests
    {
        const string DnaPath = "Assets/MetaHumanSource/Kellan/DNA/Kellan.behavior.dna.bytes";
        NativeRig OpenKellan()
        {
            if (Application.platform != RuntimePlatform.OSXEditor || !SystemInfo.processorType.Contains("Apple"))
                Assert.Ignore("The bundled native backend targets macOS arm64; this platform is not validated.");
            var dna = AssetDatabase.LoadAssetAtPath<TextAsset>(DnaPath);
            if (!dna) Assert.Ignore("Local Kellan DNA fixture is not part of the distributable package.");
            return new NativeRig(dna.bytes);
        }

        [Test]
        public void CoordinateReflectionPreservesQuaternionComposition()
        {
            var a = Quaternion.AngleAxis(32, new Vector3(1, 2, 3).normalized);
            var b = Quaternion.AngleAxis(-17, Vector3.up);
            Quaternion Convert(Quaternion q) => DnaCoordinates.Rotation(q.x, q.y, q.z, q.w);
            Assert.That(Quaternion.Angle(Convert(a * b), Convert(a) * Convert(b)), Is.LessThan(0.05f));
            Assert.That(DnaCoordinates.Translation(100, 200, 300), Is.EqualTo(new Vector3(1, 3, 2)));
        }

        [Test]
        public void InvalidDnaIsRejectedBeforeNativeParsing()
        {
            Assert.Throws<ArgumentException>(() => new NativeRig(new byte[32]));
        }

        [Test]
        public void NativeJawAndBlinkEvaluateAndReturnToNeutralWithoutDrift()
        {
            using (var rig = OpenKellan())
            {
                var inputs = new float[rig.RawControlNames.Length];
                rig.Evaluate(inputs);
                var neutral = (float[])rig.JointDeltas.Clone();
                int jaw = Array.IndexOf(rig.RawControlNames, "CTRL_expressions.jawOpen");
                Assert.That(jaw, Is.GreaterThanOrEqualTo(0));
                inputs[jaw] = 0.7f;
                rig.Evaluate(inputs);
                var posed = (float[])rig.JointDeltas.Clone();
                Assert.That(posed.Zip(neutral, (a, b) => Mathf.Abs(a - b)).Max(), Is.GreaterThan(0.01f));
                rig.Evaluate(inputs);
                Assert.That(rig.JointDeltas, Is.EqualTo(posed).Within(0.000001f));
                Array.Clear(inputs, 0, inputs.Length);
                inputs[Array.IndexOf(rig.RawControlNames, "CTRL_expressions.eyeBlinkL")] = 1;
                rig.Evaluate(inputs);
                Assert.That(rig.JointDeltas.Zip(neutral, (a, b) => Mathf.Abs(a - b)).Max(), Is.GreaterThan(0.001f));
                Array.Clear(inputs, 0, inputs.Length);
                rig.Evaluate(inputs);
                Assert.That(rig.JointDeltas, Is.EqualTo(neutral).Within(0.000001f));
                inputs[0] = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Evaluate(inputs));
                Assert.Throws<ArgumentOutOfRangeException>(() => rig.Evaluate(new float[inputs.Length], rig.LodCount));
            }
        }

        [Test]
        public void ImportedKellanBindingMatchesDnaAndMovesSkinnedVertices()
        {
            using (var check = OpenKellan()) { }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHumanGenerated/Kellan/Kellan.prefab");
            if (!prefab) Assert.Ignore("Local Kellan prefab fixture is absent.");
            var instance = UnityEngine.Object.Instantiate(prefab);
            var baseline = new Mesh();
            var posed = new Mesh();
            try
            {
                var driver = instance.GetComponent<DnaRigDriver>();
                driver.Initialize();
                Assert.That(driver.BoundJointCount, Is.EqualTo(646));
                Assert.That(driver.MaximumBindPositionError, Is.LessThan(0.0001f));
                driver.ClearControls(); driver.EvaluateNow(); driver.face.BakeMesh(baseline);
                driver.SetRawControl("CTRL_expressions.jawOpen", 0.7f); driver.EvaluateNow(); driver.face.BakeMesh(posed);
                var neutralVertices = baseline.vertices;
                var poseVertices = posed.vertices;
                Assert.That(neutralVertices.Zip(poseVertices, Vector3.Distance).Max(), Is.GreaterThan(0.01f));
                driver.ClearControls(); driver.EvaluateNow(); driver.face.BakeMesh(posed);
                Assert.That(neutralVertices.Zip(posed.vertices, Vector3.Distance).Max(), Is.LessThan(0.00001f));
                driver.Shutdown();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(posed);
            }
        }

        [Test]
        public void BodyJointDeltasLayerOnTheAnimatedPoseWithoutAccumulating()
        {
            using (var check = OpenKellan()) { }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHumanGenerated/Kellan/Kellan.prefab");
            if (!prefab) Assert.Ignore("Local Kellan prefab fixture is absent.");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var driver = instance.GetComponent<DnaRigDriver>();
                driver.Initialize();
                var bones = driver.face.rootBone.GetComponentsInChildren<Transform>(true);
                var neck = bones.Single(t => t.name == "neck_01");
                var jaw = bones.Single(t => t.name == "FACIAL_C_Jaw");
                var bind = neck.localRotation;
                driver.ClearControls(); driver.EvaluateNow();
                var jawRest = jaw.localRotation;
                // Body animation turns the neck; the rig keeps it (neutral deltas) and does not drift over repeated evaluations.
                var animated = Quaternion.Euler(0, 20, 0) * bind;
                neck.localRotation = animated;
                for (int i = 0; i < 3; i++) driver.EvaluateNow();
                Assert.That(Quaternion.Angle(neck.localRotation, animated), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(jaw.localRotation, jawRest), Is.LessThan(.01f), "FACIAL_ joints stay neutral + delta.");
                // Without layering the rig owns the joint again.
                driver.layerBodyJoints = false;
                driver.EvaluateNow();
                Assert.That(Quaternion.Angle(neck.localRotation, bind), Is.LessThan(.01f));
                Assert.That(DnaRigDriver.IsBodyJoint("neck_01") && DnaRigDriver.IsBodyJoint("head") && !DnaRigDriver.IsBodyJoint("FACIAL_C_Jaw"));
                driver.Shutdown();
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test] public void AnimatedMapRegionsMatchDnaOrMaterialNamesAndPackIntoSliceChannels()
        {
            string[] rig = { "head_wm1_normal.head_wm1_blink_L", "head_cm1_color.head_wm1_blink_L", "head_wm3_normal.head_wm13_lips_UL" };
            var regions = new[]
            {
                new AnimatedMapRegion { name = "head_wm1_normal_head_wm1_blink_L", map = 0, slice = 0, channel = 0 },
                new AnimatedMapRegion { name = "head_wm3_normal.head_wm13_lips_UL", map = 2, slice = 9, channel = 2 },
                new AnimatedMapRegion { name = "head_wm2_normal_head_wm2_missing", map = 1, slice = 4, channel = 1 },
            };
            var index = DnaAnimatedMapBinding.MatchRegions(rig, regions, out var unmatched);
            CollectionAssert.AreEqual(new[] { 0, 2, -1 }, index);
            CollectionAssert.AreEqual(new[] { "head_wm2_normal_head_wm2_missing" }, unmatched);
            var weights = new Vector4[DnaAnimatedMapBinding.Maps * DnaAnimatedMapBinding.Slices];
            weights[5] = Vector4.one; // stale values must be cleared
            DnaAnimatedMapBinding.Pack(new[] { .25f, .9f, .5f }, index, regions, weights);
            Assert.AreEqual(new Vector4(.25f, 0, 0, 0), weights[0]);
            Assert.AreEqual(new Vector4(0, 0, .5f, 0), weights[2 * DnaAnimatedMapBinding.Slices + 9]);
            Assert.AreEqual(Vector4.zero, weights[5]);
            Assert.AreEqual(Vector4.zero, weights[DnaAnimatedMapBinding.Slices + 4]);
            Assert.Throws<ArgumentOutOfRangeException>(() => DnaAnimatedMapBinding.MatchRegions(rig,
                new[] { new AnimatedMapRegion { name = "x", map = 3, slice = 0, channel = 0 } }, out _));
        }

        [Test] public void BrowRaiseDrivesItsWrinkleRegionInTheRigOutputs()
        {
            using var rig = OpenKellan();
            int map = Array.FindIndex(rig.AnimatedMapNames, n => n.Replace('.', '_') == "head_wm1_normal_head_wm1_browsRaiseInner_L");
            if (map < 0) Assert.Ignore("This DNA has no head_wm1 browsRaiseInner_L animated map.");
            int control = Array.IndexOf(rig.RawControlNames, "CTRL_expressions.browRaiseInL");
            Assert.GreaterOrEqual(control, 0);
            var inputs = new float[rig.RawControlNames.Length];
            rig.Evaluate(inputs);
            float neutral = rig.AnimatedMaps[map];
            inputs[control] = 1;
            rig.Evaluate(inputs);
            Assert.That(rig.AnimatedMaps[map], Is.GreaterThan(neutral + .5f), "A full inner brow raise must drive its wrinkle region.");
        }

        [Test] public void SkinShaderDeclaresAnimatedMapKeyword()
        {
            var shader = Shader.Find("MetaHuman/URP/Skin");
            Assert.IsTrue(new UnityEngine.Rendering.LocalKeyword(shader, "MH_ANIMATED_MAPS").isValid);
        }

        [Test] public void MeshBlendShapesMapToRigChannelsAcrossMeshes()
        {
            string[] rig = { "brow_down_L", "Bdown_Blateral__browLower_L", "Braise_Eblink_INL" };
            string[] mesh = { "head_lod0_mesh__brow_down_L", "head_lod0_mesh__Bdown_Blateral__browLower_L",
                              "cartilage_lod0_mesh__Braise_Eblink_INL", "head_lod0_mesh__Braise_Eblink_INL", "brow_down_L", "teeth_lod0_mesh__unknown" };
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 2, 0, -1 }, DnaRigDriver.MapBlendShapes(mesh, rig));
        }

        [Test] public void AdaCorrectiveShapesRespondToExpressions()
        {
            if (Application.platform != RuntimePlatform.OSXEditor || !SystemInfo.processorType.Contains("Apple"))
                Assert.Ignore("The bundled native backend targets macOS arm64; this platform is not validated.");
            var dna = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/MetaHumanSource/Ada/DNA/Ada.behavior.dna.bytes");
            if (!dna) Assert.Ignore("Local Ada DNA fixture is not part of the distributable package.");
            using var rig = new NativeRig(dna.bytes);
            Assert.That(rig.BlendShapeNames.Length, Is.GreaterThan(0));
            var inputs = new float[rig.RawControlNames.Length];
            rig.Evaluate(inputs);
            float neutral = rig.BlendShapes.Max(Mathf.Abs);
            inputs[Array.IndexOf(rig.RawControlNames, "CTRL_expressions.mouthCornerPullL")] = 1;
            inputs[Array.IndexOf(rig.RawControlNames, "CTRL_expressions.browDownL")] = 1;
            rig.Evaluate(inputs);
            Assert.That(neutral, Is.LessThan(1e-4f), "The neutral pose must not activate corrective shapes.");
            Assert.That(rig.BlendShapes.Max(), Is.GreaterThan(.5f), "A full smile and brow-down must activate corrective shapes.");
        }
    }
}
