using System;
using System.Threading;
using Audio2Face;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>
    /// Lipsync module: NVIDIA Audio2Face-3D v3.0 (com.sky.sentis.audio2face), ARKit-52 at 60 fps. The package outputs the
    /// SDK pose order; this adapter reorders to Live Link (the FaceCurves contract). Blinks are dropped by the mapping:
    /// the idle performance keeps the eyes.
    /// </summary>
    [AvatarModule("audio2face", "Audio2Face-3D"), AddComponentMenu("Avatar Modules/Lip Sync/Audio2Face-3D"), DisallowMultipleComponent, Preserve]
    public sealed class Audio2FaceLipSync : MonoBehaviour, ILipSyncModule
    {
        [Tooltip("Claire has the most neutral resting mouth; James and Mark rest with a slight lip roll / chin raise.")]
        public A2FOptions options = new A2FOptions();

        [NonSerialized] A2FStream m_Stream;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            // A copy, so the CPU fallback does not overwrite the inspector setting.
            var used = JsonUtility.FromJson<A2FOptions>(JsonUtility.ToJson(options));
            if (used.backend == Unity.InferenceEngine.BackendType.GPUCompute && !SystemInfo.supportsComputeShaders)
                used.backend = Unity.InferenceEngine.BackendType.CPU;
            m_Stream = await A2FStream.CreateAsync(used);   // warms up once
        }

        public async Awaitable<FaceCurves> AnimateAsync(SpeechAudio speech, CancellationToken cancel)
        {
            float[][] frames;
            try { frames = await A2FGenerator.GenerateAsync(m_Stream, speech.Samples, speech.SampleRate); }
            catch
            {
                // The generator resamples on a background thread: an exception there must not leave the caller off the main thread.
                await Awaitable.MainThreadAsync();
                throw;
            }
            cancel.ThrowIfCancellationRequested();
            return new FaceCurves(ToLiveLink(frames), FaceCurves.ArkitCount, A2FConstants.Fps);
        }

        static float[] ToLiveLink(float[][] frames)
        {
            int n = A2FConstants.ArkitCount;
            var values = new float[frames.Length * n];
            for (int f = 0; f < frames.Length; f++) A2FConstants.ToLiveLinkOrder(frames[f], new Span<float>(values, f * n, n));
            return values;
        }

        public void Dispose() { m_Stream?.Dispose(); m_Stream = null; }
    }
}
