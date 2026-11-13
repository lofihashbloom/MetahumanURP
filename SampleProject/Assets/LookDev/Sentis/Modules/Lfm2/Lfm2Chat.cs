using System;
using System.Threading;
using SentisModels;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>
    /// Chat module: LFM2.5 350M (com.sky.sentis.lfm2.5-350m), Liquid AI's hybrid conv + attention model. Multilingual
    /// (English, Korean, Japanese, Chinese, European languages, Arabic), about twice as fast per token as Qwen3 0.6B.
    /// </summary>
    [AvatarModule("lfm2", "LFM2.5 350M"), AddComponentMenu("Avatar Modules/Chat/LFM2.5 350M"), DisallowMultipleComponent, Preserve]
    public sealed class Lfm2Chat : MonoBehaviour, IChatModule
    {
        public BackendType backend = BackendType.GPUCompute;

        [NonSerialized] Lfm2Model m_Model;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            m_Model = new Lfm2Model(backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : backend);
            m_Model.Load(AvatarModules.ModelRoot(typeof(Lfm2Model), Lfm2Model.PackageName));
            await m_Model.WarmupAsync();
        }

        public Awaitable<string> ReplyAsync(ChatRequest request, Action<string> onText, CancellationToken cancel) =>
            // LFM2 uses ChatML; Lfm2Model.Tokenize adds the leading <|startoftext|>.
            m_Model.GenerateFromPromptAsync(request.ToChatML(), request.MaxNewTokens, onText, cancel);

        public void Dispose() { m_Model?.Dispose(); m_Model = null; }
    }
}
