using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetaHuman.Editor.Groom;
using MetaHuman.Groom;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MetaHuman.Tests
{
    public sealed class HairFiberLookupTests
    {
        const int Width = 32, Height = 16, Slices = 4;
        string folder, profileName;

        [SetUp] public void CreateTemporaryAssetFolder()
        {
            profileName = "FiberFixture_" + Guid.NewGuid().ToString("N");
            folder = "Assets/__" + profileName;
            AssetDatabase.CreateFolder("Assets", "__" + profileName);
        }

        [TearDown] public void RemoveOnlyOwnedFixtures()
        {
            AssetDatabase.DeleteAsset(folder);
            // Failed imports may have allocated a texture before rejecting data.
            // Only transient objects carrying this test's unique name are owned here.
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2DArray>())
                if (texture && texture.name.StartsWith(profileName, StringComparison.Ordinal) && !AssetDatabase.Contains(texture))
                    Object.DestroyImmediate(texture);
        }

        byte[] Fixture(string defect = null)
        {
            var metadata = new HairFiberLookupMetadata {
                formatVersion = 1, width = Width, height = Height, slices = Slices,
                profileName = profileName, profileStatus = "diagnostic", eta = 1.55f, betaN = .37f,
                sigmaA = new[] { .8f, 1.9f, 2.4f }, absorptionUnits = "Inverse physical fiber diameter",
                verifiedNativePigment = false, perStrandPigmentVariationSupported = false
            };
            if (defect == "metadataDimensions") metadata.width = Width + 1;
            if (defect == "metadataAbsorption") metadata.sigmaA[1] = -1;
            var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(metadata));
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes(defect == "magic" ? "BADFBR01" : "MHFBR001"));
                writer.Write(defect == "dimensions" ? 0 : Width); writer.Write(Height); writer.Write(Slices); writer.Write(json.Length);
                writer.Write(json);
                for (int slice = 0; slice < Slices; ++slice)
                for (int y = 0; y < Height; ++y)
                for (int x = 0; x < Width; ++x)
                {
                    ushort red = Mathf.FloatToHalf((slice + 1) * .125f);
                    if (slice == 0 && x == 0 && y == 0)
                    {
                        if (defect == "nan") red = 0x7e00;
                        if (defect == "infinity") red = 0x7c00;
                        if (defect == "negative") red = 0xbc00;
                    }
                    writer.Write(red); writer.Write(Mathf.FloatToHalf(y / 32f));
                    writer.Write(Mathf.FloatToHalf(x / 64f)); writer.Write((ushort)0);
                }
            }
            byte[] bytes = stream.ToArray();
            if (defect == "truncated") Array.Resize(ref bytes, bytes.Length - 1);
            return bytes;
        }

        string Import(byte[] bytes, string name = "Fixture")
        {
            string path = folder + "/" + name + ".mhfiber";
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return path;
        }

        [Test] public void ValidFixtureImportsAsLinearFourSliceAssetWithItsProfile()
        {
            string path = Import(Fixture());
            Assert.That(AssetImporter.GetAtPath(path), Is.InstanceOf<HairFiberLookupImporter>());
            var asset = AssetDatabase.LoadAssetAtPath<HairFiberLookupTable>(path);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.metadata.profileName, Is.EqualTo(profileName));
            Assert.That(asset.metadata.profileStatus, Is.EqualTo("diagnostic"));
            Assert.That(asset.metadata.verifiedNativePigment, Is.False);
            Assert.That(asset.metadata.perStrandPigmentVariationSupported, Is.False);
            Assert.That(asset.metadataJson, Does.Contain("Inverse physical fiber diameter"));
            Assert.That(asset.Matches(1.55f, .37f, new Vector3(.8f, 1.9f, 2.4f)), Is.True);
            var texture = asset.angularDensity;
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(Width)); Assert.That(texture.height, Is.EqualTo(Height));
            Assert.That(texture.depth, Is.EqualTo(Slices)); Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBAHalf));
            Assert.That(texture.mipmapCount, Is.EqualTo(1)); Assert.That(texture.isReadable, Is.False);
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(texture.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat)); Assert.That(texture.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2DArray>().Single(), Is.SameAs(texture));
        }

        [Test] public void ImportedHalfPayloadPreservesSliceChannelAndRowOrientationOnGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supports2DArrayTextures
                || (SystemInfo.copyTextureSupport & CopyTextureSupport.TextureToRT) == 0)
                Assert.Ignore("This payload readback requires an Editor graphics device with2D arrays.");
            var asset = AssetDatabase.LoadAssetAtPath<HairFiberLookupTable>(Import(Fixture()));
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var readable = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            var active = RenderTexture.active;
            try
            {
                target.Create();
                for (int slice = 0; slice < Slices; ++slice)
                {
                    Graphics.CopyTexture(asset.angularDensity, slice, 0, target, 0, 0);
                    RenderTexture.active = target;
                    readable.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); readable.Apply();
                    foreach (var point in new[] { new Vector2Int(0, 0), new Vector2Int(7, 5), new Vector2Int(Width - 1, Height - 1) })
                    {
                        Color pixel = readable.GetPixel(point.x, point.y);
                        Assert.That(pixel.r, Is.EqualTo((slice + 1) * .125f).Within(1e-6));
                        Assert.That(pixel.g, Is.EqualTo(point.y / 32f).Within(1e-6));
                        Assert.That(pixel.b, Is.EqualTo(point.x / 64f).Within(1e-6));
                        Assert.That(pixel.a, Is.Zero, "Table alpha is not coverage.");
                    }
                }
            }
            finally { RenderTexture.active = active; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(readable); }
        }

        [TestCase("magic", "Unknown fiber table format")]
        [TestCase("dimensions", "Invalid fiber table dimensions")]
        [TestCase("truncated", "Truncated or oversized fiber table")]
        [TestCase("metadataDimensions", "Invalid fiber table profile")]
        [TestCase("metadataAbsorption", "Invalid fiber absorption")]
        [TestCase("nan", "finite and nonnegative")]
        [TestCase("infinity", "finite and nonnegative")]
        [TestCase("negative", "finite and nonnegative")]
        public void InvalidFixtureCannotProduceUsableAsset(string defect, string expectedReason)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            var errors = new List<string>();
            Application.LogCallback collect = (message, stack, kind) => {
                if (kind == LogType.Error || kind == LogType.Exception) lock (errors) errors.Add(message + " " + stack);
            };
            string path = folder + "/" + defect + ".mhfiber";
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceivedThreaded += collect;
                Import(Fixture(defect), defect);
            }
            catch (Exception error) { lock (errors) errors.Add(error.ToString()); }
            finally { Application.logMessageReceivedThreaded -= collect; LogAssert.ignoreFailingMessages = previous; }
            Assert.That(AssetImporter.GetAtPath(path), Is.InstanceOf<HairFiberLookupImporter>(), "The real ScriptedImporter must own this extension.");
            Assert.That(AssetDatabase.LoadAssetAtPath<HairFiberLookupTable>(path), Is.Null);
            string[] recorded; lock (errors) recorded = errors.ToArray();
            Assert.That(recorded.Any(message => message.IndexOf(expectedReason, StringComparison.OrdinalIgnoreCase) >= 0), Is.True,
                "The rejection must report the expected validation cause, not fail for an unrelated reason. Logs: " + string.Join("\n", recorded));
        }

        [Test] public void RuntimeProfileMatchingRejectsChangedParametersOrMissingMetadata()
        {
            var asset = ScriptableObject.CreateInstance<HairFiberLookupTable>();
            try
            {
                Assert.That(asset.Matches(1.55f, .37f, Vector3.one), Is.False);
                asset.metadata = new HairFiberLookupMetadata { eta = 1.55f, betaN = .37f, sigmaA = new[] { .8f, 1.9f, 2.4f } };
                var sigma = new Vector3(.8f, 1.9f, 2.4f);
                Assert.That(asset.Matches(1.55f, .37f, sigma), Is.True);
                Assert.That(asset.Matches(1.56f, .37f, sigma), Is.False);
                Assert.That(asset.Matches(1.55f, .38f, sigma), Is.False);
                Assert.That(asset.Matches(1.55f, .37f, sigma + Vector3.right * .01f), Is.False);
                Assert.That(asset.Matches(1.55f, .37f, sigma + Vector3.right * 1e-6f), Is.True);
                asset.metadata.sigmaA[0] = float.NaN;
                Assert.That(asset.Matches(1.55f, .37f, sigma), Is.False);
                asset.metadata.sigmaA = new[] { .8f, 1.9f };
                Assert.That(asset.Matches(1.55f, .37f, sigma), Is.False);
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
