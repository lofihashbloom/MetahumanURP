using System;
using System.Text;
using System.Threading;
using SentisModels;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Scripting;

namespace LookDev.Avatar
{
    /// <summary>Chat module: Gemma 3 270M IT (com.sky.sentis.gemma3-270m-it).</summary>
    [AvatarModule("gemma3", "Gemma 3 270M"), AddComponentMenu("Avatar Modules/Chat/Gemma 3 270M"), DisallowMultipleComponent, Preserve]
    public sealed class Gemma3Chat : MonoBehaviour, IChatModule
    {
        public BackendType backend = BackendType.GPUCompute;

        [NonSerialized] Gemma3Model m_Model;

        public async Awaitable LoadAsync(CancellationToken cancel)
        {
            m_Model = new Gemma3Model(backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : backend);
            m_Model.Load(AvatarModules.ModelRoot(typeof(Gemma3Model), Gemma3Model.PackageName));
            await m_Model.WarmupAsync();
        }

        public Awaitable<string> ReplyAsync(ChatRequest request, Action<string> onText, CancellationToken cancel) =>
            m_Model.GenerateFromPromptAsync(Prompt(request), request.MaxNewTokens, onText, cancel);

        // Gemma 3 has no system role: the system prompt leads the first user turn.
        public static string Prompt(ChatRequest request)
        {
            var sb = new StringBuilder();
            void User(string text)
            {
                bool first = sb.Length == 0;
                sb.Append("<start_of_turn>user\n");
                if (first && !string.IsNullOrEmpty(request.SystemPrompt)) sb.Append(request.SystemPrompt).Append("\n\n");
                sb.Append(text).Append("<end_of_turn>\n");
            }
            foreach (var turn in request.History) { User(turn.User); sb.Append("<start_of_turn>model\n").Append(turn.Reply).Append("<end_of_turn>\n"); }
            User(request.Message);
            return sb.Append("<start_of_turn>model\n").ToString();
        }

        public void Dispose() { m_Model?.Dispose(); m_Model = null; }
    }
}
