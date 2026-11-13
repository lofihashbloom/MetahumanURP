using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

// The three swappable parts of LocalAvatar: a chat model (SLM), a voice (TTS) and a face (audio -> ARKit curves).
// LocalAvatar only knows these interfaces. A model package is plugged in by an adapter: a component that implements one
// interface, carries [AvatarModule], and lives in its own assembly that compiles only when the package is installed
// (see Modules/*). The modules LocalAvatar uses are the adapter components on its GameObject; installing or removing a
// package needs no change to LocalAvatar.
namespace LookDev.Avatar
{
    public interface IAvatarModule : IDisposable
    {
        /// <summary>Loads and warms up the model. Called once, on the main thread, before any other call.</summary>
        Awaitable LoadAsync(CancellationToken cancel);
    }

    /// <summary>Text reply to a chat turn (SLM). Each model applies its own chat template.</summary>
    public interface IChatModule : IAvatarModule
    {
        /// <param name="onText">The reply so far, while it streams.</param>
        Awaitable<string> ReplyAsync(ChatRequest request, Action<string> onText, CancellationToken cancel);
    }

    /// <summary>Speech for a line of text (TTS). The module picks the language and voice.</summary>
    public interface ISpeechModule : IAvatarModule
    {
        Awaitable<SpeechAudio> SynthesizeAsync(string text, CancellationToken cancel);
    }

    /// <summary>Face animation for speech audio (lipsync).</summary>
    public interface ILipSyncModule : IAvatarModule
    {
        Awaitable<FaceCurves> AnimateAsync(SpeechAudio speech, CancellationToken cancel);
    }

    public readonly struct ChatTurn
    {
        public readonly string User, Reply;
        public ChatTurn(string user, string reply) { User = user; Reply = reply; }
    }

    public sealed class ChatRequest
    {
        public string SystemPrompt;
        /// <summary>Previous exchanges, oldest first.</summary>
        public IReadOnlyList<ChatTurn> History = Array.Empty<ChatTurn>();
        public string Message;
        public int MaxNewTokens = 96;

