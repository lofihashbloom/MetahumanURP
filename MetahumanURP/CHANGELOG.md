# Changelog

## 0.1.0

First version.

- **Import.** Unreal export scripts (`Tools~/Unreal`) write a versioned manifest with source hashes; **Tools > MetaHuman > Import Selected Manifest** builds the prefab, materials and a look-development scene in Unity 6000.6 / URP 17.6.
- **Face rig.** `DnaRigDriver` evaluates the MetaHuman DNA rig through a bundled OpenRigLogic native plugin (macOS arm64 only): joints, corrective blend shapes and animated wrinkle maps. `SkinnedNormalRecompute` rebuilds normals on the deformed face.
- **Skin, eyes and teeth.** A two-lobe skin shader with screen-space diffusion, fuzz and scatter, drawn in a single pass; refractive eyes with an optical snapshot; layered teeth with mouth occlusion.
- **Hair.** Strand grooms with continuous transparency and a deferred, compute-rasterised renderer (Metal), hair self-shadow volumes, hair-to-skin receiver shadows, temporal reconstruction for TAA, strand LOD, light sway, and `GroomSurfaceBinding` for facial hair that follows the skin. Hair cards as a fallback.
- **Sample.** `SampleProject` in the repository: the MetaHuman Taro in a look-development stage, saying one line with lip sync.

Not Unreal or DNA parity: the skin scattering, eye and hair shading are this package's own approximations. Player builds and Windows native binaries are not validated.
