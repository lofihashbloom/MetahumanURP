using System;
using System.IO;
using System.Security.Cryptography;
using MetaHuman.Editor.Groom;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Tests
{
    /// <summary>
    /// Tests that need no exported character: every input is generated here, so they run from a clean clone.
    /// </summary>
    public sealed class ImporterHardeningTests
    {
        string directory;

        [SetUp] public void CreateDirectory() { directory = Path.Combine(Path.GetTempPath(), "metahuman-urp-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void DeleteDirectory() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        // A valid N x N x (N+1) float field next to a Fields.json, as build_receiver_shadow_fields.py writes it.
        string WriteField(int n, string file = "key-OpticalDepth.f32", string hash = null, int? declaredResolution = null, int extraBytes = 0)
        {
            var bytes = new byte[n * n * (n + 1) * 4 + extraBytes];
            for (int i = 0; i + 3 < bytes.Length; i += 4) BitConverter.GetBytes(i * .001f).CopyTo(bytes, i);
            string target = Path.Combine(directory, Path.GetFileName(file));
            File.WriteAllBytes(target, bytes);
            if (hash == null) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            int r = declaredResolution ?? n;
            string json = "{\"groomSha256\":\"00\",\"fields\":[{\"name\":\"key\",\"file\":\"" + file.Replace("\\", "\\\\") + "\",\"sha256\":\"" + hash + "\",\"resolution\":" + r +
                ",\"shapeXYZ\":[" + r + "," + r + "," + (r + 1) + "],\"originZMin\":[0,0,-2,1],\"rightUMin\":[1,0,0,-0.5],\"upVMin\":[0,1,0,-0.5],\"forwardZMax\":[0,0,1,3],\"scales\":[1,1,0.5]}]}";
            string path = Path.Combine(directory, "Fields.json");
            File.WriteAllText(path, json);
            return path;
        }

        [Test]
        public void ReceiverShadowFieldLoadsWhenSizeAndHashMatch()
        {
            var field = HairReceiverShadowFieldImporter.Load(WriteField(4), "key");
            try
            {
                Assert.That(field.Validate(), Is.Null);
                Assert.That(field.opticalDepth.width, Is.EqualTo(4));
                Assert.That(field.opticalDepth.depth, Is.EqualTo(5));
            }
            finally { UnityEngine.Object.DestroyImmediate(field.opticalDepth); UnityEngine.Object.DestroyImmediate(field); }
        }

        [Test]
        public void ReceiverShadowFieldRejectsWrongHashSizeAndResolution()
        {
            Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, hash: new string('0', 64)), "key"), "hash mismatch");
            Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, hash: ""), "key"), "missing hash");
            Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, extraBytes: 4), "key"), "byte count");
            // A resolution whose byte count overflows 32 bits must be refused before any allocation.
            Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, declaredResolution: 1291), "key"), "resolution cap");
        }

        [Test]
        public void ReceiverShadowFieldDataMustBeAFileNameNextToTheJson()
        {
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(directory), Path.GetFileName(directory) + "-outside.f32"), new byte[320]);
            try
            {
                Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, file: "../" + Path.GetFileName(directory) + "-outside.f32"), "key"));
                Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, file: Path.Combine(directory, "key-OpticalDepth.f32")), "key"));
                Assert.Throws<InvalidDataException>(() => HairReceiverShadowFieldImporter.Load(WriteField(4, file: "sub\\key.f32"), "key"));
            }
            finally { File.Delete(Path.Combine(Path.GetDirectoryName(directory), Path.GetFileName(directory) + "-outside.f32")); }
        }

        [Test]
        public void SharedCameraHookStaysSubscribedUntilItsLastUserReleases()
        {
            int calls = 0;
            var hook = new SharedCameraHook((context, camera) => calls++);
            bool first = false, second = false;
            hook.Acquire(ref first); hook.Acquire(ref second);
            hook.Acquire(ref first);                       // idempotent per holder
            var go = new GameObject("hook test camera"); var cam = go.AddComponent<Camera>(); cam.enabled = false;
            var target = new RenderTexture(16, 16, 16);
            try
            {
                cam.targetTexture = target;
                cam.Render(); int withBoth = calls;
                Assert.That(withBoth, Is.GreaterThan(0), "The handler must run while held.");
                hook.Release(ref first);                   // one of two instances disposed: the other still needs the handler
                cam.Render(); int withOne = calls;
                Assert.That(withOne, Is.GreaterThan(withBoth), "Releasing one holder must not remove the shared handler.");
                hook.Release(ref second);
                cam.Render();
                Assert.That(calls, Is.EqualTo(withOne), "The handler is removed with the last holder.");
                hook.Release(ref second);                  // releasing twice is harmless
            }
            finally { cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(go); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }
}
