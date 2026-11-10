using MetaHuman.Groom;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Tests
{
    public sealed class HairReceiverShadowTests
    {
        static HairReceiverShadowField Field(Vector3 lightPosition, int n = 4)
        {
            var field = ScriptableObject.CreateInstance<HairReceiverShadowField>();
            field.opticalDepth = new Texture3D(n, n, n + 1, TextureFormat.RFloat, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            field.originZMin = new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1);
            field.rightUMin = new Vector4(1, 0, 0, -.1f);
            field.upVMin = new Vector4(0, 1, 0, -.1f);
            field.forwardZMax = new Vector4(0, 0, 1, 2);
            field.scales = new Vector3(5, 5, 1);
            return field;
        }

        static void Destroy(HairReceiverShadowField field)
        {
            Object.DestroyImmediate(field.opticalDepth);
            Object.DestroyImmediate(field);
        }

        [Test] public void AdditionalLightsMapBySlotIdentityInBufferOrderSkippingMain()
        {
            var objects = new GameObject[4];
            var l = new Light[4];
            for (int i = 0; i < 4; i++) { objects[i] = new GameObject("L" + i); l[i] = objects[i].AddComponent<Light>(); }
            try
            {
                var slots = new[] { l[2], null, l[0], null };
                var mapped = HairReceiverShadowFeature.MapAdditionalLights(new[] { l[0], l[1], l[2], l[3] }, 1, slots, 256, out int main);
                Assert.AreEqual(-1, main);
                CollectionAssert.AreEqual(new[] { 2, 0, -1 }, mapped);
                mapped = HairReceiverShadowFeature.MapAdditionalLights(new[] { l[0], l[2] }, 0, slots, 256, out main);
                Assert.AreEqual(2, main);
                CollectionAssert.AreEqual(new[] { 0 }, mapped);
                mapped = HairReceiverShadowFeature.MapAdditionalLights(new[] { l[0], l[1], l[2] }, -1, slots, 2, out main);
                CollectionAssert.AreEqual(new[] { 2, -1 }, mapped);
            }
            finally { foreach (var o in objects) Object.DestroyImmediate(o); }
        }

        [Test] public void FieldValidationRequiresBoundaryPrefixShapeAndSampling()
        {
            var field = Field(Vector3.zero);
            try
            {
                Assert.IsNull(field.Validate());
                field.opticalDepth.filterMode = FilterMode.Point;
                StringAssert.Contains("bilinear", field.Validate());
                Object.DestroyImmediate(field.opticalDepth);
                field.opticalDepth = new Texture3D(4, 4, 4, TextureFormat.RFloat, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                StringAssert.Contains("N+1", field.Validate());
                Object.DestroyImmediate(field.opticalDepth);
                field.opticalDepth = new Texture3D(4, 4, 5, TextureFormat.RHalf, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                StringAssert.Contains("RFloat", field.Validate());
            }
            finally { Destroy(field); }
        }

        [Test] public void VolumeResolvesWorldFramesAndRejectsMovedLightsDuplicatesAndScale()
        {
            var root = new GameObject("Volume");
            var lightObject = new GameObject("Key");
            var field = Field(new Vector3(0, 1, -2));
            try
            {
                var light = lightObject.AddComponent<Light>();
                var volume = root.AddComponent<HairReceiverShadowVolume>();
                volume.lights[0] = light; volume.fields[0] = field;
                root.transform.SetPositionAndRotation(new Vector3(3, 0, 0), Quaternion.Euler(0, 90, 0));
                var expected = root.transform.TransformPoint(new Vector3(0, 1, -2));
                light.transform.position = expected;

                var slots = volume.ResolveSlots();
                Assert.IsTrue(slots[0].Valid, slots[0].error);
                Assert.That(Vector3.Distance(slots[0].originZMin, expected), Is.LessThan(1e-5f));
                Assert.AreEqual(1, slots[0].originZMin.w);
                Assert.That(Vector3.Distance(slots[0].forwardZMax, root.transform.forward), Is.LessThan(1e-5f));
                Assert.AreEqual("Empty", slots[1].error);

                light.transform.position = expected + new Vector3(0, .05f, 0);
                StringAssert.Contains("moved", volume.ResolveSlots()[0].error);
                light.transform.position = expected;

                volume.lights[1] = light; volume.fields[1] = field;
                StringAssert.Contains("more than one slot", volume.ResolveSlots()[1].error);
                volume.lights[1] = null; volume.fields[1] = null;

                root.transform.localScale = new Vector3(2, 2, 2);
                StringAssert.Contains("unit scale", volume.ResolveSlots()[0].error);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(lightObject);
                Destroy(field);
            }
        }

        [Test] public void LastEnabledVolumeIsActiveAndDisablingClearsReceiverGlobal()
        {
            var a = new GameObject("A").AddComponent<HairReceiverShadowVolume>();
            var b = new GameObject("B").AddComponent<HairReceiverShadowVolume>();
            try
            {
                Assert.AreEqual(b, HairReceiverShadowVolume.Active);
                b.enabled = false;
                Assert.AreEqual(a, HairReceiverShadowVolume.Active);
                Shader.SetGlobalFloat("_MHReceiverEnabled", 1);
                a.enabled = false;
                Assert.IsNull(HairReceiverShadowVolume.Active);
                Assert.AreEqual(0, Shader.GetGlobalFloat("_MHReceiverEnabled"));
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        [Test] public void EverySkinDirectLightPathAppliesTheReceiver()
        {
            // The skin's whole lighting is one loop (MHSkinLighting): the main light and both additional-light loops
            // (Forward+ directional and clustered) are fetched there only, each wrapped by the receiver, and every
            // term (surface response, scatter, fuzz, teeth translucency) takes that light.
            string Source(string file) => System.IO.File.ReadAllText("Packages/metahuman-urp/Runtime/Shaders/" + file);
            int Count(string text, string token) => text.Split(token).Length - 1;
            string skin = Source("MetaHumanSkinLighting.hlsl"), surface = Source("MetaHumanSurface.hlsl");
            Assert.AreEqual(1, Count(skin, "Light key = MHReceiverMain(GetMainLight("));
            Assert.AreEqual(2, Count(skin, "Light light = MHReceiverAdditional(GetAdditionalLight("));
            Assert.AreEqual(1, Count(skin, "GetMainLight("), "No unwrapped main light fetch");
            Assert.AreEqual(2, Count(skin, "GetAdditionalLight("), "No unwrapped additional light fetch");
            Assert.AreEqual(1, Count(skin, "MHSkinAddLight(key,"));
            Assert.AreEqual(2, Count(skin, "MHSkinAddLight(light,"));
            // MHSkinAddLight feeds the same light to the scatter/fuzz/translucency terms and to the surface response.
            Assert.AreEqual(1, Count(skin, "MHAddExtraLight(light,"));
            Assert.AreEqual(1, Count(surface, "MHAddExtraLight("), "Only the definition: the skin loop is the one caller");
            // The skin shades through that loop alone; URP's own (unattenuated) loop is compiled for eyes and hair cards.
            Assert.AreEqual(1, Count(surface, "half3 color = MHSkinLighting(inputData, surface"));
            StringAssert.Contains("#else\n    // Eyes and hair cards: URP's own light loop.\n    half3 color = UniversalFragmentPBR(inputData, surface).rgb;\n#endif", surface.Replace("\r\n", "\n"));
            Assert.AreEqual(1, Count(surface, "UniversalFragmentPBR(inputData, surface)"));
            Assert.AreEqual(0, Count(skin, "UniversalFragmentPBR(") + Count(skin, "LightingPhysicallyBased("));
        }

        [Test] public void SkinShaderDeclaresLocalReceiverKeyword()
        {
            var shader = Shader.Find("MetaHuman/URP/Skin");
            Assert.IsNotNull(shader);
            var keyword = new LocalKeyword(shader, "MH_HAIR_RECEIVER_SHADOW");
            Assert.IsTrue(keyword.isValid);
            Assert.AreEqual(ShaderKeywordType.UserDefined, keyword.type);
        }
    }
}
