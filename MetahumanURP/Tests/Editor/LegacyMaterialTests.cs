using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class LegacyMaterialTests
    {
        [Test] public void LegacyBodyMaskPreservesRoughnessAndDoesNotInterpretAlphaAsFuzz()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/MetaHumanGenerated/Kellan/Materials/MI_BodySynthesized_Simplified.mat");
            if (!material) Assert.Ignore("Local Kellan material fixture is absent.");
            Assert.That(material.GetFloat("_UseSRMF"), Is.EqualTo(1));
            Assert.That(material.GetFloat("_UseSpecularMap"), Is.Zero);
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_SRMFMap")), Does.Contain("T_Body_Specular"));
            Assert.That(material.GetFloat("_UsePackedFuzz"), Is.Zero);
            Assert.That(material.GetFloat("_SpecularCavityPower"), Is.EqualTo(2.5f));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_DetailNormalMap")), Does.Contain("T_SkinMicroNormal001"));
            Assert.That(material.GetFloat("_DetailTiling"), Is.EqualTo(70));
            Assert.That(material.GetFloat("_DetailNormalStrength"), Is.EqualTo(.25f));
            Assert.That(material.GetFloat("_ProfileLobes"), Is.EqualTo(1));
            Assert.That(material.GetFloat("_PrimaryRoughnessScale"), Is.EqualTo(.73f).Within(.0001));
            Assert.That(material.GetFloat("_SecondaryRoughnessScale"), Is.EqualTo(1.33f).Within(.0001));
            Assert.That(material.GetFloat("_SecondaryWeight"), Is.EqualTo(.85f).Within(.0001));
            Assert.That(material.GetFloat("_Scatter"), Is.Zero, "No thickness map was exported for this legacy fixture.");
        }

        [Test] public void LegacyEyeControlsConvertSourceUnitsWithoutChangingRawControlData()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/MetaHumanGenerated/Kellan/Materials/MI_MetaHumanEye_Simplified.mat");
            if (!material) Assert.Ignore("Local Kellan material fixture is absent.");
            Assert.That(material.GetFloat("_IrisBrightnessBoost"), Is.EqualTo(8).Within(0.0001));
            Assert.That(material.GetFloat("_IrisResponse"), Is.EqualTo(.15f).Within(.0001));
            Assert.That(material.GetFloat("_PupilRadius"), Is.EqualTo(.45f).Within(.0001));
            Assert.That(material.GetFloat("_IrisTextureScale"), Is.EqualTo(1 / 1.443953f).Within(0.0001));
            Assert.That(material.GetFloat("_IrisColorBalance"), Is.EqualTo(0.5f));
            Assert.That(material.GetFloat("_IrisRadius"), Is.EqualTo(0.32f).Within(0.0001));
            Assert.That(material.GetFloat("_PupilScale"), Is.EqualTo(1.12f).Within(0.0001));
            Assert.That(material.GetFloat("_ClearCoat"), Is.Zero);
        }

        [Test] public void SourceDisabledEyeLayersRemainDisabledAfterImport()
        {
            const string root = "Assets/MetaHumanGenerated/Kellan/Materials/";
            var cartilage = AssetDatabase.LoadAssetAtPath<Material>(root + "M_Cartilage.mat");
            if (!cartilage) Assert.Ignore("Local Kellan material fixture is absent.");
            Assert.That(cartilage.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
            Assert.That(cartilage.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            Assert.That(cartilage.GetColor("_BaseColor").a, Is.Zero);
            Assert.That(cartilage.GetFloat("_Cutoff"), Is.GreaterThan(0));
            var lashes = AssetDatabase.LoadAssetAtPath<Material>(root + "M_EyelashLowerLODs_Inst.mat");
            Assert.That(lashes.GetFloat("_OpacityStrength"), Is.Zero);
            Assert.That(lashes.GetFloat("_Specular"), Is.Zero);
            var film = AssetDatabase.LoadAssetAtPath<Material>(root + "MI_lacrimal_fluid_Inst.mat");
            Assert.That(film.GetFloat("_Strength"), Is.EqualTo(.01f).Within(.0001));
            Assert.That(film.GetFloat("_FilmMetallic"), Is.EqualTo(1));
            Assert.That(film.GetFloat("_Roughness"), Is.EqualTo(.2f).Within(.0001));
        }
    }
}
