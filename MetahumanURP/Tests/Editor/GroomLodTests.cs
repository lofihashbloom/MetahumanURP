using System;
using MetaHuman.Editor.Groom;
using MetaHuman.Groom;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Tests
{
    public sealed class GroomLodTests
    {
        [Test] public void HysteresisPreventsThresholdChatterAndMissingCardsFallBack()
        {
            var p = GroomRepresentation.Strands;
            p = GroomLodMath.Select(.19f, p, .2f, .01f, .1f, false, true, true); Assert.AreEqual(GroomRepresentation.Strands, p);
            p = GroomLodMath.Select(.17f, p, .2f, .01f, .1f, false, true, true); Assert.AreEqual(GroomRepresentation.Cards, p);
            p = GroomLodMath.Select(.21f, p, .2f, .01f, .1f, false, true, true); Assert.AreEqual(GroomRepresentation.Cards, p);
            p = GroomLodMath.Select(.23f, p, .2f, .01f, .1f, false, true, true); Assert.AreEqual(GroomRepresentation.Strands, p);
            Assert.AreEqual(GroomRepresentation.Strands, GroomLodMath.Select(0, p, .2f, .01f, .1f, false, true, false));
            Assert.AreEqual(GroomRepresentation.Culled, GroomLodMath.Select(.005f, p, .2f, .01f, .1f, true, true, true));
            Assert.AreEqual(GroomRepresentation.Culled, GroomLodMath.Select(.0105f, GroomRepresentation.Culled, .2f, .01f, .1f, true, true, true));
            Assert.AreEqual(GroomRepresentation.Cards, GroomLodMath.Select(.012f, GroomRepresentation.Culled, .2f, .01f, .1f, true, true, true));
        }

        [Test] public void StrandFractionFollowsScreenHeightInStepsWithHysteresis()
        {
            Assert.AreEqual(1, GroomStrandLod.Fraction(.5f, 1, .35f, .25f, 1, .1f), "Close-ups draw every strand.");
            Assert.AreEqual(1, GroomStrandLod.Fraction(.34f, 1, .35f, .25f, 1, .1f), "Inside the hysteresis band the fraction holds.");
            Assert.AreEqual(.5f, GroomStrandLod.Fraction(.175f, 1, .35f, .25f, 1, .1f), "Half the full-detail height keeps half the strands.");
            Assert.AreEqual(.625f, GroomStrandLod.Fraction(.2f, 1, .35f, .25f, 1, .1f), "Fractions round up to 1/8 steps.");
            Assert.AreEqual(.25f, GroomStrandLod.Fraction(.01f, .5f, .35f, .25f, 1, .1f), "The minimum fraction bounds distant grooms.");
            Assert.AreEqual(.5f, GroomStrandLod.Fraction(.18f, .5f, .35f, .25f, 1, .1f), "Small moves keep the current fraction.");
            Assert.AreEqual(.25f, GroomStrandLod.Fraction(.175f, 1, .35f, .25f, 2, .1f), "The exponent thins faster with distance.");
            Assert.AreEqual(1, GroomStrandLod.Fraction(float.NaN, .5f, .35f, .25f, 1, .1f));
        }

        [Test] public void ProjectionUsesAuthoredFrustumAndHandlesNearAndBehind()
        {
            var go = new GameObject("LOD Camera");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.fieldOfView = 60; camera.aspect = 1; camera.nearClipPlane = .1f;
                var b = new Bounds(new Vector3(0, 0, 10), new Vector3(2, 2, 0));
                Assert.IsTrue(GroomLodMath.TryScreenHeight(b, camera, out float perspective));
                Assert.That(perspective, Is.EqualTo(1 / (10 * Mathf.Tan(30 * Mathf.Deg2Rad))).Within(1e-5));
                camera.aspect = 2;
                Assert.IsTrue(GroomLodMath.TryScreenHeight(b, camera, out float wide)); Assert.That(wide, Is.EqualTo(perspective).Within(1e-5));
                camera.orthographic = true; camera.orthographicSize = 5;
                GroomLodMath.TryScreenHeight(b, camera, out float ortho); Assert.That(ortho, Is.EqualTo(.2f).Within(1e-5));
                GroomLodMath.TryScreenHeight(new Bounds(Vector3.zero, Vector3.one), camera, out float near); Assert.AreEqual(1, near);
                GroomLodMath.TryScreenHeight(new Bounds(new Vector3(0, 0, -10), Vector3.one), camera, out float behind); Assert.AreEqual(0, behind);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public void TransformedBoundsContainEveryCornerUnderNonuniformScale()
        {
            var bounds = new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6));
            var m = Matrix4x4.TRS(new Vector3(4, 2, -1), Quaternion.Euler(23, 51, 17), new Vector3(2, 3, .5f));
            var world = GroomLodMath.TransformBounds(bounds, m); world.Expand(1e-4f);
            for (int i = 0; i < 8; i++) Assert.IsTrue(world.Contains(m.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
        }

        [Test] public void ControllerIsInertByDefaultOwnsAndRestoresExactStates()
        {
            var root = new GameObject("Groom"); var cameraObject = new GameObject("Camera"); var mesh = Triangle();
            try
            {
                var strands = new GameObject("Strands").AddComponent<MeshRenderer>(); strands.transform.SetParent(root.transform); strands.enabled = true;
                var cards = new GameObject("Cards").AddComponent<MeshRenderer>(); cards.transform.SetParent(root.transform); cards.enabled = false;
                var secondCard = new GameObject("Cards2").AddComponent<MeshRenderer>(); secondCard.transform.SetParent(root.transform); secondCard.enabled = false;
                foreach (var renderer in new[] { strands, cards, secondCard }) renderer.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var c = root.AddComponent<GroomLodController>(); c.strandRenderers = new Renderer[] { strands }; c.cardRenderers = new Renderer[] { cards, secondCard };
                Assert.IsFalse(c.automaticSelection); Assert.IsTrue(strands.enabled); Assert.IsFalse(cards.enabled);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 10; camera.transform.position = new Vector3(0, 0, -10);
                c.localReferenceBounds = new Bounds(Vector3.zero, Vector3.one);
                int changes = 0; c.LevelChanged += (_, __) => changes++;
                Assert.IsTrue(c.EvaluateForCamera(camera)); Assert.AreEqual(GroomRepresentation.Cards, c.CurrentRepresentation); Assert.IsFalse(strands.enabled); Assert.IsTrue(cards.enabled);
                c.Restore(); Assert.IsTrue(strands.enabled); Assert.IsFalse(cards.enabled); Assert.AreEqual(2, changes);
                secondCard.gameObject.SetActive(false);
                Assert.IsTrue(c.EvaluateForCamera(camera)); Assert.AreEqual(GroomRepresentation.Strands, c.CurrentRepresentation);
                Assert.IsFalse(cards.enabled); // One valid card must not conceal a missing second chunk.
                c.enabled = false; Assert.IsTrue(strands.enabled); Assert.IsFalse(cards.enabled);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void SharedRendererOwnershipRejectsSecondController()
        {
            var a = new GameObject("A"); var b = new GameObject("B"); var view = new GameObject("View"); var mesh = Triangle();
            try
            {
                var renderer = a.AddComponent<MeshRenderer>(); a.AddComponent<MeshFilter>().sharedMesh = mesh; var one = a.AddComponent<GroomLodController>(); var two = b.AddComponent<GroomLodController>();
                one.strandRenderers = new Renderer[] { renderer }; two.strandRenderers = new Renderer[] { renderer };
                var camera = view.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -5);
                Assert.IsTrue(one.EvaluateForCamera(camera)); Assert.IsFalse(two.EvaluateForCamera(camera));
                one.Restore(); Assert.IsTrue(two.EvaluateForCamera(camera)); two.Restore();
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(view); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void DirectAssignmentsRejectEmptyCardsAndResetTaaWithoutOptionalHistoryComponent()
        {
            var root = new GameObject("Groom"); var view = new GameObject("TAA Camera"); var mesh = Triangle();
            try
            {
                var strands = root.AddComponent<MeshRenderer>(); root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var emptyCard = new GameObject("Empty Card").AddComponent<MeshRenderer>(); emptyCard.transform.SetParent(root.transform);
                var controller = root.AddComponent<GroomLodController>(); controller.strandRenderers = new Renderer[] { strands }; controller.cardRenderers = new Renderer[] { emptyCard };
                var camera = view.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 10; camera.transform.position = new Vector3(0, 0, -10);
                var additional = view.AddComponent<UniversalAdditionalCameraData>(); additional.antialiasing = AntialiasingMode.TemporalAntiAliasing;
                Assert.IsFalse(view.GetComponent<GroomTemporalHistory>()); Assert.IsFalse(additional.resetHistory);
                Assert.IsTrue(controller.EvaluateForCamera(camera)); Assert.AreEqual(GroomRepresentation.Strands, controller.CurrentRepresentation);
                Assert.IsTrue(strands.enabled); Assert.IsFalse(emptyCard.enabled); Assert.IsTrue(additional.resetHistory);
                Assert.Throws<ArgumentException>(() => GroomLodSetup.Configure(root, new Renderer[] { strands }, new Renderer[] { emptyCard }, camera));

                // A present filter with no mesh is also unavailable. Unity's destroyed
                // component wrapper must then behave like a missing filter, not throw.
                var filter = emptyCard.gameObject.AddComponent<MeshFilter>();
                Assert.IsTrue(controller.EvaluateForCamera(camera)); Assert.AreEqual(GroomRepresentation.Strands, controller.CurrentRepresentation);
                Assert.Throws<ArgumentException>(() => GroomLodSetup.Configure(root, new Renderer[] { strands }, new Renderer[] { emptyCard }, camera));
                filter.sharedMesh = mesh;
                Assert.IsTrue(controller.EvaluateForCamera(camera)); Assert.AreEqual(GroomRepresentation.Cards, controller.CurrentRepresentation);
                UnityEngine.Object.DestroyImmediate(filter);
                Assert.IsTrue(controller.EvaluateForCamera(camera)); Assert.AreEqual(GroomRepresentation.Strands, controller.CurrentRepresentation);
                Assert.IsTrue(strands.enabled); Assert.IsFalse(emptyCard.enabled);
                Assert.Throws<ArgumentException>(() => GroomLodSetup.Configure(root, new Renderer[] { strands }, new Renderer[] { emptyCard }, camera));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(view); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static Mesh Triangle() => new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };

        [Test] public void CardBakeIncludesActualCurvesAndProducesBoundedNonconstantCoverage()
        {
            var data = new GroomCurveData { strands = new GroomCurveData.Strand[3], positions = new Vector3[12], widths = new float[12] };
            for (int c = 0; c < 3; c++)
            {
                data.strands[c] = new GroomCurveData.Strand { first = c * 4, count = 4, width = .01f };
                for (int p = 0; p < 4; p++) { data.positions[c * 4 + p] = new Vector3((c - 1) * .1f + p * .02f, -p * .3f, 0); data.widths[c * 4 + p] = .01f; }
            }
            var result = GroomClusterCardBaker.Build(data, Matrix4x4.identity, new GroomClusterCardBaker.Settings { cards = 1, stations = 8, tileWidth = 64, tileHeight = 64 });
            try
            {
                Assert.AreEqual(3, result.sourceStrands); Assert.AreEqual(1, result.cardCount); Assert.AreEqual(42, result.mesh.triangles.Length);
                Assert.AreEqual(21, result.projectedSegments); Assert.That(result.maximumOffPlaneDistance, Is.LessThan(1e-5));
                float min = 1, max = 0; foreach (var pixel in result.coverage.GetPixels()) { min = Mathf.Min(min, pixel.r); max = Mathf.Max(max, pixel.r); }
                Assert.AreEqual(0, min); Assert.That(max, Is.GreaterThan(.5f)); Assert.That(max, Is.LessThanOrEqualTo(1));
                CollectionAssert.AreEqual(new[] { 0, 0, 0 }, result.sourceCurveToCard);
                Assert.Throws<ArgumentException>(() => GroomClusterCardBaker.Build(data, Matrix4x4.Scale(new Vector3(1, 2, 1))));
                float originalWidth = data.widths[0]; data.widths[0] = float.NaN;
                Assert.Throws<ArgumentException>(() => GroomClusterCardBaker.Build(data, Matrix4x4.identity)); data.widths[0] = originalWidth;
                var originalStrand = data.strands[0]; data.strands[0] = new GroomCurveData.Strand { first = 1, count = 4 };
                Assert.Throws<ArgumentException>(() => GroomClusterCardBaker.Build(data, Matrix4x4.identity)); data.strands[0] = originalStrand;
            }
            finally { UnityEngine.Object.DestroyImmediate(result.mesh); UnityEngine.Object.DestroyImmediate(result.coverage); }
        }
    }
}
