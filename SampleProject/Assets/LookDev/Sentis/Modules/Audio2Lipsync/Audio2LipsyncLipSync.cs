using System;
using System.Threading;
using Audio2Lipsync;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>
    /// Lipsync module: Audio2Lipsync (com.sky.sentis.audio2lipsync), HuBERT-Large + transformer decoder,
    /// 52 ARKit channels in Live Link order at 60 fps from 16 kHz audio.
    /// </summary>
    [AvatarModule("audio2lipsync", "Audio2Lipsync"), AddComponentMenu("Avatar Modules/Lip Sync/Audio2Lipsync"), DisallowMultipleComponent, Preserve]
    public sealed class Audio2LipsyncLipSync : MonoBehaviour, ILipSyncModule
    {
        public BackendType backend = BackendType.GPUCompute;
        public LipsyncSettings settings = LipsyncSettings.Default;

        [NonSerialized] Audio2LipsyncRunner m_Runner;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            m_Runner = await Audio2LipsyncRunner.CreateAsync(backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : backend);
            m_Runner.Warmup();
        }

        public async Awaitable<FaceCurves> AnimateAsync(SpeechAudio speech, CancellationToken cancel)
        {
            var audio = speech.Samples;
            if (speech.SampleRate != A2LConstants.AudioSampleRate)
            {
                await Awaitable.BackgroundThreadAsync();
                try { audio = AudioUtils.Resample(speech.Samples, speech.SampleRate, A2LConstants.AudioSampleRate); }
                finally { await Awaitable.MainThreadAsync(); }   // an exception must not leave the caller off the main thread
            }
            cancel.ThrowIfCancellationRequested();
            var frames = await m_Runner.GenerateAsync(audio, settings);
            return new FaceCurves(frames, A2LConstants.NumBlendshapes, A2LConstants.FaceFps);
        }

        public void Dispose() { m_Runner?.Dispose(); m_Runner = null; }
    }
}