        /// <summary>The conversation in the ChatML template (Qwen, LFM2 and others), ending with the assistant turn opened.</summary>
        public string ToChatML()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(SystemPrompt)) sb.Append("<|im_start|>system\n").Append(SystemPrompt).Append("<|im_end|>\n");
            foreach (var turn in History)
                sb.Append("<|im_start|>user\n").Append(turn.User).Append("<|im_end|>\n<|im_start|>assistant\n").Append(turn.Reply).Append("<|im_end|>\n");
            return sb.Append("<|im_start|>user\n").Append(Message).Append("<|im_end|>\n<|im_start|>assistant\n").ToString();
        }
    }

    /// <summary>Mono PCM in [-1, 1].</summary>
    public readonly struct SpeechAudio
    {
        public readonly float[] Samples;
        public readonly int SampleRate;
        public float Duration => Samples == null || SampleRate <= 0 ? 0 : Samples.Length / (float)SampleRate;
        public SpeechAudio(float[] samples, int sampleRate) { Samples = samples; SampleRate = sampleRate; }
    }

    /// <summary>
    /// Face curves at a fixed rate. Channels 0..51 of each frame are the ARKit blendshapes in Apple Live Link Face order
    /// (<see cref="LiveLinkNames"/>), 0..1; a module may append further channels (Stride > 52), which are ignored.
    /// Frame 0 is at the first audio sample. Eye blink and gaze channels may be zero: the idle performance owns the eyes.
    /// </summary>
    public sealed class FaceCurves
    {
        public const int ArkitCount = 52;
        public readonly float[] Values;
        public readonly int Stride;
        public readonly float Fps;
        public int FrameCount => Values.Length / Stride;
        public float Duration => FrameCount / Fps;

        public FaceCurves(float[] values, int stride, float fps)
        {
            if (stride < ArkitCount) throw new ArgumentException($"A frame needs at least {ArkitCount} channels", nameof(stride));
            Values = values ?? Array.Empty<float>(); Stride = stride; Fps = fps;
        }

        public static readonly string[] LiveLinkNames =
        {
            "EyeBlinkLeft", "EyeLookDownLeft", "EyeLookInLeft", "EyeLookOutLeft", "EyeLookUpLeft", "EyeSquintLeft", "EyeWideLeft",
            "EyeBlinkRight", "EyeLookDownRight", "EyeLookInRight", "EyeLookOutRight", "EyeLookUpRight", "EyeSquintRight", "EyeWideRight",
            "JawForward", "JawRight", "JawLeft", "JawOpen", "MouthClose", "MouthFunnel", "MouthPucker", "MouthRight", "MouthLeft",
            "MouthSmileLeft", "MouthSmileRight", "MouthFrownLeft", "MouthFrownRight", "MouthDimpleLeft", "MouthDimpleRight",
            "MouthStretchLeft", "MouthStretchRight", "MouthRollLower", "MouthRollUpper", "MouthShrugLower", "MouthShrugUpper",
            "MouthPressLeft", "MouthPressRight", "MouthLowerDownLeft", "MouthLowerDownRight", "MouthUpperUpLeft", "MouthUpperUpRight",
            "BrowDownLeft", "BrowDownRight", "BrowInnerUp", "BrowOuterUpLeft", "BrowOuterUpRight",
            "CheekPuff", "CheekSquintLeft", "CheekSquintRight", "NoseSneerLeft", "NoseSneerRight", "TongueOut",
        };
    }

    /// <summary>Marks an adapter component: its id (for -avatarChat / -avatarSpeech / -avatarLipSync) and name (for the log).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AvatarModuleAttribute : Attribute
    {
        public readonly string Id, DisplayName;
        public AvatarModuleAttribute(string id, string displayName) { Id = id; DisplayName = displayName; }
    }

    /// <summary>Finds the installed adapters: every non-abstract [AvatarModule] component in the loaded assemblies.</summary>
    public static class AvatarModules
    {
        static List<Type> s_All;

        public static IReadOnlyList<Type> All
        {
            get
            {
                if (s_All != null) return s_All;
                var found = new List<Type>();
                foreach (var asm in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
                {
                    // Adapters reference this assembly; skipping the rest keeps the scan short.
                    if (asm != typeof(IAvatarModule).Assembly && !asm.GetReferencedAssemblies().Any(r => r.Name == typeof(IAvatarModule).Assembly.GetName().Name)) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var t in types)
                        if (!t.IsAbstract && typeof(IAvatarModule).IsAssignableFrom(t) && typeof(MonoBehaviour).IsAssignableFrom(t)
                            && t.GetCustomAttribute<AvatarModuleAttribute>() != null)
                            found.Add(t);
                }
                return s_All = found.OrderBy(t => t.FullName).ToList();
            }
        }

        public static AvatarModuleAttribute Info(Type t) => t?.GetCustomAttribute<AvatarModuleAttribute>();
        public static string Name(object module) => module == null ? "none" : Info(module.GetType())?.DisplayName ?? module.GetType().Name;

        public static IEnumerable<Type> Of<T>() where T : IAvatarModule => All.Where(t => typeof(T).IsAssignableFrom(t));

        /// <summary>The adapter component with this id; null (with a warning) when it is not installed.</summary>
        public static Type Find<T>(string id) where T : class, IAvatarModule
        {
            var type = Of<T>().FirstOrDefault(t => string.Equals(Info(t).Id, id, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                Debug.LogWarning($"No {typeof(T).Name} with id '{id}' is installed (installed: {string.Join(", ", Of<T>().Select(t => Info(t).Id))}).");
            return type;
        }

        /// <summary>A model package's weights folder: <paramref name="folder"/> inside the package in the editor,
        /// StreamingAssets/<paramref name="packageName"/> in a player.</summary>
        public static string ModelRoot(Type inPackage, string packageName, string folder = "Models~")
        {
#if UNITY_EDITOR
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(inPackage.Assembly);
            if (package != null) return Path.Combine(package.resolvedPath, folder);
#endif
            return Path.Combine(Application.streamingAssetsPath, packageName);
        }
    }
}
