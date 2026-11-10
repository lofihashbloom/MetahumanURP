using MetaHuman.Groom;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace MetaHuman.Tests
{
    public sealed class GroomTemporalTests
    {
        [Test] public void ExplicitGeometryInvalidationOnlyResetsTemporalCameras()
        {
            var go=new GameObject("Groom history invalidation test");
            try{
                var camera=go.AddComponent<Camera>();var additional=camera.GetUniversalAdditionalCameraData();
                var history=go.AddComponent<GroomTemporalHistory>();
                additional.antialiasing=AntialiasingMode.None;additional.resetHistory=false;
                history.InvalidateHistory();Assert.That(additional.resetHistory,Is.False);Assert.That(history.ResetCount,Is.Zero);
                additional.antialiasing=AntialiasingMode.TemporalAntiAliasing;
                history.InvalidateHistory();Assert.That(additional.resetHistory,Is.True);Assert.That(history.ResetCount,Is.EqualTo(1));
            }finally{Object.DestroyImmediate(go);}
        }
    }
}
