using System;

namespace MetaHuman.Editor
{
    [Serializable] public sealed class InterchangeManifest
    {
        public int schemaVersion;
        public string characterName, engineVersion, sourceRoot;
        public bool complete;
        public DnaRecord dna;
        public ModelRecord[] models = Array.Empty<ModelRecord>();
        public MaterialRecord[] materials = Array.Empty<MaterialRecord>();
        public TextureRecord[] textures = Array.Empty<TextureRecord>();
        public ExportError[] errors = Array.Empty<ExportError>();
        public string[] notes = Array.Empty<string>();
    }
    [Serializable] public sealed class DnaRecord { public string file, sha256, coordinateConvention; }
    [Serializable] public sealed class ModelRecord
    {
        public string source, file, sha256, name, role;
        public bool skeletal;
        public int cardLod;
        public string cardLayout;
        public int[] sectionMaterialIndices = Array.Empty<int>();
        public MaterialSlot[] slots = Array.Empty<MaterialSlot>();
    }
    [Serializable] public sealed class MaterialSlot { public string name, material; }
    [Serializable] public sealed class MaterialRecord
    {
        public string source, name;
        public bool discardAll;
        public string baseMaterial, packedMaskLayout;
        public SubsurfaceProfileRecord subsurfaceProfile;
        public ScalarParameter[] scalars = Array.Empty<ScalarParameter>();
        public VectorParameter[] vectors = Array.Empty<VectorParameter>();
        public BoolParameter[] staticSwitches = Array.Empty<BoolParameter>();
        public TextureParameter[] parameters = Array.Empty<TextureParameter>();
        public string[] textures = Array.Empty<string>();
    }
    [Serializable] public sealed class SubsurfaceProfileRecord
    {
        public string source;
        public bool burley, meanFreePath;
        public float[] surfaceAlbedo, meanFreePathColor;
        public float meanFreePathDistanceCentimetres, worldUnitScale, roughness0, roughness1, lobeMix;
    }
    [Serializable] public sealed class TextureParameter { public string name, texture; }
    [Serializable] public sealed class ScalarParameter { public string name; public float value; }
    [Serializable] public sealed class BoolParameter { public string name; public bool value; }
    [Serializable] public sealed class VectorParameter { public string name; public float[] value; }
    [Serializable] public sealed class TextureRecord
    {
        public string source, file, sha256, name, compression;
        public string derivedFrom, bakeSourceSha256;
        public bool derived;
        public bool nativeBlocks;
        public string sourcePackageSha256;
        public string sourceFormat;
        public TextureStorageRecord storage;
        public bool srgb, flipGreen;
    }
    [Serializable] public sealed class TextureStorageRecord
    {
        public string container;
        public int width, height, bitsPerChannel, bitsPerPixel;
    }
    [Serializable] public sealed class ExportError { public string asset, error; }
}
