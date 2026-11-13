using System;
using System.Threading;
using NeuroSync;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>Lipsync module: NeuroSync (com.sky.sentis.neurosync), 68 channels at 60 fps (61 Live Link + 7 emotions).</summary>
    [AvatarModule("neurosync", "NeuroSync"), AddComponentMenu("Avatar Modules/Lip Sync/NeuroSync"), DisallowMultipleComponent, Preserve]
    public sealed class NeuroSyncLipSync : MonoBehaviour, ILipSyncModule
    {
        public BackendType featureBackend = BackendType.GPUCompute;
        public BackendType decoderBackend = BackendType.GPUCompute;

        [NonSerialized] NeuroSyncRunner m_Runner;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            m_Runner = await NeuroSyncRunner.CreateAsync(Usable(featureBackend), Usable(decoderBackend));
            m_Runner.Warmup();
        }

        public async Awaitable<FaceCurves> AnimateAsync(SpeechAudio speech, CancellationToken cancel)
        {
            float[] audio;
            await Awaitable.BackgroundThreadAsync();
            try { audio = AudioUtils.Prepare(speech.Samples, speech.SampleRate); }
            finally { await Awaitable.MainThreadAsync(); }   // an exception must not leave the caller off the main thread
            cancel.ThrowIfCancellationRequested();
            var curves = await m_Runner.GenerateAsync(audio);
            return new FaceCurves(curves, NeuroSyncConstants.NumOutputs, NeuroSyncConstants.FaceFps);
        }

        static BackendType Usable(BackendType b) => b == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : b;

        public void Dispose() { m_Runner?.Dispose(); m_Runner = null; }
    }
}
