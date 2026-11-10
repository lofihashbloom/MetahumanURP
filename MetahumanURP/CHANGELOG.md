# Changelog

## Unreleased

Fixes from a code review of 0.1.0.

- **Manifest paths.** `MetaHumanImporter.ResolveAsset` normalises backslashes before checking that a file stays inside its bundle and refuses any `..` segment or rooted path. On macOS and Linux a path like `..\..\X` used to pass as a single file name. A manifest entry without a `sha256` is now refused. `export_character.py` writes forward-slash paths on every platform.
- **Look-development scene.** The command no longer replaces the Build Settings scene list (it appends its scene) and no longer changes HDR, MSAA, shadow distance or skin weights silently: it lists the project-wide changes and asks, and does nothing to them in batch mode.
- **Deferred grooms.** A deferred-capable groom material on a renderer the feature cannot draw (a `SkinnedMeshRenderer`) made that groom invisible, drawn by neither path. The feature now warns once and leaves all grooms on the forward path for that camera. The compute rasterizer is only used with a reversed depth buffer and compute shader support; otherwise the hardware mode runs.
- **Camera hooks.** `GroomDeferredFeature` and `SkinDiffusionFeature` shared one static camera-start handler per type, so disposing one instance removed it for the others and their global flags could stay set. The subscription is now reference-counted (`SharedCameraHook`).
- **Documentation.** The package README is rewritten as documentation: requirements, install, import steps, what each component does, limits.

## 0.1.0

First version.

- **Import.** Unreal export scripts (`Tools~/Unreal`) write a versioned manifest with source hashes; **Tools > MetaHuman > Import Selected Manifest** builds the prefab, materials and a look-development scene in Unity 6000.6 / URP 17.6.
- **Face rig.** `DnaRigDriver` evaluates the MetaHuman DNA rig through a bundled OpenRigLogic native plugin (macOS arm64 only): joints, corrective blend shapes and animated wrinkle maps. `SkinnedNormalRecompute` rebuilds normals on the deformed face.
- **Skin, eyes and teeth.** A two-lobe skin shader with screen-space diffusion, fuzz and scatter, drawn in a single pass; refractive eyes with an optical snapshot; layered teeth with mouth occlusion.
- **Hair.** Strand grooms with continuous transparency and a deferred, compute-rasterised renderer (Metal), hair self-shadow volumes, hair-to-skin receiver shadows, temporal reconstruction for TAA, strand LOD, light sway, and `GroomSurfaceBinding` for facial hair that follows the skin. Hair cards as a fallback.
- **Sample.** `SampleProject` in the repository: the MetaHuman Taro in a look-development stage, saying one line with lip sync.

Not Unreal or DNA parity: the skin scattering, eye and hair shading are this package's own approximations. Player builds and Windows native binaries are not validated.
