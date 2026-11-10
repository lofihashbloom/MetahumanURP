using System;
using System.Linq;
using MetaHuman.Dna;
using MetaHuman.Editor.Groom;
using MetaHuman.Groom;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class CardSurfaceTests
    {
        [Test]
        public void LongHeadCardsKeepTheirShapeAndLodVisibilityWhileFollowingHeadMotion()
        {
            var root = new GameObject("Long card binding fixture");
            var material = new Material(Shader.Find("MetaHuman/URP/HairCards"));
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right * .02f, Vector3.up * .2f },
                triangles = new[] { 0, 1, 2 }, normals = new[] { Vector3.back, Vector3.back, Vector3.back } };
            string output = "Assets/__MetaHumanCardTest_" + Guid.NewGuid().ToString("N");
            try
            {
                var head = new GameObject("head").transform; head.SetParent(root.transform);
                var face = root.AddComponent<SkinnedMeshRenderer>(); face.sharedMesh = mesh;
                face.rootBone = head; face.bones = new[] { head };
                var disabledLod = new GameObject("Inactive LOD"); disabledLod.transform.SetParent(root.transform); disabledLod.SetActive(false);
                MeshRenderer Card(string name, Transform parent)
                {
                    var item = new GameObject(name); item.transform.SetParent(parent, false);
                    item.transform.localPosition = new Vector3(0, 0, .15f);
                    item.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = item.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
                }
                var visible = Card("Hair_Long_LOD0", root.transform);
                var hidden = Card("Hair_Long_LOD4", disabledLod.transform);
                var shape = mesh.vertices; var position = visible.transform.position;
                var reports = CardSurfaceBaker.Bind(root, face, output);
                Assert.That(reports.Count, Is.EqualTo(2));
                Assert.That(visible.transform.parent, Is.SameAs(head));
                Assert.That(Vector3.Distance(visible.transform.position, position), Is.LessThan(1e-6));
                Assert.That(hidden.gameObject.activeInHierarchy, Is.False, "Changing attachment must preserve LOD visibility.");
                Assert.That(mesh.vertices, Is.EqualTo(shape), "Long card geometry must not collapse onto facial triangles.");
                head.position = new Vector3(.3f, .4f, .5f);
                Assert.That(Vector3.Distance(visible.transform.position, position + head.position), Is.LessThan(1e-6));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh);
                if (AssetDatabase.IsValidFolder(output)) AssetDatabase.DeleteAsset(output);
            }
        }

        [Test]
        public void SurfaceProjectionHandlesTriangleInteriorEdgesAndRejectsEmptyGeometry()
        {
            var surface = new TriangleSurface(new[] { Vector3.zero, Vector3.right, Vector3.up }, new[] { 0, 1, 2 });
            var hit = surface.Closest(new Vector3(0.25f, 0.25f, 2));
            Assert.That(Vector3.Distance(hit.point, new Vector3(0.25f, 0.25f, 0)), Is.LessThan(0.000001f));
            Assert.That(Vector3.Distance(hit.barycentric, new Vector3(0.5f, 0.25f, 0.25f)), Is.LessThan(0.000001f));
            hit = surface.Closest(new Vector3(0.8f, 0.8f, 0.5f));
            Assert.That(Vector3.Distance(hit.point, new Vector3(0.5f, 0.5f, 0)), Is.LessThan(0.000001f));
            Assert.Throws<ArgumentException>(() => new TriangleSurface(new[] { Vector3.zero, Vector3.zero, Vector3.zero }, new[] { 0, 1, 2 }));
        }

        [Test]
        public void TriangleAttachmentPreservesOffsetUnderRigidMotion()
        {
            var attachment = new CardSurfaceAsset.Attachment {
                barycentric = new Vector3(0.3f, 0.2f, 0.5f), normalSign = 1, offset = new Vector3(0.01f, 0.02f, 0.03f)
            };
            var a = Vector3.zero; var b = Vector3.right; var c = Vector3.up;
            var rest = CardSurfaceMath.Position(attachment, a, b, c);
            var rotation = Quaternion.Euler(28, 57, -12); var translation = new Vector3(2, -1, 3);
            var actual = CardSurfaceMath.Position(attachment, rotation * a + translation, rotation * b + translation, rotation * c + translation);
            Assert.That(Vector3.Distance(actual, rotation * rest + translation), Is.LessThan(0.000001f));
            // The cross product of a valid 0.1 mm triangle is below Unity's
            // default normalization cutoff, but must retain its normal.
            a = Vector3.zero; b = Vector3.up * 0.0001f; c = Vector3.forward * 0.0001f;
            rest = CardSurfaceMath.Position(attachment, a, b, c);
            actual = CardSurfaceMath.Position(attachment, rotation * a, rotation * b, rotation * c);
            Assert.That(Vector3.Distance(actual, rotation * rest), Is.LessThan(0.000001f));
        }

        [Test]
        public void BoundKellanCardsFollowBrowDeformationAndKeepRestAssetsUnchanged()
        {
            if (Application.platform != RuntimePlatform.OSXEditor || !SystemInfo.processorType.Contains("Apple"))
                Assert.Ignore("The Kellan facial test fixture needs the bundled Apple Silicon backend.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHumanGenerated/Kellan/Kellan.prefab");
            if (!prefab || !prefab.GetComponent<CardSurfaceDeformer>()) Assert.Ignore("Local bound Kellan fixture is absent.");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var driver = instance.GetComponent<DnaRigDriver>(); var deformer = instance.GetComponent<CardSurfaceDeformer>();
                driver.Initialize(); driver.ClearControls(); driver.EvaluateNow(); deformer.UpdateNow();
                var target = deformer.targets.First(t => t.filter.gameObject.activeInHierarchy);
                Assert.That(target.binding.fittedMesh.isReadable, Is.True, "Generated meshes must retain CPU data in a player.");
                var rest = target.binding.fittedMesh.vertices;
                var neutral = target.filter.sharedMesh.vertices;
                Assert.That(neutral.Zip(rest, Vector3.Distance).Max(), Is.LessThan(0.00001f));
                driver.SetRawControl("CTRL_expressions.browRaiseInL", 1);
                driver.SetRawControl("CTRL_expressions.browRaiseOuterL", 1);
                driver.EvaluateNow(); deformer.UpdateNow();
                Assert.That(neutral.Zip(target.filter.sharedMesh.vertices, Vector3.Distance).Max(), Is.GreaterThan(0.0001f));
                Assert.That(target.binding.fittedMesh.vertices, Is.EqualTo(rest));
                Assert.That(target.filter.sharedMesh, Is.Not.SameAs(target.binding.fittedMesh));
                var originalTargets = deformer.targets;
                deformer.targets = Array.Empty<CardSurfaceDeformer.Target>();
                deformer.UpdateNow();
                Assert.That(target.filter.sharedMesh, Is.SameAs(target.binding.fittedMesh));
                deformer.targets = originalTargets;
                deformer.UpdateNow();
                deformer.Release();
                Assert.That(target.filter.sharedMesh, Is.SameAs(target.binding.fittedMesh));
                driver.Shutdown();
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
