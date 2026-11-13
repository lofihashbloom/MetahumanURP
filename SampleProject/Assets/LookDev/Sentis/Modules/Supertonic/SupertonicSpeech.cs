using System;
using System.Text.RegularExpressions;
using System.Threading;
using SentisModels;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>Speech module: Supertonic TTS (com.sky.sentis.supertonic). Korean and Japanese are detected from the text.</summary>
    [AvatarModule("supertonic", "Supertonic"), AddComponentMenu("Avatar Modules/Speech/Supertonic"), DisallowMultipleComponent, Preserve]
    public sealed class SupertonicSpeech : MonoBehaviour, ISpeechModule
    {
        public BackendType backend = BackendType.GPUCompute;
        [Tooltip("F1-F5, M1-M5.")] public string voiceStyle = "M5";
        [Range(2, 16)] public int steps = 6;
        [Range(.7f, 1.5f)] public float speed = 1.05f;

        [NonSerialized] SupertonicTts m_Tts;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            m_Tts = new SupertonicTts(backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : backend, steps, speed);
            m_Tts.Load(AvatarModules.ModelRoot(typeof(SupertonicTts), "com.sky.sentis.supertonic"));
            await m_Tts.Synthesize("Hi.", SupertonicLanguage.en, voiceStyle);
        }

        public async Awaitable<SpeechAudio> SynthesizeAsync(string text, CancellationToken cancel)
        {
            var pcm = await m_Tts.Synthesize(ForSpeech(text), DetectLanguage(text), voiceStyle);
            return new SpeechAudio(pcm, m_Tts.SampleRate);
        }

        // Acronyms and product names the voice would otherwise read as words.
        static readonly Regex Spelled = new Regex(@"\b(URP|HDRP|GPU|CPU|AI)\b", RegexOptions.Compiled);

        // A final "..." instead of "." makes the voice trail off rather than clip the last syllable.
        static string ForSpeech(string s)
        {
            s = Spelled.Replace(s, m => string.Join(" ", m.Value.ToCharArray())).Replace("MetaHuman", "Meta Human").TrimEnd();
            return s.EndsWith(".") && !s.EndsWith("...") ? s.TrimEnd('.') + "..." : s;
        }

        public static SupertonicLanguage DetectLanguage(string s)
        {
            int hangul = 0, kana = 0;
            foreach (char c in s)
            {
                if (c >= 0xAC00 && c <= 0xD7A3 || c >= 0x1100 && c <= 0x11FF || c >= 0x3130 && c <= 0x318F) hangul++;
                else if (c >= 0x3040 && c <= 0x30FF) kana++;
            }
            if (hangul > 0 && hangul >= kana) return SupertonicLanguage.ko;
            return kana > 0 ? SupertonicLanguage.ja : SupertonicLanguage.en;
        }

        public void Dispose() { m_Tts?.Dispose(); m_Tts = null; }
    }
}
