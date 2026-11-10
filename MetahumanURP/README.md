# MetaHuman for Unity (URP)

`metahuman-urp` imports Epic MetaHuman characters exported from Unreal Engine and renders them in **Unity 6000.6 / URP 17.6**: skin, eyes, teeth, strand hair and the DNA face rig. It is an independent, experimental package. It is not a Unity or Epic product and it does not claim Unreal render parity.

## Requirements and platform support

| | Supported | Not validated |
| --- | --- | --- |
| Editor | Unity 6000.6, URP 17.6, Forward or Forward+ | Other Unity or URP versions |
| Graphics | Metal on Apple silicon | Direct3D, Vulkan, OpenGL |
| Face rig (`DnaRigDriver`) | macOS arm64: the bundled OpenRigLogic plugin | Windows, Intel Macs, Linux |
| Deferred hair (`GroomDeferredFeature`) | Metal, single-sample cameras | Other graphics APIs, MSAA cameras |
| Player builds | | All. The plugin is ad-hoc signed, so a macOS player would need signing and notarisation |

## Install

In the Package Manager choose **Install package from git URL** and enter:

```
https://github.com/lofihashbloom/MetahumanURP.git?path=/MetahumanURP
```

Append `#<tag or commit>` to pin a revision. To work on the package, clone the repository and use **Install package from disk** with this folder's `package.json`.

## Import a character

1. **Export from Unreal.** Run `Tools~/Unreal/export_character.py` through Unreal's Python commandlet with `-AllowCommandletRendering` (not `-nullrhi`). Set `METAHUMAN_SOURCE_ROOT=/Game/MetaHumans/MyCharacter` and `METAHUMAN_EXPORT_DIR=/absolute/path/to/UnityProject/Assets/MetaHumanSource/MyCharacter`. The script reads the assembled character without modifying it and writes meshes, textures and a `.mhmanifest.json` that records every file's SHA-256.
2. **Import in Unity.** Keep the exported folder under `Assets`, select its `.mhmanifest.json` and choose **Tools > MetaHuman > Import Selected Manifest**. The importer refuses files that are missing, have no hash, fail their hash, or point outside the export folder. Generated assets go to `Assets/MetaHumanGenerated/<Name>`, with an `ImportReport.json` listing material assignments and warnings. Reimporting overwrites the generated prefab and materials.
3. **Optional look-development scene.** Select the generated prefab and choose **Tools > MetaHuman > Create Look Development Scene From Selected Prefab**. It creates a portrait scene with three lights and adds it to the build list. The look expects HDR, 4x MSAA, a 20 m shadow distance and unlimited skin weights; these are project-wide settings, so the command lists what would change and asks first.

Other tools in `Tools~/Unreal`: `export_groom_data.py` (strand curves for the groom renderers), `bake_eye_occlusion.py`, `extract_legacy_dna.py`, and two editor-only Unreal plugins (`GroomExportBridge`, `TextureExportBridge`) with their own READMEs. `Tools~/Hair` builds the hair lookup tables and the hair-to-skin shadow fields.

## What is in the package

**Face rig.** `DnaRigDriver` evaluates a MetaHuman DNA behaviour stream through OpenRigLogic and drives the face joints, corrective blend shapes and animated wrinkle-map weights from raw rig controls (`SetRawControl`). `BakedFaceDriver` is a simpler named-control-to-blend-shape driver for exports that carry blend shapes. `SkinnedNormalRecompute` with `SkinnedNormalsFeature` rebuilds normals on the deformed face.

**Skin and teeth.** The `MetaHuman/URP/Skin` shader has two specular lobes, fuzz and a local scatter term, and feeds a screen-space diffusion pass (`SkinDiffusionFeature`, **Tools > MetaHuman > Enable Skin Diffusion On Active Renderer**). A teeth mode adds layered enamel and dentin and a mouth occlusion map.

**Eyes.** `MetaHuman/URP/Eye` shades sclera and iris with refraction into a recessed iris, limbus and pupil controls. `EyeSceneColorFeature` supplies the scene colour the cornea refracts.

**Hair.**
- `MetaHuman/URP/GroomStrands` draws strand ribbons with continuous transparency (`GroomTransparencyFeature`, **Tools > MetaHuman > Enable Continuous Groom Transparency On Active Renderer**).
- `GroomDeferredFeature` rasterises rigid strand grooms in compute and shades each hair pixel once. It draws `MeshRenderer` grooms only: put them on their own layer and set `exclusiveLayers`. Strands that deform with the skin stay on the forward path.
- `GroomShadowVolume` with `GroomShadowFeature` gives hair self-shadowing; `HairReceiverShadowVolume` with `HairReceiverShadowFeature` darkens the skin under the hair from precomputed fields (**Assets > MetaHuman > Import Hair Receiver Shadow Fields...**).
- `GroomSurfaceBinding` attaches facial hair to the deforming face skin; `GroomSway` adds light secondary motion; `GroomStrandLod` and `GroomLodController` reduce strand count or switch to cards with distance; `GroomTemporalHistory` supplies hair depth and motion for TAA.
- `MetaHuman/URP/HairCards` shades card meshes as a fallback.

The sample project in this repository (`SampleProject`) shows these parts working together on one character.

## Limits

- Skin scattering is a screen-space approximation, the eye and hair shading are this package's own models, and hair cards and baked blend-shape playback are simplifications. None of it is Unreal or DNA parity.
- Hair shadow fields on the skin are baked for the rest pose and fixed lights. Self-shadow volumes treat strands as grey occluders, without fibre colour or multiple scattering.
- Skin, eye and hair shaders ignore light layers, light cookies, adaptive probe volumes, lightmaps, decals and LOD cross-fade.
- The compute hair renderer assumes a reversed depth buffer and is only used where the platform has one.
- Most integration tests need exported character data that is not in this repository; without it they are skipped, not passed. The unit tests (parsers, bindings, path checks, shading maths) run from a clean clone.

## Third-party

The package statically links [OpenRigLogic](https://github.com/EpicGames/OpenRigLogic) (MIT, Epic Games) in its macOS plugin; see `Third Party Notices.txt`. MetaHuman characters are not part of the package and keep Epic's terms ([metahuman.com/license](https://www.metahuman.com/license)). The repository's `THIRD_PARTY_LICENSES.md` covers the sample's content.
