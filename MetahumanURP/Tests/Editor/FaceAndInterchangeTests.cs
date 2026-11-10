using System;
using System.IO;
using MetaHuman.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class FaceAndInterchangeTests
    {
        [Test]
        public void SkeletalLodRemapOverridesMisleadingFbxMaterialNames()
        {
            var model = new ModelRecord {
                name = "face", skeletal = true,
                slots = new[] { new MaterialSlot { material = "teeth" }, new MaterialSlot { material = "skin" } },
                sectionMaterialIndices = new[] { 1, 0 }
            };
            Assert.That(MetaHumanImporter.ResolveSlot(model, 0, 2, "teeth").material, Is.EqualTo("skin"));
            Assert.That(MetaHumanImporter.ResolveSlot(model, 1, 2, "skin").material, Is.EqualTo("teeth"));
            model.sectionMaterialIndices = Array.Empty<int>();
            Assert.Throws<InvalidDataException>(() => MetaHumanImporter.ResolveSlot(model, 0, 2, "teeth"));
        }

        [Test]
        public void BundleCannotEscapeItsSourceDirectory()
        {
            Assert.Throws<InvalidDataException>(() => MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "../elsewhere.fbx"));
            Assert.Throws<InvalidDataException>(() => MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "/tmp/test.fbx"));
            Assert.That(MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "Meshes/face.fbx"), Is.EqualTo("Assets/Test/Meshes/face.fbx"));
            // Backslashes are separators in manifests: "..\\.." must not pass as one file name on macOS and Linux.
            Assert.Throws<InvalidDataException>(() => MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "..\\..\\ProjectSettings\\ProjectSettings.asset"));
            Assert.Throws<InvalidDataException>(() => MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "Meshes/../../elsewhere.fbx"));
            Assert.Throws<InvalidDataException>(() => MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "C:/tmp/test.fbx"));
            Assert.That(MetaHumanImporter.ResolveAsset("Assets/Test/Kellan.mhmanifest.json", "Meshes\\face.fbx"), Is.EqualTo("Assets/Test/Meshes/face.fbx"));
        }

        [Test]
        public void BakedInputsCombineWithoutAccumulatingAcrossFramesAndClearToNeutral()
        {
            var go = new GameObject("face test");
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            mesh.AddBlendShapeFrame("smile", 100, new[] { Vector3.right }, new[] { Vector3.zero }, new[] { Vector3.zero });
            var mapping = ScriptableObject.CreateInstance<FaceMapping>();
            try
            {
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                var driver = go.AddComponent<BakedFaceDriver>();
                driver.face = renderer;
                driver.mapping = mapping;
                driver.applyInLateUpdate = false;
                mapping.blendShapes = new[] {
                    new FaceMapping.BlendShapeBinding { control = "left", blendShape = "smile", scale = 60 },
                    new FaceMapping.BlendShapeBinding { control = "right", blendShape = "smile", scale = 60 }
                };
                driver.SetControl("left", 1);
                driver.SetControl("right", 1);
                driver.Apply();
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100));
                driver.Apply();
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100));
                driver.ClearControls();
                driver.Apply();
                Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                Assert.Throws<ArgumentOutOfRangeException>(() => driver.SetControl("left", float.NaN));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(mapping);
            }
        }
    }
}
