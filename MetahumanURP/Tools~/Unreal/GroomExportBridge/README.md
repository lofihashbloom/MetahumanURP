# Original groom data export bridge

This independently authored helper reads public `UGroomAsset` / `FHairDescription` APIs. It contains no Unreal rendering implementation. Build locally with the licensed Unreal installation; keep the compiled plugin outside this UPM package. Tested with Unreal 5.8.4 on macOS (arm64/x64 Editor module built, arm64 export exercised).

```sh
"$UE_ENGINE/Build/BatchFiles/RunUAT.sh" BuildPlugin \
  -Plugin=/absolute/path/GroomExportBridge.uplugin \
  -Package=/absolute/path/GroomExportBridgeBuilt -TargetPlatforms=Mac

export METAHUMAN_GROOM_ASSET=/Game/MetaHumans/Kellan/MaleHair/Hair/Hair_S_360Waves.Hair_S_360Waves
export METAHUMAN_GROOM_EXPORT_DIR=/absolute/path/UnityProject/Assets/MetaHumanSource/Kellan/Groom
"$UE_ENGINE/Binaries/Mac/UnrealEditor-Cmd" /absolute/path/Project.uproject \
  -PLUGIN=/absolute/path/GroomExportBridgeBuilt/GroomExportBridge.uplugin \
  -run=pythonscript -script=/absolute/path/export_groom_data.py \
  -nullrhi -unattended -nosplash -nosound
```

The script supports project-owned `/Game` assets, writes a `.mhgroom` and JSON source/settings/hash receipt, and never saves Unreal assets. Check the command log and receipt rather than trusting shell exit status alone. Python must be enabled in the Unreal project.

`MHGRM001` is little-endian: 8-byte ASCII magic, signed int32 strand and point counts; per strand int32 first/count/guide/group, float32 rootUV(x,y), float32 diameter; per point float32(x,y,z,diameter). Positions and widths retain original asset-local centimetres. A zero stored width means the source attribute was absent, not a recommended render diameter. Guides are retained in export and skipped by the first Unity renderer. The Unity builder takes explicit resolved group width/taper settings; it does not yet reproduce every source width attribute. Root UV and source widths remain available for later implementations.

The initial Unity preview converts UE centimetres `(x,y,z)` to Unity metres `(x,z,-y)/100`, then into the neutral head's local transform. Verify the convention and rest fit for another character before using that conversion. No source groom binding, curve interpolation, simulation or renderer data is transferred.

## Read-only built attribute snapshot

`ExportBuiltGroomAttributes(AssetPath, OutputDirectory)` is a separate editor helper for investigating the asset's native cached platform buffers. It writes `PlatformGroomAttributes.json`, raw buffers, and a candidate original-curve mapping. It never changes the existing `MHGRM001` export or saves an Unreal asset. Build the plugin to a separate directory before loading it temporarily with `-PLUGIN=...`.

The helper uses public `UGroomAsset::GetHairGroupsPlatformData`, `FHairStrandsBulkCommon::Read_DDC`, and the public Derived Data Cache request/response API. If NullRHI leaves platform groups uninitialized, it allocates missing transient group containers with `SetNumGroup(count,false,false)` and calls public `CacheStrandsData` per group; it does not initialize GPU resources. Only the cache may be populated. It restores transient bulk streaming pointers/sizes after taking immutable response-buffer copies.

Platform curve ordering must not be assumed to match imported/source ordering. The helper calls public `FGroomBuilder` APIs on temporary CPU data with source mappings enabled, then marks `sourceMappingAccepted=true` only when native and rebuilt precompression position/count/offset hashes and point/curve counts match, every mapped root matches its original `FHairDescription` curve, and no root mapping fails. Consumers must additionally verify unique IDs, source bounds, guide exclusion, complete expected render-curve coverage and the original `.mhgroom` hash before assigning seeds. A JSON snapshot alone is not permission to use an unverified candidate mapping.

The new helper copies original bytes and API-produced metadata. It contains no native seed generator, shuffle algorithm, position codec or renderer implementation. Seed fields can be read as UNorm8 data using exported chunk offsets/strides; transcoded positions/radii/arclength are retained raw rather than reimplementing their codecs. Output artifacts are diagnostic interchange, not a claim of rendered parity or a portable serialized Unreal asset.
