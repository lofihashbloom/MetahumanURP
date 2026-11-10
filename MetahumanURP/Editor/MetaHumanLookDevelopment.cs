using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Editor
{
    public static class MetaHumanLookDevelopment
    {
        const string Output = "Assets/MetaHumanGenerated/LookDevelopment";

        [MenuItem("Tools/MetaHuman/Create Look Development Scene From Selected Prefab")]
        static void CreateSelected()
        {
            var prefab = Selection.activeGameObject;
            if (!prefab || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                throw new InvalidOperationException("Select an imported character prefab in the Project window.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create(prefab);
        }

        public static void Create(GameObject prefab)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp))
                throw new InvalidOperationException("Look development requires URP.");
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var character = instance.GetComponent<MetaHumanCharacter>();
            var surfaces = instance.GetComponentsInChildren<Renderer>();
            if (surfaces.Length == 0) throw new InvalidOperationException("No renderers found in the prefab.");
            Bounds bounds = surfaces[0].bounds;
            foreach (var surface in surfaces) bounds.Encapsulate(surface.bounds);
            Bounds portrait = character && character.face ? character.face.bounds : bounds;
            Vector3 target = portrait.center;
            float extent = Mathf.Max(portrait.size.x, portrait.size.y, 0.12f);

            var camera = new GameObject("Portrait Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 30;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.transform.position = target + new Vector3(0, extent * 0.04f, -extent * 2.5f);
            camera.transform.LookAt(target);
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = 1;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            Directional("Key — warm", new Vector3(35, 145, 0), new Color(1, 0.89f, 0.79f), 2.2f, true);
            Directional("Fill — cool", new Vector3(15, 315, 0), new Color(0.66f, 0.8f, 1), 0.7f, false);
            Directional("Rim", new Vector3(15, 0, 0), new Color(0.8f, 0.88f, 1), 1.4f, true);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.15f, 0.18f, 0.23f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.07f, 0.09f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.025f, 0.03f);
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Output + "/PortraitVolume.asset");
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, Output + "/PortraitVolume.asset"); }
            if (!profile.TryGet<Tonemapping>(out var tone))
            {
                tone = profile.Add<Tonemapping>();
                AssetDatabase.AddObjectToAsset(tone, profile);
            }
            tone.mode.Override(TonemappingMode.ACES);
            var volume = new GameObject("Portrait ACES Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            // Save explicit, inspectable baseline settings in this newly created host only.
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 20;
            QualitySettings.skinWeights = SkinWeights.Unlimited;
            EditorUtility.SetDirty(urp);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, Output + "/Portrait.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Output + "/Portrait.unity", true) };
            Selection.activeGameObject = instance;
        }

        static void Directional(string name, Vector3 euler, Color color, float intensity, bool shadows)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(euler);
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowBias = 0.015f;
            light.shadowNormalBias = 0.02f;
        }

        public static string Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) errors.Add("URP inactive");
            foreach (string name in new[] { "Skin", "Eye", "EyeOverlay", "HairCards", "GroomStrands" })
            {
                var shader = Shader.Find("MetaHuman/URP/" + name);
                if (!shader || ShaderUtil.ShaderHasError(shader)) errors.Add("Shader error: " + name);
            }
            var diffusion = Shader.Find("Hidden/MetaHuman/SkinDiffusion");
            if (!diffusion || ShaderUtil.ShaderHasError(diffusion)) errors.Add("Shader error: SkinDiffusion");
            var groomTransparency = Shader.Find("Hidden/MetaHuman/GroomTransparency");
            if (!groomTransparency || ShaderUtil.ShaderHasError(groomTransparency)) errors.Add("Shader error: GroomTransparency");
            foreach (var surface in UnityEngine.Object.FindObjectsByType<Renderer>())
                foreach (var material in surface.sharedMaterials)
                    if (!material || !material.shader || ShaderUtil.ShaderHasError(material.shader)) errors.Add("Invalid material: " + surface.name);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors.Distinct()));
            return "URP active; surface shaders, skin diffusion shader and scene materials have no reported errors.";
        }

        public static void Capture(string path, int width = 1200, int height = 1200)
        {
            var camera = Camera.main;
            if (!camera) throw new InvalidOperationException("No MainCamera in the scene.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            // Readback must contain display-encoded values; raw half-float HDR readback
            // written directly to PNG looks much darker than the Game view.
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            Texture2D image = null;
            try
            {
                rt.Create();
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                if (image) UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
