using System;
using System.Collections.Generic;
using System.IO;
using MetaHuman.Groom;
using UnityEditor;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Imports Fields.json from Tools~/Hair/build_receiver_shadow_fields.py as field assets.</summary>
    public static class HairReceiverShadowFieldImporter
    {
        [Serializable] sealed class Receipt { public string groomSha256; public Row[] fields; }
        [Serializable] sealed class Row
        {
            public string name, file, sha256;
            public int resolution;
            public int[] shapeXYZ;
            public float[] originZMin, rightUMin, upVMin, forwardZMax, scales;
        }

        [MenuItem("Assets/MetaHuman/Import Hair Receiver Shadow Fields...")]
        static void ImportMenu()
        {
            string json = EditorUtility.OpenFilePanel("Fields.json from build_receiver_shadow_fields.py", "", "json");
            if (string.IsNullOrEmpty(json)) return;
            string folder = EditorUtility.SaveFolderPanel("Destination folder inside Assets", "Assets", "");
            if (string.IsNullOrEmpty(folder)) return;
            string relative = FileUtil.GetProjectRelativePath(folder);
            if (string.IsNullOrEmpty(relative)) { EditorUtility.DisplayDialog("Hair receiver shadows", "Choose a folder inside this project's Assets.", "OK"); return; }
            Import(json, relative);
        }

        /// <summary>Loads one field from the builder's JSON row and its .f32 data, without creating assets.</summary>
        public static HairReceiverShadowField Load(string fieldsJsonPath, string name)
        {
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(fieldsJsonPath));
            foreach (var row in receipt.fields)
                if (row.name == name) return Create(row, Path.GetDirectoryName(fieldsJsonPath), receipt.groomSha256);
            throw new ArgumentException("No field named " + name + " in " + fieldsJsonPath);
        }

        /// <summary>Creates a texture and field asset per light under assetFolder; returns the fields.</summary>
        public static List<HairReceiverShadowField> Import(string fieldsJsonPath, string assetFolder)
        {
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(fieldsJsonPath));
            if (receipt?.fields == null || receipt.fields.Length == 0) throw new InvalidDataException("No fields in " + fieldsJsonPath);
            Directory.CreateDirectory(assetFolder);
            var result = new List<HairReceiverShadowField>();
            foreach (var row in receipt.fields)
            {
                var field = Create(row, Path.GetDirectoryName(fieldsJsonPath), receipt.groomSha256);
                string basePath = assetFolder.TrimEnd('/') + "/" + row.name + "-HairReceiverShadow";
                AssetDatabase.CreateAsset(field.opticalDepth, basePath + "-OpticalDepth.asset");
                AssetDatabase.CreateAsset(field, basePath + ".asset");
                result.Add(field);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static HairReceiverShadowField Create(Row row, string directory, string groomSha256)
        {
            int n = row.resolution;
            if (n < 2 || row.shapeXYZ == null || row.shapeXYZ.Length != 3 || row.shapeXYZ[0] != n || row.shapeXYZ[1] != n || row.shapeXYZ[2] != n + 1)
                throw new InvalidDataException("Field " + row.name + " must be N x N x (N+1)");
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, row.file));
            if (bytes.Length != n * n * (n + 1) * 4) throw new InvalidDataException("Field " + row.name + " has the wrong byte count");
            var texture = new Texture3D(n, n, n + 1, TextureFormat.RFloat, false)
            {
                name = row.name + " hair optical depth", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixelData(bytes, 0);
            texture.Apply(false, false);
            var field = ScriptableObject.CreateInstance<HairReceiverShadowField>();
            field.name = row.name + " hair receiver shadow";
            field.opticalDepth = texture;
            field.originZMin = V4(row.originZMin);
            field.rightUMin = V4(row.rightUMin);
            field.upVMin = V4(row.upVMin);
            field.forwardZMax = V4(row.forwardZMax);
            field.scales = new Vector3(row.scales[0], row.scales[1], row.scales[2]);
            field.sourceLight = row.name;
            field.sourceGroomSha256 = groomSha256;
            string error = field.Validate();
            if (error != null) throw new InvalidDataException(row.name + ": " + error);
            return field;
        }

        static Vector4 V4(float[] a) => new Vector4(a[0], a[1], a[2], a[3]);
    }
}
