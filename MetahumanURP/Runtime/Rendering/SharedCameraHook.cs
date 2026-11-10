using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Rendering
{
    /// <summary>
    /// One static camera-start handler shared by every instance of a renderer feature, subscribed while at least one
    /// instance holds it. Unsubscribing a static method directly from one instance's Dispose would remove it for all of
    /// them, leaving their per-camera global flags stuck from the last camera that set them.
    /// </summary>
    internal sealed class SharedCameraHook
    {
        readonly Action<ScriptableRenderContext, Camera> handler;
        int users;

        public SharedCameraHook(Action<ScriptableRenderContext, Camera> handler) => this.handler = handler;

        /// <summary>Takes a reference for one feature instance (idempotent per instance through <paramref name="held"/>).</summary>
        public void Acquire(ref bool held)
        {
            if (held) return;
            held = true;
            if (users++ == 0) RenderPipelineManager.beginCameraRendering += handler;
        }

        /// <summary>Releases this instance's reference; the handler is removed with the last one.</summary>
        public void Release(ref bool held)
        {
            if (!held) return;
            held = false;
            if (--users <= 0) { users = 0; RenderPipelineManager.beginCameraRendering -= handler; }
        }
    }
}
