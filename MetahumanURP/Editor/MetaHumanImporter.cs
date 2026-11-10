using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MetaHuman.Editor
{
    public static class MetaHumanImporter
    {
        [Serializable] public sealed class ImportReport
        {
            public string character, manifest, prefab;
            public int renderers, bones, blendShapes;
            public string dnaBackend;
            public int dnaBoundJoints;
            public List<Groom.CardSurfaceBaker.Report> cardBindings = new List<Groom.CardSurfaceBaker.Report>();
            public List<string> warnings = new List<string>();
            public List<string> assignments = new List<string>();
        }

        [MenuItem("Tools/MetaHuman/Import Selected Manifest")]
        static void ImportSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!path.EndsWith(".mhmanifest.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select an exported .mhmanifest.json in the Project window.");
            Selection.activeObject = Import(path);
        }

        public static string ResolveAsset(string manifestPath, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new InvalidDataException("Bundle paths must be relative.");
            // Manifests use '/' separators. A backslash is an ordinary file-name character on macOS and Linux, so
            // "..\..\X" would pass the containment check below as one segment and only become "../../X" afterwards:
            // normalise first, then refuse rooted paths and any ".." segment outright.
            relativePath = relativePath.Replace('\\', '/');
            if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal) || relativePath.Contains(":")
                || Array.IndexOf(relativePath.Split('/'), "..") >= 0)
                throw new InvalidDataException("Bundle paths must be relative and stay inside the bundle: " + relativePath);
            string project = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(Path.GetDirectoryName(manifestPath) ?? "");
            string file = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !file.StartsWith(project + "Assets" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Bundle files must remain inside their Assets folder.");
            return file.Substring(project.Length).Replace('\\', '/');
        }

        static string Verify(string manifest, string file, string hash)
        {
            string path = ResolveAsset(manifest, file);
            if (!File.Exists(path)) throw new FileNotFoundException("Bundle file missing", path);
            // Every exported file carries its SHA-256; a manifest entry without one is refused rather than trusted.
            if (string.IsNullOrEmpty(hash)) throw new InvalidDataException("Bundle file has no sha256 in the manifest: " + file);
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA256 mismatch: " + path);
                }
            }
            return path;
        }

        public static MaterialSlot ResolveSlot(ModelRecord model, int section, int sectionCount, string fbxMaterialName)
        {
            if (model.skeletal)
            {
                if (model.sectionMaterialIndices.Length != sectionCount)
                    throw new InvalidDataException("Resolved section materials are missing or do not match FBX sections: " + model.name + ". Re-export the bundle.");
                int index = model.sectionMaterialIndices[section];
                if (index < 0 || index >= model.slots.Length)
                    throw new InvalidDataException("Invalid section material index in " + model.name);
                return model.slots[index];
            }
            return model.slots.FirstOrDefault(s => s.material.Split('.').Last() == fbxMaterialName)
                ?? (section < model.slots.Length ? model.slots[section] : null);
        }

        public static GameObject Import(string manifestPath)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                throw new InvalidOperationException("Activate URP before importing a MetaHuman.");
            var data = JsonUtility.FromJson<InterchangeManifest>(File.ReadAllText(manifestPath));
            if (data == null || data.schemaVersion != 1 || !data.complete || data.models.Length == 0)
                throw new InvalidDataException("Expected a complete schema 1 bundle containing models. Inspect export errors first.");
            // Validate the entire bundle before modifying any import settings or generated assets.
            foreach (var t in data.textures) Verify(manifestPath, t.file, t.sha256);
            foreach (var m in data.models) Verify(manifestPath, m.file, m.sha256);
            if (data.dna != null && !string.IsNullOrEmpty(data.dna.file))
            {
                Verify(manifestPath, data.dna.file, data.dna.sha256);
                if (data.dna.coordinateConvention != "LegacyUnrealFbx")
                    throw new InvalidDataException("This DNA coordinate convention is not supported by the current bridge.");
            }
            string safeName = string.Concat((data.characterName ?? "Character").Where(c => char.IsLetterOrDigit(c) || c == '_'));
            if (safeName.Length == 0) safeName = "Character";
            string output = "Assets/MetaHumanGenerated/" + safeName;
            Directory.CreateDirectory(output + "/Materials");
            AssetDatabase.Refresh();
            var report = new ImportReport { character = data.characterName, manifest = manifestPath };
            report.warnings.Add("Prototype shaders approximate scattering, cornea and hair; render parity is unmeasured.");
            report.warnings.Add("Strand grooms, animated delta-map rendering and automatic LOD assembly are not implemented.");
            var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            var pendingTextures = new List<(string source, string path)>();
            foreach (var t in data.textures)
            {
                string path = ResolveAsset(manifestPath, t.file);
                if (t.nativeBlocks)
                {
                    if (!(AssetImporter.GetAtPath(path) is NativeTextureImporter))
                        throw new InvalidDataException("Native texture requires the .mhtexture importer: " + path);
                    var native = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (!native)
                        throw new InvalidDataException("Validated native block texture failed to load: " + path);
                    textures[t.source] = native;
                    report.assignments.Add(t.name + ": original " + native.format + " blocks and mip chain preserved");
                    continue;
                }
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) throw new InvalidDataException("Texture importer unavailable: " + path);
                bool normal = (t.compression ?? "").IndexOf("NORMALMAP", StringComparison.OrdinalIgnoreCase) >= 0
                    || (t.name ?? "").IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && t.srgb;
                importer.flipGreenChannel = normal && !t.flipGreen;
                int dimension = t.storage != null ? Mathf.Max(t.storage.width, t.storage.height) : 4096;
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(32, dimension)), 32, 16384);
                if (dimension > importer.maxTextureSize)
                    report.warnings.Add("Source texture exceeds the importer size limit: " + path + " (" + dimension + " pixels).");
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = !t.derived;
                if (t.derived) importer.wrapMode = TextureWrapMode.Clamp;
                importer.isReadable = false;
                // Settings are written now and the textures imported together below: one reimport per texture made a
                // character import many times slower than a single batch.
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                pendingTextures.Add((t.source, path));
            }
            AssetDatabase.StartAssetEditing();
            try { foreach (var (_, path) in pendingTextures) AssetDatabase.ImportAsset(path); }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var (source, path) in pendingTextures)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (!texture) throw new InvalidDataException("Texture failed to import: " + path);
                textures[source] = texture;
            }
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var record in data.materials)
                materials[record.source] = CreateMaterial(record, textures, output, report);

            var root = new GameObject(safeName);
            try
            {
                var receipt = root.AddComponent<MetaHumanCharacter>();
                receipt.sourceCharacter = data.sourceRoot;
                receipt.sourceEngine = data.engineVersion;
                receipt.sourceManifest = manifestPath;
                foreach (var model in data.models.GroupBy(m => m.source).Select(group => group.First()))
                {
                    string path = ResolveAsset(manifestPath, model.file);
                    if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                    {
                        importer.importBlendShapes = true;
                        importer.importNormals = ModelImporterNormals.Import;
                        importer.importTangents = ModelImporterTangents.CalculateMikk;
                        // Corrective shapes move geometry only. The FBX-imported normal deltas shade eyelid folds black
                        // when the shapes are active, and Unity-calculated deltas still leave a dark sliver (Ada, v32);
                        // position-only shapes matched Unreal best.
                        importer.importBlendShapeNormals = ModelImporterNormals.None;
                        importer.animationType = ModelImporterAnimationType.Generic;
                        importer.optimizeGameObjects = false;
                        importer.importCameras = false;
                        importer.importLights = false;
                        importer.bakeAxisConversion = true;
                        importer.skinWeights = ModelImporterSkinWeights.Custom;
                        importer.maxBonesPerVertex = 12;
                        importer.minBoneWeight = 0.0001f;
                        importer.SaveAndReimport();
                    }
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!asset) throw new InvalidDataException("Model failed to import: " + path);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    instance.name = model.name;
                    instance.transform.SetParent(root.transform, false);
                    // This first milestone displays the highest available cards only.
                    if (model.role == "hair" && model.cardLod > data.models.Where(m => m.role == "hair" && m.name.Split(new[] { "_LOD" }, StringSplitOptions.None)[0] == model.name.Split(new[] { "_LOD" }, StringSplitOptions.None)[0]).Min(m => m.cardLod))
                        instance.SetActive(false);
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = renderer.sharedMaterials;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            string slotName = slots[i] ? slots[i].name : "";
                            // UE's skeletal FBX exporter can omit the LOD material remap. Preserve
                            // Unreal's resolved section mapping instead of trusting FBX material names.
                            var slot = ResolveSlot(model, i, slots.Length, slotName);
                            if (slot != null && materials.TryGetValue(slot.material, out var material))
                            {
                                slots[i] = material;
                                report.assignments.Add(model.name + ": section " + i + " = " + slot.material);
                            }
                            else report.warnings.Add("Unmapped material: " + model.name + "/" + slotName);
                        }
                        renderer.sharedMaterials = slots;
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            skin.quality = SkinQuality.Auto;
                            skin.updateWhenOffscreen = true;
                            report.bones += skin.bones.Length;
                            report.blendShapes += skin.sharedMesh ? skin.sharedMesh.blendShapeCount : 0;
                            if (model.role == "face" && !receipt.face) receipt.face = skin;
                        }
                    }
                }
                report.cardBindings = Groom.CardSurfaceBaker.Bind(root, receipt.face, output);
                receipt.surfaces = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
                if (data.dna != null && !string.IsNullOrEmpty(data.dna.file))
                {
                    var definitionPath = output + "/" + safeName + "DNA.asset";
                    var definition = AssetDatabase.LoadAssetAtPath<Dna.DnaRigDefinition>(definitionPath);
                    if (!definition)
                    {
                        definition = ScriptableObject.CreateInstance<Dna.DnaRigDefinition>();
                        AssetDatabase.CreateAsset(definition, definitionPath);
                    }
                    definition.dnaBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(ResolveAsset(manifestPath, data.dna.file));
                    if (!definition.dnaBytes) throw new InvalidDataException("Import the standalone DNA file with a .bytes extension.");
                    definition.sourceSha256 = data.dna.sha256;
                    EditorUtility.SetDirty(definition);
                    var driver = root.AddComponent<Dna.DnaRigDriver>();
                    driver.definition = definition;
                    driver.face = receipt.face;
                    try
                    {
                        driver.Initialize();
                        report.dnaBoundJoints = driver.BoundJointCount;
                        report.dnaBackend = "Epic OpenRigLogic 5.8; neutral joint binding verified";
                        driver.Shutdown();
                    }
                    catch (Exception exception) when (exception is DllNotFoundException || exception is BadImageFormatException || exception is EntryPointNotFoundException)
                    {
                        driver.enabled = false;
                        report.dnaBackend = "Native library unavailable on this platform";
                        report.warnings.Add("DNA component disabled: install a compatible native backend before enabling it. " + exception.Message);
                    }
                }
                else if (report.blendShapes == 0)
                    report.warnings.Add("This export contains zero blend shapes. Facial animation requires baked joints or a DNA/RigLogic backend; BakedFaceDriver cannot animate this face by itself.");
                receipt.importNotes = string.Join("\n", report.warnings.Distinct());
                report.renderers = receipt.surfaces.Length;
                report.prefab = output + "/" + safeName + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, report.prefab);
                File.WriteAllText(output + "/ImportReport.json", JsonUtility.ToJson(report, true));
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>True when every texel of the texture's smallest-mip average is black (0 within 1/255).</summary>
        static bool IsBlack(Texture2D texture)
        {
            var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var probe = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            var active = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                probe.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                return probe.GetPixels32().All(c => c.r <= 1 && c.g <= 1 && c.b <= 1 && c.a <= 1);
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(probe);
            }
        }

        static Material CreateMaterial(MaterialRecord source, Dictionary<string, Texture2D> textures, string output, ImportReport report)
        {
            string name = source.name ?? "Material";
            string lower = name.ToLowerInvariant();
            bool hair = lower.Contains("hair") || lower.Contains("lash");
            bool eye = lower.Contains("eye") && !hair && !lower.Contains("occlusion");
            bool occlusion = lower.Contains("occlusion");
            bool wetline = lower.Contains("lacrimal") || lower.Contains("fluid");
            bool skin = lower.Contains("head") || lower.Contains("skin") || lower.Contains("body") || lower.Contains("teeth") || lower.Contains("cartilage");
            bool legacySkin = skin && source.packedMaskLayout == "LegacySRM";
            bool legacyEye = eye && (source.baseMaterial ?? "").Contains("M_MetaHumanEye_Simplified");
            bool modernEye = eye && (source.baseMaterial ?? "").Split('.').Last() == "M_EyeRefractive";
            string baseName = (source.baseMaterial ?? "").Split('.').Last();
            bool fullSkin = skin && (baseName == "M_Head_Baked" || baseName == "M_BodySkin");
            // MetaHuman Creator (UE 5.6+) builds: baked "unified" skin, teeth and eye materials whose
            // parameter names ("Basecolor Baked", "Normal Baked", "SRMF Baked") must be bound exactly; a fuzzy
            // match would pick the animated delta maps listed first.
            bool unified = baseName.StartsWith("M_skin_unified", StringComparison.Ordinal) || baseName == "M_teeth_unified"
                || baseName == "M_eye_eyeball_unified";
            string shaderName = source.discardAll ? "Universal Render Pipeline/Unlit" : occlusion || wetline ? "MetaHuman/URP/EyeOverlay" : hair ? "MetaHuman/URP/HairCards" : eye ? "MetaHuman/URP/Eye" : skin ? "MetaHuman/URP/Skin" : "Universal Render Pipeline/Lit";
            var shader = Shader.Find(shaderName);
            if (!shader) throw new InvalidOperationException("Shader unavailable: " + shaderName);
            string path = output + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            else { material.shader = shader; }
            if (skin && material.HasProperty("_DesktopSkinLighting"))
                material.SetFloat("_DesktopSkinLighting", legacySkin && source.subsurfaceProfile != null ? 1 : 0);
            if (source.discardAll)
            {
                material.SetColor("_BaseColor", new Color(0,0,0,0));
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.SetOverrideTag("RenderType", "TransparentCutout");
                report.assignments.Add(name + ": source constant opacity mask discards all geometry");
                EditorUtility.SetDirty(material); return material;
            }
            var candidates = source.parameters.Select(p => (p.name, p.texture))
                .Concat(source.textures.Select(t => (t.Split('/').Last(), t))).ToArray();
            float Scalar(string key, float fallback) => source.scalars?.FirstOrDefault(s => s.name == key)?.value ?? fallback;
            Texture2D Pick(params string[] keys)
            {
                foreach (string key in keys)
                    foreach (var candidate in candidates)
                        if (candidate.Item1.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                            && !candidate.Item1.Contains("_WM") && !candidate.Item1.Contains("_CM")
                            && textures.TryGetValue(candidate.Item2, out var texture)) return texture;
                return null;
            }
            Texture2D Exact(string parameter)
            {
                var entry = source.parameters.FirstOrDefault(p => string.Equals(p.name, parameter, StringComparison.OrdinalIgnoreCase));
                return entry != null && textures.TryGetValue(entry.texture, out var texture) ? texture : null;
            }
            void Bind(string property, Texture2D texture, string toggle = null)
            {
                if (!material.HasProperty(property)) return;
                material.SetTexture(property, texture);
                if (property == "_RoughnessMap" || property == "_SpecularMap")
                {
                    string uvProperty = property == "_RoughnessMap" ? "_RoughnessUVFlipY" : "_SpecularUVFlipY";
                    if (material.HasProperty(uvProperty)) material.SetFloat(uvProperty,
                        texture && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is NativeTextureImporter ? 1 : 0);
                }
                if (property == "_BumpMap" || property == "_DetailNormalMap")
                {
                    bool native = texture && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is NativeTextureImporter;
                    string flip = property == "_BumpMap" ? "_NormalUVFlipY" : "_DetailUVFlipY";
                    string sign = property == "_BumpMap" ? "_NormalGreenSign" : "_DetailGreenSign";
                    if (material.HasProperty(flip)) material.SetFloat(flip, native ? 1 : 0);
                    if (material.HasProperty(sign)) material.SetFloat(sign, native ? -1 : 1);
                }
                if (toggle != null) material.SetFloat(toggle, texture ? 1 : 0);
                if (texture) report.assignments.Add(name + ": " + property + " = " + AssetDatabase.GetAssetPath(texture));
            }
            var baseColor = unified ? Exact("Basecolor Baked") ?? Exact("Sclera Basecolor Baked")
                : Exact("Color_MAIN") ?? Pick("BaseColor", "Base Color", "Diffuse", "Albedo");
            if (modernEye) baseColor = null;
            Bind("_BaseMap", baseColor);
            Bind("_BumpMap", unified ? Exact("Normal Baked") ?? Exact("Sclera Normal Baked")
                : modernEye ? Exact("ScleraNormal") : Exact("Normal_MAIN") ?? Pick("Normal"));
            if (skin)
            {
                var micro = unified ? Exact("Micro Skin Details Normal") ?? Exact("Detail Normal")
                    : Exact("Normal_MICRO") ?? Pick("SkinMicroNormal", "MicroNormal", "DetailNormal");
                Bind("_DetailNormalMap", micro);
                material.SetFloat("_DetailTiling", fullSkin ? Scalar("MicroNormalTiling", 30) : Scalar("DetailTiling", 30));
                material.SetFloat("_DetailNormalStrength", micro ? (fullSkin ? Scalar("MicroNormalIntensity", 0) : 1 - Mathf.Clamp01(Scalar("DetailFlatten", 1))) : 0);
                material.SetFloat("_NormalScale", fullSkin ? Scalar("NormalIntensity", 1) : 1 - Mathf.Clamp01(Scalar("FlattenNormal", 0)));
            }
            if (!baseColor && !occlusion && !wetline) report.warnings.Add("Base color needs manual review: " + name);
            if (hair || eye || skin)
            {
                var packed = Pick("SRMF", "SRM_Baked", "SpecRoughMetal");
                if (legacySkin) packed = Pick("Specular");
                // Legacy instances may call a packed texture 'Specular'; inspect its source name as well.
                if (!packed)
                    foreach (var parameter in source.parameters)
                        if (parameter.texture.IndexOf("SpecRoughMetal", StringComparison.OrdinalIgnoreCase) >= 0)
                            textures.TryGetValue(parameter.texture, out packed);
                if (fullSkin) packed = null;
                if (unified)
                {
                    packed = Exact("SRMF Baked") ?? Exact("SRM Baked");
                    // Observed on UE 5.8.4 Mac builds: the baked SRMF can be a constant black map in Unreal's own
                    // platform data. Binding it would give zero specular and roughness; use the constants instead.
                    if (packed && IsBlack(packed))
                    {
                        report.warnings.Add(name + ": " + packed.name + " is constant black in the source build; SRMF not bound (constant roughness 0.6, specular 0.4)");
                        packed = null;
                        // Neutral skin constants (not a reconstruction of the missing map).
                        material.SetFloat("_Roughness", .6f);
                        material.SetFloat("_Specular", .4f);
                    }
                }
                Bind("_SRMFMap", packed, "_UseSRMF");
                Bind("_SpecularMap", packed || fullSkin || unified ? null : Pick("Specular"), "_UseSpecularMap");
                Bind("_RoughnessMap", unified ? null : Exact("Roughness_MAIN") ?? Pick("Roughness"), "_UseRoughnessMap");
                Bind("_ScatterMap", unified ? Exact("Scatter Baked") : Pick("Scatter", "Subsurface"));
                if (skin)
                {
                    material.SetFloat("_UsePackedFuzz", legacySkin || (unified && lower.Contains("teeth")) ? 0 : 1);
                    if (legacySkin || fullSkin) material.SetFloat("_Fuzz", 0);
                    if (fullSkin) material.SetFloat("_Specular", Scalar("Specular", .4f));
                }
                if (hair)
                {
                    material.SetFloat("_OpacityStrength", Mathf.Clamp01(Scalar("Opacity", 1)));
                    material.SetFloat("_Specular", Scalar("Specular", .5f));
                    Bind("_OpacityMap", Pick("Coverage", "Opacity", "Texture"), "_UseOpacityMap");
                    material.SetColor("_BaseColor", new Color(0.035f, 0.022f, 0.015f, 1));
                    if (lower.Contains("eyebrow"))
                    {
                        material.SetFloat("_Cutoff", 0.05f);
                        material.SetFloat("_Specular", 0.2f);
                        material.SetFloat("_Roughness", 0.65f);
                    }
                }
                bool dermal = skin && (lower.Contains("head") || lower.Contains("body") || lower.Contains("skin"));
                if (skin)
                {
                    material.SetFloat("_DiffusionAmount", dermal ? 1 : 0);
                    var profile = source.subsurfaceProfile;
                    material.SetFloat("_ProfileLobes", dermal && profile != null ? 1 : 0);
                    if (dermal && profile != null)
                    {
                        material.SetFloat("_PrimaryRoughnessScale", profile.roughness0);
                        material.SetFloat("_SecondaryRoughnessScale", profile.roughness1);
                        material.SetFloat("_SecondaryWeight", profile.lobeMix);
                    }
                    material.SetFloat("_RoughnessMin", Scalar("MinRoughness", 0));
                    material.SetFloat("_RoughnessMax", Scalar("MaxRoughness", 1));
                    material.SetFloat("_SpecularCavityPower", legacySkin ? Scalar("CavityMapPower", 1) : 1);
                    material.SetFloat("_SpecularMultiplier", Scalar("SpecMult", 1) * (legacySkin ? Scalar("Fresnel", 1) : 1));
                    // Bind the source input separately from the prior constant
                    // approximation. Enable only after the candidate convention
                    // has been validated against native material-input captures.
                    bool nativeLegacyNormal = legacySkin && material.GetTexture("_BumpMap")
                        && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap"))) is NativeTextureImporter;
                    material.SetFloat("_LegacySourceFresnel", nativeLegacyNormal ? 1 : 0);
                    material.SetFloat("_LegacyFresnelNormalYSign", 1);
                    material.SetFloat("_LegacySpecularMultiplier", Scalar("SpecMult", 1));
                    material.SetFloat("_LegacyFresnelBase", legacySkin ? Scalar("Fresnel", 1.5f) : 1);
                    material.SetFloat("_LegacyFresnelExponent", Scalar("Fresnel_Exp", 5));
                    material.SetFloat("_LegacyFresnelFlatness", Scalar("FlattenNormal", 0));
                    material.SetFloat("_LegacyFresnelNormalYSign", 1);
                    // No thickness/scatter texture exists in this legacy bundle.
                    // Do not inject broad backlighting through an opaque head.
                    if ((legacySkin || fullSkin) && !Pick("Scatter", "Subsurface")) material.SetFloat("_Scatter", 0);
                }
                if (eye)
                {
                    Bind("_ScleraMap", modernEye ? Exact("ScleraBaseColor") : Pick("Veins", "Sclera_BaseColor"), "_UseScleraMap");
                    Bind("_IrisColorMap", Pick("iris_color_picker"), "_UseIrisColorMap");
                    Bind("_ScleraTintMap", Pick("sclera_tint_picker"), "_UseScleraTintMap");
                    material.SetFloat("_EyeFamily", modernEye ? 1 : 0);
                    if (modernEye)
                    {
                        Bind("_IrisMasksMap", Exact("IrisMasks"));
                        Bind("_IrisHeightMap", Exact("IrisHeight"));
                        Bind("_EyeAxisMap", Pick("T_Eye_N"), "_UseEyeAxisMap");
                        material.SetFloat("_ScaleIrisWithinMask", Scalar("ScaleIrisWithinMask", .85f));
                        material.SetFloat("_IrisColorBalanceSmoothness", Scalar("IrisColorBalanceSmoothness", .5f));
                        material.SetFloat("_RadialStructuralBlend", Scalar("RadialStructuralBlendSwitch", 1));
                        material.SetFloat("_IrisSaturation", Scalar("IrisSaturation", 1));
                        material.SetFloat("_IrisAO", Scalar("AOinIris", .7f));
                        material.SetFloat("_NormalScale", 1 - Mathf.Clamp01(Scalar("ScleraFlattenNormal", .7f)));
                    }
                    material.SetFloat("_Roughness", modernEye ? Scalar("ScleraRoughness", .1f) : Scalar("Roughness", 0.1f));
                    material.SetFloat("_Specular", modernEye ? Scalar("ScleraSpecularity", .4f) : Scalar("Specular", 0.5f));
                    material.SetFloat("_CorneaIOR", Scalar("IoR", 1.336f));
                    material.SetFloat("_IrisRadius", modernEye ? 2 * (.15f + .03f * Scalar("IrisUVRadius", .5f)) : Scalar("Iris_Diameter", 0.32f));
                    material.SetFloat("_PupilScale", Scalar("PupilScale", 1.12f));
                    material.SetFloat("_IrisContrast", Scalar("IrisContrast", 1.5f));
                    float sourceIrisScale = Scalar("IrisTextScale", 1.44f);
                    material.SetFloat("_IrisTextureScale", legacyEye ? 1 / Mathf.Max(sourceIrisScale, 0.01f) : sourceIrisScale);
                    material.SetFloat("_IrisBrightness", Scalar("IrisBrightness", 1));
                    material.SetFloat("_IrisBrightnessBoost", Scalar("IrisBrightnessBoost", 8));
                    // Independent optical model calibrated against the neutral
                    // Kellan reference. These are approximation controls, not
                    // reconstructed Unreal shader equations or universal fits.
                    material.SetFloat("_IrisResponse", legacyEye ? .15f : 1);
                    material.SetFloat("_PupilRadius", legacyEye ? .45f : .23f);
                    material.SetFloat("_IrisColorBalance", Scalar("IrisColorBalance", 0.5f));
                    // The simplified source uses its normal map and Specular value
                    // for corneal reflections; IoR governs refraction independently.
                    if (legacyEye) material.SetFloat("_ClearCoat", 0);
                    material.SetFloat("_DesktopEyeLighting", legacyEye || modernEye ? 1 : 0);
                    material.SetFloat("_ScleraBrightness", Scalar("ScleraBrightness", 0.47f));
                    material.SetVector("_IrisPicker1", new Vector4(Scalar("IrisColor1U", 0.81f), Scalar("IrisColor1V", 0.16f), 0, 0));
                    material.SetVector("_IrisPicker2", new Vector4(Scalar("IrisColor2U", 0.67f), Scalar("IrisColor2V", 0.14f), 0, 0));
                    material.SetVector("_ScleraPicker", new Vector4(Scalar("ScleraTintU", 0.69f), Scalar("ScleraTintV", 0.63f), 0, 0));
                    material.SetColor("_IrisColor", Color.white);
                    material.SetColor("_ScleraColor", Color.white);
                    material.SetFloat("_Scatter", 0); material.SetFloat("_Fuzz", 0);
                    report.assignments.Add(name + ": resolved eye scalar parameters imported; refractive iris remains an independent approximation");
                    if (modernEye) report.warnings.Add(name + ": modern EyeRefractive height/masks and separate iris-lighting model remain unvalidated; legacy Kellan iris calibration is not applied.");
                }
                if (lower.Contains("teeth")) material.SetFloat("_Scatter", 0.08f);
            }
            else if (occlusion || wetline)
            {
                material.SetFloat("_Mode", wetline ? 1 : 0);
                material.SetFloat("_Strength", wetline ? (source.scalars?.FirstOrDefault(s => s.name == "Opacity")?.value ?? .01f) : .35f);
                if (wetline)
                {
                    material.SetFloat("_Roughness", source.scalars?.FirstOrDefault(s => s.name == "Roughness")?.value ?? .2f);
                    material.SetFloat("_FilmMetallic", source.scalars?.FirstOrDefault(s => s.name == "Metallic")?.value ?? 0);
                    material.SetFloat("_FilmSpecular", source.scalars?.FirstOrDefault(s => s.name == "Specular")?.value ?? .5f);
                }
                else
                {
                    var blur = Pick("OcclusionBlurWeight");
                    var transmission = Pick("OcclusionTransmission");
                    material.SetFloat("_UseSceneFilter", 0);
                    if (blur && transmission)
                    {
                        var tint = source.vectors?.FirstOrDefault(v => v.name == "Blur color")?.value;
                        EyeOpticsSetup.ApplyLookups(material, blur, transmission,
                            tint != null && tint.Length >= 3 ? new Color(tint[0], tint[1], tint[2], 1) : Color.white,
                            Scalar("OpticalRadiusMillimetres", .272f));
                    }
                }
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent + (wetline ? 10 : 5);
                report.warnings.Add("Independent " + (occlusion ? "UV edge eye occlusion" : "reflective wet-film") + " approximation requires reference matching: " + name);
            }
            else
            {
                if (material.GetTexture("_BumpMap")) material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_Smoothness", 0.3f);
            }
            if (fullSkin) ModernMaterialInputs.ApplySkin(material, source, Exact);
            else if (skin && material.HasProperty("_MakeupWeights")) material.SetVector("_MakeupWeights", Vector4.zero);
            if (modernEye) ModernMaterialInputs.ApplyEye(material, source, Pick("T_Veins_D"));
            if (lower.Contains("cartilage")) material.SetColor("_BaseColor", new Color(0.55f, 0.24f, 0.18f, 1));
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
