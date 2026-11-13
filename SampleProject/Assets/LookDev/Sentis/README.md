# On-device conversation (Unity Sentis)

`Assets/Taro/TaroSentis.unity` turns Taro into a chat partner that runs entirely on the device through Unity Inference Engine (Sentis): type a line, Taro answers, speaks the whole reply and lip-syncs it on the DNA rig.

The idle performance (blinks, gaze, head and hair) and the camera orbit of the sample shot are kept; the orbit plays back and forth. While the input field is not focused, **Space** replays the last reply and **C** stops or resumes the camera. **Esc** stops the reply being prepared or spoken. The scene is a copy of `TaroPhoto.unity` with `LocalAvatar` on Taro and the chat UI (reply text at the top, the input field at the bottom).

## Installing the models

The models are not part of the sample, which opens without them; `TaroSentis` then echoes what you type, with a note to install them. Install any subset: each module's adapter compiles only when its package is installed. Check their licences in [`THIRD_PARTY_LICENSES.md`](../../../../THIRD_PARTY_LICENSES.md) first; they take about 6.5 GB through Git LFS (all seven packages).

1. Add the packages, pinned to the tested commits, to `SampleProject/Packages/manifest.json`:

   ```json
   "com.sky.sentis.gemma3-270m-it": "https://huggingface.co/Sky-Kim/com.sky.sentis.gemma3-270m-it.git#723a071fe1e147dcfa8a5ddad0d667dc13385a1b",
   "com.sky.sentis.audio2face": "https://huggingface.co/Sky-Kim/com.sky.sentis.audio2face.git#11bb93ab7ac6ab9b08e2d3603ea2de82a1aac43b",
   "com.sky.sentis.neurosync": "https://huggingface.co/Sky-Kim/com.sky.sentis.neurosync.git#35a3a8710b6a1a377ec4dd63cef19d5b644fb8bb",
   "com.sky.sentis.supertonic": "https://huggingface.co/Sky-Kim/com.sky.sentis.supertonic.git#c1e1e7f542964ebf4921363c384b20d749c595bf",
   "com.sky.sentis.qwen3-0.6b": "https://huggingface.co/Sky-Kim/com.sky.sentis.qwen3-0.6b.git#e6acce68684a60b4aa5dd25ba554cccd287d17af",
   "com.sky.sentis.lfm2.5-350m": "https://huggingface.co/Sky-Kim/com.sky.sentis.lfm2.5-350m.git#7aa6eebc820ae7ef60145df67eb1a2df7d152787",
   "com.sky.sentis.audio2lipsync": "https://huggingface.co/Sky-Kim/com.sky.sentis.audio2lipsync.git#116878db38e6545daac8f07ed0bcb90910077f39"
   ```

2. Set **Project Settings > Player > Active Input Handling** to **Both** and restart the Editor: the chat field is a uGUI `InputField`.
3. Open `Assets/Taro/TaroSentis.unity`, select Taro and add one module of each kind with **Add Component > Avatar Modules > Chat / Speech / Lip Sync**, then enter Play mode. A kind with no module is not used: without a chat model Taro repeats what you type.

In the Editor the models load from the packages. A player build needs the `Models~` folders of the Gemma, Qwen3, LFM2.5 and Supertonic packages copied to `StreamingAssets/<package name>`, and Audio2Lipsync's `Model~/audio2lipsync.sentis` copied to `StreamingAssets/Audio2Lipsync/` (NeuroSync and Audio2Face write their own at build time). Ship each model's licence and NOTICE with the build where it has one (Audio2Face, Qwen3, LFM2.5; see [`THIRD_PARTY_LICENSES.md`](../../../../THIRD_PARTY_LICENSES.md)).

## Modules

The three models are swappable modules. `LocalAvatar` only knows three interfaces ([`AvatarModules.cs`](AvatarModules.cs)): `IChatModule` (SLM), `ISpeechModule` (TTS) and `ILipSyncModule` (audio to ARKit-52 curves in Live Link order). Each model package is plugged in by a small adapter component in [`Modules/<Name>`](Modules), whose assembly compiles only when that package is installed:

