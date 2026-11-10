using System;
using System.IO;
using System.Text;
using MetaHuman.Groom;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    [ScriptedImporter(1, "mhfiber")]
    public sealed class HairFiberLookupImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            using var stream = File.OpenRead(context.assetPath);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "MHFBR001") throw new InvalidDataException("Unknown fiber table format.");
            int width = reader.ReadInt32(), height = reader.ReadInt32(), slices = reader.ReadInt32(), metadataLength = reader.ReadInt32();
            if (width < 32 || width > 2048 || height < 16 || height > 1024 || slices != 4 || metadataLength < 2 || metadataLength > 1048576)
                throw new InvalidDataException("Invalid fiber table dimensions or metadata length.");
            long payloadLength = (long)width * height * slices * 8;
            if (stream.Length != 24L + metadataLength + payloadLength) throw new InvalidDataException("Truncated or oversized fiber table.");
            string json = Encoding.UTF8.GetString(reader.ReadBytes(metadataLength));
            var metadata = JsonUtility.FromJson<HairFiberLookupMetadata>(json);
            if (metadata == null || metadata.formatVersion != 1 || metadata.width != width || metadata.height != height || metadata.slices != slices ||
                metadata.sigmaA == null || metadata.sigmaA.Length != 3 || !float.IsFinite(metadata.eta) || metadata.eta <= 1 ||
                !float.IsFinite(metadata.betaN) || metadata.betaN <= 0 || metadata.betaN > 1)
                throw new InvalidDataException("Invalid fiber table profile.");
            foreach (float absorption in metadata.sigmaA)
                if (!float.IsFinite(absorption) || absorption < 0) throw new InvalidDataException("Invalid fiber absorption.");
            if (!BitConverter.IsLittleEndian) throw new InvalidDataException("Fiber payload requires little-endian host representation.");
            var texture = new Texture2DArray(width, height, slices, TextureFormat.RGBAHalf, false, true)
            {
                name = metadata.profileName + " Angular Density",
                filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            for (int slice = 0; slice < slices; ++slice)
            {
                byte[] bytes = reader.ReadBytes(width * height * 8);
                for (int i = 0; i < bytes.Length; i += 2)
                {
                    int half = bytes[i] | (bytes[i + 1] << 8);
                    if ((half & 0x7c00) == 0x7c00 || ((half & 0x8000) != 0 && (half & 0x7fff) != 0))
                        throw new InvalidDataException("Fiber density payload must be finite and nonnegative.");
                }
                texture.SetPixelData(bytes, 0, slice);
            }
            texture.Apply(false, true);
            var asset = ScriptableObject.CreateInstance<HairFiberLookupTable>();
            asset.name = metadata.profileName; asset.metadata = metadata; asset.metadataJson = json; asset.angularDensity = texture;
            context.AddObjectToAsset("AngularDensity", texture);
            context.AddObjectToAsset("HairFiberLookup", asset);
            context.SetMainObject(asset);
        }
    }
}
