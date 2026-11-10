# Changelog

## Unreleased

Fixes from a code review of 0.1.0.

- **Manifest paths.** `MetaHumanImporter.ResolveAsset` normalises backslashes before checking that a file stays inside its bundle and refuses any `..` segment or rooted path. On macOS and Linux a path like `..\..\X` used to pass as a single file name. A manifest entry without a `sha256` is now refused. `export_character.py` writes forward-slash paths on every platform.
- **Look-development scene.** The command no longer replaces the Build Settings scene list (it appends its scene) and no longer changes HDR, MSAA, shadow distance or skin weights silently: it lists the project-wide changes and asks, and does nothing to them in batch mode.
- **Deferred grooms.** A deferred-capable groom material on a renderer the feature cannot draw (a `SkinnedMeshRenderer`) made that groom invisible, drawn by neither path. The feature now warns once and leaves all grooms on the forward path for that camera. The compute rasterizer is only used with a reversed depth buffer and compute shader support; otherwise the hardware mode runs.
- **Camera hooks.** `GroomDeferredFeature` and `SkinDiffusionFeature` shared one static camera-start handler per type, so disposing one instance removed it for the others and their global flags could stay set. The subscription is now reference-counted (`SharedCameraHook`).
- **Documentation.** The package README is rewritten as documentation: requirements, install, import steps, what each component does, limits.
- **Importing a character.** The eye overlay no longer adds or re-enables the eye renderer feature silently: the importer asks, and reports in batch mode (**Tools > MetaHuman > Enable Eye Optics On Active Renderer** does it explicitly). Textures are imported in one batch instead of one reimport each. `.mhtexture` files follow the build target they are imported for.
- **Hair-to-skin shadow fields.** The importer checks each data file's SHA-256, caps the resolution (a huge value overflowed the size check), validates every array and only reads a plain file name next to `Fields.json`. The Unreal groom exporter writes the strand or groom width where a groom has no per-vertex widths (it wrote 0), and the field builder no longer produces NaN from zero widths and fails on any non-finite output.
- **Skin diffusion.** The normal and eye-depth guide target is 32-bit float where the platform supports it at the camera's sample count: half-float depth steps of 2 to 4 mm at 2 to 8 m were coarser than the depth rejection.
- **Per-frame cost.** Self-shadow volumes rescan the scene's lights every 30 frames in play mode instead of every frame, and the shadow features, `GroomShadowVolume` and `GroomSway` stop allocating lists and material arrays each frame. The rig driver classifies body joints once per rig. `CardSurfaceDeformer` reports a failing binding once and disables itself instead of throwing every frame.
- **Deferred grooms.** Material ids for the hardware mode follow material names, not scene-scan order, so they stop changing on shared material assets.
- **Tools.** The groom transparency command lists the strand materials it would switch and asks, with undo. `export_groom_data.py` no longer uses `assert` for control flow (`python -O` removed the export call). The Unreal built-attribute export names its files after the groom asset. `generate_fiber_azimuth.py` gains `--logistic-factor`.
- **Tests.** New tests that need no character data: the field importer's checks and the shared camera hook.

Known and unchanged: the shipped hair azimuth tables were generated without the sqrt(pi/8) factor of Chiang et al. 2016 Eq. 8, so their lobes are about 1.6x wider than the paper's; the hair look was tuned on them (see `--logistic-factor`). Forward-path grooms claimed by the deferred renderer on shared layers still run their vertex stage to cull themselves; give deferred grooms their own layer with `exclusiveLayers`. Non-native textures are imported uncompressed at source size.

## 0.1.0

First version.

- **Import.** Unreal export scripts (`Tools~/Unreal`) write a versioned manifest with source hashes; **Tools > MetaHuman > Import Selected Manifest** builds the prefab, materials and a look-development scene in Unity 6000.6 / URP 17.6.
- **Face rig.** `DnaRigDriver` evaluates the MetaHuman DNA rig through a bundled OpenRigLogic native plugin (macOS arm64 only): joints, corrective blend shapes and animated wrinkle maps. `SkinnedNormalRecompute` rebuilds normals on the deformed face.
- **Skin, eyes and teeth.** A two-lobe skin shader with screen-space diffusion, fuzz and scatter, drawn in a single pass; refractive eyes with an optical snapshot; layered teeth with mouth occlusion.
- **Hair.** Strand grooms with continuous transparency and a deferred, compute-rasterised renderer (Metal), hair self-shadow volumes, hair-to-skin receiver shadows, temporal reconstruction for TAA, strand LOD, light sway, and `GroomSurfaceBinding` for facial hair that follows the skin. Hair cards as a fallback.
- **Sample.** `SampleProject` in the repository: the MetaHuman Taro in a look-development stage, saying one line with lip sync.

Not Unreal or DNA parity: the skin scattering, eye and hair shading are this package's own approximations. Player builds and Windows native binaries are not validated.
