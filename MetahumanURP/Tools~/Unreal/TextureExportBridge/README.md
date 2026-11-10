# Native desktop block texture interchange

This independent Editor-only bridge reads `UTexture2D` platform mips through public APIs. It exports the current Unreal Editor platform's encoded blocks and original available mip chain. It contains no texture codec or Unreal rendering implementation.

| Native format | Container format | Unity format | Bytes per 4×4 block | Color space |
|---|---|---|---:|---|
| `PF_DXT1` | `BC1_UNorm` | `DXT1` | 8 | Source sRGB flag preserved |
| `PF_DXT5` | `BC3_UNorm` | `DXT5` | 16 | Source sRGB flag preserved |
| `PF_BC5` | `BC5_UNorm` | `BC5` | 16 | Linear required |
| `PF_BC7` | `BC7_UNorm` | `BC7` | 16 | Source sRGB flag preserved |

Virtual textures, unlisted formats, missing mip data and mismatched block lengths fail explicitly. Normal-map payloads are supported only through linear BC5. There is no decompression, format substitution or recompression fallback. Other format names/aliases are rejected.

The platform blocks can be generated from source art through Unreal's derived-data cache, including authored texture build operations. They are not necessarily bytes embedded in the `.uasset`, another platform's cooked output, or a GPU readback. Source `.uasset` hashes alone do not imply identical built textures when their composite normal-map dependencies differ.

Use a dedicated export process. The public initial-mip API can consume in-memory bulk-data buffers. No source assets are saved or compression/build settings changed. The bridge does not implement a codec, generate replacement mips, convert source art, flip rows or invert channels; it copies the platform data supplied after native texture compilation has completed.

## Build and invoke

Build with Epic's `RunUAT.sh BuildPlugin -Plugin=<this .uplugin> -Package=<new output directory> -TargetPlatforms=Mac`. The current Mac build produces an arm64/x86_64 universal Editor module. SHA256 uses the OS library: CommonCrypto on Mac and BCrypt on Windows. Windows source/build/runtime remains unverified.

Pass the built `.uplugin` to a dedicated RHI-enabled Python commandlet with `-PLUGIN=<built .uplugin>`. Call `unreal.NativeTextureExportLibrary.export_native_texture(asset_path, output_file)` from Python. It returns an empty error string on success, or an explanatory error string on failure. Preserve old exports and receipts; use a fresh destination for a new experiment.

Example: `UnrealEditor-Cmd <project.uproject> -run=pythonscript -AllowCommandletRendering -script=<export script.py> -PLUGIN=<built .uplugin> -unattended -nosplash -nosound`. `-nullrhi` alone does not supply the required platform data. The ordinary offscreen Editor previously exported valid data but encountered a Slate/ICU teardown failure; a commandlet avoids that lifecycle. An error exit requires separate complete receipts/hash checks before any output is accepted.

The host's `Tools/Unreal/export_native_normals.py` retains the four original Kellan BC5 fixtures and source/hash receipts. `Tools/Unreal/export_modern_native_roughness_v9.py` exports only Ada/Taro's built `FaceRoughness_MAIN` textures. Their source texture build settings reference the respective `FaceNormal_MAIN`, with normal-to-green roughness compositing and power1. The raw exported TGA has zero green; transporting it alone loses that build result. Native output is BC1 sRGB, 4096×4096 with 13 available mips for each character, with different payload hashes despite identical source TGA bytes. The dedicated UE5.8.4/Mac commandlet completed with exit0 and no errors/warnings. Native export success is distinct from Unity import/render validation.

The host records these under `Validation/AdditionalFaces/NativeRoughnessExport-v9.json`, `NativeBlockBridgeBuild-v9.json`, `ModernRoughnessTextureSettings-v9.json` and `NativeInputsManifestReceipt-v9.json`. Source roughness/normal package hashes, compositing controls, source/target metadata, per-mip hashes and complete payload hashes accompany the files. New `*-NativeInputs.mhmanifest.json` files derive from `*-MaterialInputs` and replace only each native roughness texture record; all earlier manifests and Kellan native fixtures remain unchanged.

## Container and Unity import

The `.mhtexture` version-one binary layout remains:

1. Eight ASCII bytes `MHTEX001`.
2. A little-endian unsigned 32-bit UTF-8 JSON header length.
3. The JSON header.
4. Unchanged encoded mip blocks, largest mip first.

The header includes explicit format/color space, dimensions, available mip count, source asset/package SHA256, engine/platform, per-mip offsets/hashes and a complete payload SHA256. Optional compositing provenance records the composite texture path/package hash, mode and power. Mip offsets are relative to the payload. Byte length is `ceil(width/4) * ceil(height/4) * blockBytes`; even sub-4-pixel mips occupy a complete block. Extra mips after1×1 and unreferenced trailing bytes are rejected.

`NativeTextureImporter` importer version2 accepts these explicit BC1/3/5/7 container formats and remains compatible with the original BC5 files. It validates the container, every mip and the complete payload. It constructs the corresponding `Texture2D` with the original mip count and `linear = !header.srgb`, then checks a `LoadRawTextureData` round trip and calls `Apply(false,true)` without generating mips. Unsupported desktop texture formats/backends fail. macOS and Windows64 are permitted build targets; that allowance is not evidence of Windows testing.

`Tools/QA/ValidateNativeBlockFormats.cs` is a root-owned execution step: it validates the actual four original BC5 and two new color fixtures, imported Unity format/mip/sRGB metadata, and malformed payload/block/flag variants without importing corrupt assets. It writes a separate `Validation/NativeTextures/BlockFormatValidation-v9.json` and preserves earlier receipts. Consult that receipt for actual execution status; script presence/native export success does not prove Unity tests passed. BC3/BC7 format branches are compiled but are not exercised by these native fixtures, so no BC3/BC7 GPU or Windows validation is claimed.

## Orientation and sampling

Rows and encoded components remain unchanged. `sourceFlipGreenApplied` describes the source texture build's existing inversion, not a request to invert imported data again. BC5 reconstructs normal Z in the consumer shader. Color/scalar native maps also need explicit source-to-Unity UV handling because their original platform rows differ from the source-image export/import path. The importer retains source address modes and defaults to trilinear/anisotropy1; reference tests must match native sampling and mip selection. Preserving blocks avoids a new codec pass but does not establish complete sampler, material or renderer parity.
