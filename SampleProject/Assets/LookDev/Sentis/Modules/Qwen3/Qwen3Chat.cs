using System;
using System.Threading;
using SentisModels;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>
    /// Chat module: Qwen3 0.6B (com.sky.sentis.qwen3-0.6b). Multilingual (119 languages), slower than Gemma 3 270M.
    /// Thinking mode is off by default; with <see cref="think"/> the model reasons first and only the answer is returned.
    /// </summary>
    [AvatarModule("qwen3", "Qwen3 0.6B"), AddComponentMenu("Avatar Modules/Chat/Qwen3 0.6B"), DisallowMultipleComponent, Preserve]
    public sealed class Qwen3Chat : MonoBehaviour, IChatModule
    {
        public BackendType backend = BackendType.GPUCompute;

        [Tooltip("Qwen3 thinking mode: reason before answering. Slower; the reasoning is not shown or spoken.")]
        public bool think;

        [Tooltip("Extra tokens allowed for the reasoning when thinking is on, on top of the reply budget.")]
        public int thinkingTokens = 384;

        [NonSerialized] Qwen3Model m_Model;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            m_Model = new Qwen3Model(backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : backend);
            m_Model.Load(AvatarModules.ModelRoot(typeof(Qwen3Model), Qwen3Model.PackageName));
            await m_Model.WarmupAsync();
        }

        public Awaitable<string> ReplyAsync(ChatRequest request, Action<string> onText, CancellationToken cancel) =>
            m_Model.GenerateFromPromptAsync(Prompt(request, think), request.MaxNewTokens + (think ? thinkingTokens : 0), onText, cancel, thinking: think);

        /// <summary>Qwen3's ChatML template. Without <paramref name="think"/> the turn opens with an empty think block
        /// (the official non-thinking mode); earlier turns carry no reasoning, as the official template drops it.</summary>
        public static string Prompt(ChatRequest request, bool think) => request.ToChatML() + (think ? "" : "<think>\n\n</think>\n\n");

        public void Dispose() { m_Model?.Dispose(); m_Model = null; }
    }
}
