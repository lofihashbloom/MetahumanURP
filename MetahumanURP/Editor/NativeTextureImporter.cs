using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MetaHuman.Editor
{
    /// <summary>Loads supported native desktop BC blocks and their original mip chain without a codec pass.</summary>
    [ScriptedImporter(2, "mhtexture")]
    public sealed class NativeTextureImporter : ScriptedImporter
    {
        [Serializable] public sealed class Mip
        {
            public int level, width, height, byteOffset, byteLength;
            public string sha256;
        }

        [Serializable] public sealed class Header
        {
            public int schemaVersion, width, height, sourceWidth, sourceHeight, mipCount, payloadBytes;
            public string name, sourceAsset, sourcePackageSha256, engineVersion, sourcePlatform, format;
            public string rowConvention, addressX, addressY, sourceFilter, payloadSha256, limitations;
            public string compositeTexture, compositeTextureMode, compositePackageSha256;
            public float compositePower;
            public bool srgb, normalMap, sourceFlipGreenApplied;
            public Mip[] mips;
        }

        public FilterMode filterMode = FilterMode.Trilinear;
        [Range(0, 16)] public int anisoLevel = 1;

        static string Hash(byte[] data, int offset, int count)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data, offset, count)).Replace("-", "").ToLowerInvariant();
        }

        static void RequireHash(string actual, string expected, string label)
        {
            if (string.IsNullOrEmpty(expected) || expected.Length != 64 || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native texture SHA256 mismatch: " + label);
        }

        static TextureWrapMode Wrap(string value)
        {
            if (value != null && value.EndsWith("TA_Clamp", StringComparison.Ordinal)) return TextureWrapMode.Clamp;
            if (value != null && value.EndsWith("TA_Mirror", StringComparison.Ordinal)) return TextureWrapMode.Mirror;
            return TextureWrapMode.Repeat;
        }

        static TextureFormat Format(string format)
        {
            switch (format)
            {
                case "BC1_UNorm": return TextureFormat.DXT1;
                case "BC3_UNorm": return TextureFormat.DXT5;
                case "BC5_UNorm": return TextureFormat.BC5;
                case "BC7_UNorm": return TextureFormat.BC7;
                default: throw new InvalidDataException("Unsupported native block texture format: " + format);
            }
        }

        /// <summary>Validate a version-one container without creating or importing any Unity asset.</summary>
        public static Header ReadAndValidate(Stream stream, out byte[] payload, out string json)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("A readable, seekable native texture stream is required.", nameof(stream));
            Header header;
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true), true))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "MHTEX001")
                    throw new InvalidDataException("Invalid native texture magic; expected MHTEX001.");
                uint count = reader.ReadUInt32();
                if (count == 0 || count > 1024 * 1024 || count > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid native texture JSON header size.");
                json = new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)count));
                header = JsonUtility.FromJson<Header>(json);
                if (header == null || header.schemaVersion != 1
                    || header.width < 1 || header.height < 1 || header.width > 16384 || header.height > 16384
                    || header.mipCount < 1 || header.mipCount > 15 || header.mips == null || header.mips.Length != header.mipCount)
                    throw new InvalidDataException("Unsupported or malformed native block texture metadata.");
                Format(header.format);
                if ((header.format == "BC5_UNorm" && header.srgb) || (header.normalMap && header.format != "BC5_UNorm"))
                    throw new InvalidDataException("BC5 must be linear; normal-map data requires the supported BC5 path.");
                long remaining = stream.Length - stream.Position;
                if (remaining <= 0 || remaining > 512L * 1024 * 1024 || remaining != header.payloadBytes)
                    throw new InvalidDataException("Native texture payload byte count does not match its header.");
                payload = reader.ReadBytes((int)remaining);
            }

            int offset = 0;
            int width = header.width, height = header.height;
            for (int level = 0; level < header.mipCount; level++)
            {
                var mip = header.mips[level];
                int blockBytes = header.format == "BC1_UNorm" ? 8 : 16;
                int expected = checked(((width + 3) / 4) * ((height + 3) / 4) * blockBytes);
                if (mip == null || mip.level != level || mip.width != width || mip.height != height
                    || mip.byteOffset != offset || mip.byteLength != expected || expected > payload.Length - offset)
                    throw new InvalidDataException("Invalid block range or dimensions at mip " + level + ".");
                RequireHash(Hash(payload, offset, expected), mip.sha256, "mip " + level);
                offset = checked(offset + expected);
                if (width == 1 && height == 1 && level + 1 < header.mipCount)
                    throw new InvalidDataException("Native texture includes extra mips after 1x1.");
                width = Math.Max(1, width >> 1); height = Math.Max(1, height >> 1);
            }
            if (offset != payload.Length) throw new InvalidDataException("Native texture contains unreferenced payload bytes.");
            RequireHash(Hash(payload, 0, payload.Length), header.payloadSha256, "complete mip payload");

            return header;
        }

        public override void OnImportAsset(AssetImportContext context)
        {
            // The target this import is for (the asset database reimports per platform), not the editor's active one.
            var target = context.selectedBuildTarget;
            if (target != BuildTarget.StandaloneOSX && target != BuildTarget.StandaloneWindows64)
                throw new NotSupportedException("Native block textures support desktop macOS and Windows 64-bit targets only. No decompression fallback is performed.");

            Header header;
            byte[] payload;
            string json;
            using (var stream = File.OpenRead(context.assetPath)) header = ReadAndValidate(stream, out payload, out json);
            var format = Format(header.format);
            if (!SystemInfo.SupportsTextureFormat(format))
                throw new NotSupportedException("This Editor GPU does not support " + header.format + ". No decompression or recompression fallback is performed.");

            var texture = new Texture2D(header.width, header.height, format, header.mipCount, !header.srgb)
            {
                name = string.IsNullOrEmpty(header.name) ? Path.GetFileNameWithoutExtension(context.assetPath) : header.name,
                filterMode = filterMode,
                anisoLevel = anisoLevel,
                wrapModeU = Wrap(header.addressX),
                wrapModeV = Wrap(header.addressY)
            };
            try
            {
                texture.LoadRawTextureData(payload);
                var loadedBytes = texture.GetRawTextureData();
                RequireHash(Hash(loadedBytes, 0, loadedBytes.Length), header.payloadSha256, "Unity raw-data round trip");
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                context.AddObjectToAsset("Texture", texture);
                context.SetMainObject(texture);
                var metadata = new TextAsset(json) { name = "Native Texture Source Metadata" };
                context.AddObjectToAsset("Metadata", metadata);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }
    }
}