| Slot | Module | Id | Package |
| --- | --- | --- | --- |
| Chat | Gemma 3 270M | `gemma3` | [com.sky.sentis.gemma3-270m-it](https://huggingface.co/Sky-Kim/com.sky.sentis.gemma3-270m-it) |
| Chat | LFM2.5 350M | `lfm2` | [com.sky.sentis.lfm2.5-350m](https://huggingface.co/Sky-Kim/com.sky.sentis.lfm2.5-350m) |
| Chat | Qwen3 0.6B (thinking mode is a bool on the module, off by default) | `qwen3` | [com.sky.sentis.qwen3-0.6b](https://huggingface.co/Sky-Kim/com.sky.sentis.qwen3-0.6b) |
| Speech | Supertonic (voice M5; Korean and Japanese detected from the text) | `supertonic` | [com.sky.sentis.supertonic](https://huggingface.co/Sky-Kim/com.sky.sentis.supertonic) |
| Lipsync | Audio2Face-3D | `audio2face` | [com.sky.sentis.audio2face](https://huggingface.co/Sky-Kim/com.sky.sentis.audio2face) |
| Lipsync | NeuroSync | `neurosync` | [com.sky.sentis.neurosync](https://huggingface.co/Sky-Kim/com.sky.sentis.neurosync) |
| Lipsync | Audio2Lipsync (HuBERT-Large + transformer, 52 ARKit channels at 60 fps) | `audio2lipsync` | [com.sky.sentis.audio2lipsync](https://huggingface.co/Sky-Kim/com.sky.sentis.audio2lipsync) |

The whole reply the chat model generates is shown and spoken; nothing is cut to the first sentence. A reply cut off by `maxNewTokens` (96) is spoken up to its last full sentence. To keep replies short, ask for it in the system prompt.

Any slot can be empty: without a chat module Taro says what you type, without a speech module the line is only shown, and without a lipsync module the mouth stays still.

## How a module is chosen

A slot is filled in three steps: the package decides whether the adapter exists, `AvatarModules` finds the adapters that exist, and `LocalAvatar` takes the adapter components on its GameObject when Play mode starts.

1. **Compile time: is the adapter there?** Each adapter has its own assembly definition, gated on its package. `Modules/Audio2Face/LookDev.Avatar.Audio2Face.asmdef`, for example:

   ```json
   "defineConstraints": [ "LOOKDEV_AUDIO2FACE" ],
   "versionDefines": [ { "name": "com.sky.sentis.audio2face", "expression": "0.0.0", "define": "LOOKDEV_AUDIO2FACE" } ]
   ```

   With the package in the manifest, Unity defines the symbol and compiles the adapter. Without it, the assembly is skipped, so its references to the package can't break the build. Installing a model is a line in `manifest.json`; removing it is deleting that line.

2. **Run time: which adapters are installed?** An adapter is a component that implements one interface and carries `[AvatarModule(id, name)]`:

   ```csharp
   [AvatarModule("audio2face", "Audio2Face-3D"), AddComponentMenu("Avatar Modules/Lip Sync/Audio2Face-3D"), DisallowMultipleComponent, Preserve]
   public sealed class Audio2FaceLipSync : MonoBehaviour, ILipSyncModule { … }
   ```

   `AvatarModules.All` scans the loaded assemblies that reference `LookDev.Sentis` for such components; `AvatarModules.Of<ILipSyncModule>()` keeps the ones for one slot. `[Preserve]` keeps an adapter in a stripped (IL2CPP) player even when only the command line adds it.

3. **Play mode: one module per slot.** In `Start`, `Resolve` fills each slot (`chat`, `speech`, `lipSync`), in this order of precedence. Nothing is picked automatically: every slot is an explicit choice.

   | Precedence | Source | Example | Result |
   | --- | --- | --- | --- |
   | 1 | Command line | `-avatarChat lfm2 -avatarLipSync audio2face` | The adapter with that id, added to Taro if it is not there yet. `none` empties the slot. |
   | 2 | Scene | The adapter components on Taro, next to `LocalAvatar` | The component, with its settings in the inspector (backend, voice, Qwen3 thinking, Audio2Face identity). |

   Taro's **Add Component > Avatar Modules** menu lists the modules installed for each kind; installing or removing a package updates it after Unity recompiles. With no component of a kind, that slot stays empty. With two of a kind (say two chat models), the first is used and a warning names it. An id that isn't installed logs a warning and falls back to the component. Removing a package whose adapter is still on Taro leaves a Missing Script there: remove the component too. The chosen modules are logged (`[LocalAvatar] chat: …, speech: …, lipsync: …`). The on-screen status shows only the stages and their times (`reply 0.40s / voice 0.18s / face 0.57s`), not the models. A module that fails to load is logged, its slot is left empty and the status says so; the other modules still work.

After `Resolve`, `LocalAvatar` calls each module's `LoadAsync` once, then runs every turn through the interfaces only:

```
IChatModule.ReplyAsync(ChatRequest)      → reply text      (the adapter applies its model's chat template)
ISpeechModule.SynthesizeAsync(text)      → SpeechAudio     (mono PCM and its sample rate)
ILipSyncModule.AnimateAsync(SpeechAudio) → FaceCurves      (ARKit channels 0..51 in Live Link order, any fps)
ArkitToDna.Apply(FaceCurves)             → CTRL_expressions on the DNA rig
```

What differs between models stays inside the adapter: Gemma 3 has no system role, so its adapter puts the system prompt at the top of the first user turn, while Qwen3 and LFM2.5 use ChatML. Audio2Lipsync resamples the speech to 16 kHz. NeuroSync returns 68 channels, of which `FaceCurves` reads the first 52 through its stride. Swapping Audio2Face-3D for Audio2Lipsync therefore changes nothing in `LocalAvatar` or `ArkitToDna`.

## Adding a model

1. Copy one of [`Modules/*`](Modules) (for a chat model, `Lfm2`; for a lipsync model, `Audio2Lipsync`).
2. In the `.asmdef`, rename the assembly and point its reference, `versionDefines` package name and define symbol at the new package.
3. Implement the interface on a `MonoBehaviour` with `[AvatarModule("id", "Name")]`, `[AddComponentMenu("Avatar Modules/<Kind>/Name")]` and `[Preserve]`; the file name must match the class name. Public fields become the module's settings in the inspector. A lipsync module returns `FaceCurves`: frames of at least 52 values, channels 0..51 in Live Link order, at any frame rate.
4. Add the package to `manifest.json`. The module then appears under **Add Component > Avatar Modules** and answers to its id on the command line. `LocalAvatar` and the UI need no change.
