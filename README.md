# MetaHuman URP

Experimental MetaHuman rendering and interchange for Unity 6000.6 and URP 17.6. Independent integration; not an official Unity or Epic product.

[![Taro talking, recorded from the sample scene](Media/Taro-talking.webp)](Media/Taro-talking.mp4)

*The sample scene in Play mode: one line, 11.7 s, with the real-time hair path. Click for the video with sound.*

| Folder | Contents |
| --- | --- |
| [`MetahumanURP`](MetahumanURP/README.md) | The `com.unity.metahuman` UPM package: importer, skin/eye/hair rendering, DNA face driver, Unreal export tools. |
| [`SampleProject`](SampleProject) | Unity project with one scene: the character Taro in a look-development stage, talking. |

## Getting started

1. Install [Git LFS](https://git-lfs.com) (`git lfs install`), then clone `https://github.com/lofihashbloom/MetahumanURP.git`. The sample's character data is stored through Git LFS; without it the scene opens with missing meshes and textures.
2. Open `SampleProject` in Unity 6000.6 (Unity Hub > Add project from disk). Its manifest references the package with `file:../../MetahumanURP`. The first import takes several minutes.
3. Open `Assets/Taro/TaroPhoto.unity` and enter Play mode.

Tested on Apple silicon with Metal only. The face rig uses a native plugin that ships for macOS arm64, and the deferred hair renderer is Metal only.

## Using the package in another project

In a Unity 6000.6 / URP 17.6 project, choose **Package Manager > Install package from git URL** and enter:

```
https://github.com/lofihashbloom/MetahumanURP.git?path=/MetahumanURP
```

Append `#<tag or commit>` to pin a revision. The package is not on a registry; the `com.unity.*` identifier does not imply Unity ownership.

## The sample scene

Taro says one line, on a loop, while the camera orbits from one side of his face to the other and pushes in. In Play mode:

| Input | Action |
| --- | --- |
| Space | Restart the line |
| C | Stop or resume the camera move |

What drives it (`PhotoShotPlayer` on the `Taro` object):

- **Face.** `TaroPhotoTalk.asset` is a `TalkTrack` of raw DNA rig controls sampled at 30 frames per second, played through the package's `DnaRigDriver`. The mouth curves were generated from the speech recording (loudness, voicing and formants) and then corrected by hand against the recording: the mouth rests closed in the pauses between words, a few syllables were retimed, and openings wider than their loudness supports were reduced. Blinks, gaze and head nods are authored. It is not a captured performance, and the speech stays the clock: the lips follow the audio time.
- **Hair.** Scalp hair is rigid on the head joint, with a light sway (`GroomSway`, the `Hair sway` object), and drawn by the package's deferred groom renderer. Eyebrows, eyelashes, mustache and goatee follow the skin through `GroomSurfaceBinding`; they are bound to the face when Play mode starts, which takes a few seconds.
- **Body and clothes.** The character is a bust: head, neck and the jacket down to the waistband, with the arms lowered from the A-pose. There are no hands, legs or body mesh; they are not part of this look-development test. Head and chest carry a slow procedural gesture and breathing. The jacket is skinned to the body skeleton; there is no cloth simulation, so the collar follows the chest, not the head.
- **Look.** A fixed stage of spot lights and a backdrop, the package's skin, eye and hair shading, and a film grade applied as a renderer feature (`FilmGradeFeature`). The mouth interior is dimmed slightly while the jaw is half open.

Limits to keep in mind: the skin's hair-shadow fields are baked for the rest pose and the stage lights, skin scattering is a screen-space approximation, the leather is the skin shader used as a stand-in (not a measured leather model), and the eye and hair shading are this package's own models. None of this is Unreal or DNA parity.

## Licences

The original code, shaders and tooling are MIT licensed ([`LICENSE`](LICENSE)). The sample also contains third-party content, which keeps its own licence: the MetaHuman character Taro (Epic Games), the leather jacket derived from Unity's Time Ghost character, and a speech recording generated with ElevenLabs. Their sources, licences and what was changed are listed in [`THIRD_PARTY_LICENSES.md`](THIRD_PARTY_LICENSES.md). That content is stored through Git LFS in `SampleProject/Assets/Taro`.

To use your own characters, export them from Unreal with the package's `Tools~/Unreal` scripts and import them with **Tools > MetaHuman > Import Selected Manifest** (see the [package README](MetahumanURP/README.md)).
