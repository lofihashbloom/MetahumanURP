using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MetaHuman.Editor
{
    /// <summary>Adapters for authored modern material inputs, independent of BRDF choice.</summary>
    public static class ModernMaterialInputs
    {
        public static void ApplySkin(Material material, MaterialRecord source, Func<string, Texture2D> texture)
        {
            if (!material || material.shader.name != "MetaHuman/URP/Skin") return;
            float S(string name, float fallback) => source.scalars?.FirstOrDefault(s => s.name == name)?.value ?? fallback;
            bool sourceHead = (source.baseMaterial ?? "").Split('.').Last() == "M_Head_Baked";
            material.SetVector("_SourceNeckHide", new Vector4(sourceHead ? .01f * S("NeckHideScale", 0) : 0, S("NeckHideRamp", .8f), 0, 0));
            bool head = (source.baseMaterial ?? "").Split('.').Last() == "M_Head_Baked"
                && (source.staticSwitches?.Any(s => s.name == "BakedRoughnessModulation" && s.value) ?? false);
            material.SetFloat("_ModernHeadInputs", head ? 1 : 0);
            if (head)
            {
                material.SetTexture("_SpecularMap", texture("Cavity_MAIN"));
                material.SetTexture("_RoughnessMap", texture("Roughness_MAIN"));
                material.SetFloat("_RoughnessUVFlipY", material.GetTexture("_RoughnessMap")
                    && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_RoughnessMap"))) is NativeTextureImporter ? 1 : 0);
                material.SetFloat("_SpecularUVFlipY", material.GetTexture("_SpecularMap")
                    && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_SpecularMap"))) is NativeTextureImporter ? 1 : 0);
                material.SetVector("_HeadRoughnessControls", new Vector4(S("ChestMinRoughness", .34f), S("ChestMaxRoughness", .35f),
                    .8f + .4f * S("GlobalRoughness", .5f), S("FresnelRoughness", .97f)));
                material.SetFloat("_HeadFresnelExponent", S("FresnelRoughnessExponent", 4.2f));
                material.SetVector("_HeadCavityPowers", new Vector4(S("ChestCavityMapPower", 1), S("CavityMapPower", 1.93f), 0, 0));
                material.SetVector("_HeadRegionMask", new Vector4(.9850000143f, .8999999762f, 0, 0));
            }
            Vector4 V(string name)
            {
                var a = source.vectors?.FirstOrDefault(v => v.name == name)?.value;
                return a != null && a.Length >= 3 ? new Vector4(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1) : Vector4.zero;
            }
            material.SetTexture("_MakeupMask", texture("FoundationBlusherMask"));
            material.SetVector("_MakeupWeights", new Vector4(
                S("FoundationMakeup", 0) * S("FoundationMakeupIntensity", 0) * .75f,
                S("FoundationMakeup", 0) * S("FoundationMakeupConcealerIntensity", 0),
                S("BlusherMakeup", 0) * S("BlusherMakeupIntensity", 0), 0));
            material.SetVector("_FoundationColor", V("FoundationMakeupColor"));
            material.SetVector("_ConcealerColor", V("ConcealerMakeupColor"));
            material.SetVector("_BlusherColor", V("BlusherMakeupColor"));
            material.SetVector("_MakeupRoughness", new Vector4(S("FoundationMakeupRoughness", .5f) + .5f,
                S("BlusherMakeupRoughness", .5f) + .5f, 0, 0));
        }

        public static void ApplyEye(Material material, MaterialRecord source, Texture2D veins = null)
        {
            if (!material || material.shader.name != "MetaHuman/URP/Eye") return;
            // The supported desktop reference uses the native Eye model with
            // IRIS_NORMAL=0; its authored specularity survives GBuffer packing.
            material.SetFloat("_UseAuthoredEyeSpecular", 1);
            material.SetFloat("_ScleraPower", source.scalars?.FirstOrDefault(s => s.name == "ScleraPower")?.value ?? 1);
            float S(string name, float fallback) => source.scalars?.FirstOrDefault(s => s.name == name)?.value ?? fallback;
            material.SetTexture("_VeinsMap", veins); material.SetFloat("_UseVeinsMap", veins ? 1 : 0);
            material.SetFloat("_VeinsPower", S("VeinsPower", .5f));
            material.SetVector("_IrisPaletteVariation", new Vector4(S("IrisColorHueVariation", .1f), S("IrisColorValueVariation", .25f), S("IrisSaturationVariation", .25f), 0));
            Vector4 V(string name, Vector4 fallback)
            {
                var a = source.vectors?.FirstOrDefault(v => v.name == name)?.value;
                return a != null && a.Length >= 3 ? new Vector4(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1) : fallback;
            }
            material.SetFloat("_LimbusDarkening", S("LimbusDarkAmount", .5f));
            material.SetVector("_CloudyIrisColor", V("CloudyIrisColor", Vector4.zero));
            material.SetVector("_EyeCornerColor", V("ScleraCornerDarkColor", Vector4.one));
            material.SetVector("_EyeColorFields", new Vector4(S("ScleraCornerDarkRadius", .6f), S("ScleraCornerDarkHardness", .4f),
                S("CloudyIrisRadius", .13f), S("CloudyIrisHardness", 0)));
        }
    }
}
