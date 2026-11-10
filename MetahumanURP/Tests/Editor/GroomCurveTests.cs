using System;
using System.IO;
using System.Text;
using MetaHuman.Editor.Groom;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MetaHuman.Tests
{
    public sealed class GroomCurveTests
    {
        [Test] public void TerminalTangentPointAffectsDirectionsButProducesNoVisibleSegment()
        {
            var source = new GroomCurveData {
                strands = new[] { new GroomCurveData.Strand { first = 0, count = 3 } },
                positions = new[] { Vector3.zero, Vector3.up, Vector3.up + Vector3.forward },
                widths = new[] { .01f, .01f, .01f }
            };
            var groups = new[] { new GroomRibbonBaker.GroupSettings { widthCentimetres = .01f, rootScale = 1, tipScale = .5f, terminalTangentPoint = true, sourceMaximumWidthCentimetres = .02f } };
            var built = GroomRibbonBaker.Build(source, Matrix4x4.identity, groups, 1, 64, true, true, true,
                sourcePointCoordinates: new[] { 0f, .8f, 1f });
            try
            {
                var mesh = built.meshes[0];
                Assert.That(mesh.vertexCount, Is.EqualTo(6));
                Assert.That(mesh.triangles.Length, Is.EqualTo(6), "Only the first segment is visible.");
                Assert.That(mesh.triangles, Has.All.LessThan(4), "The terminal helper vertices stay unreferenced.");
                Assert.That(Vector3.Distance(mesh.normals[2], (Vector3.up + Vector3.forward).normalized), Is.LessThan(1e-6));
                var uv = new System.Collections.Generic.List<Vector2>(); var widths = new System.Collections.Generic.List<Vector2>(); mesh.GetUVs(0,uv); mesh.GetUVs(1,widths);
                Assert.That(uv[2].y, Is.EqualTo(.8f));
                Assert.That(widths[2].x, Is.EqualTo(.00003f).Within(1e-8), "Source U drives taper; explicit original group maximum drives physical normalization.");
            }
            finally { foreach (var mesh in built.meshes) Object.DestroyImmediate(mesh); }
            Assert.Throws<ArgumentException>(() => GroomRibbonBaker.Build(source, Matrix4x4.identity, groups, sourcePointCoordinates: new[] { 0f, float.NaN, 1f }));
        }
        [Test] public void ExplicitSourceSelectionKeepsOnlyMappedCurvesAndTheirSeeds()
        {
            var source = new GroomCurveData {
                strands = new[] { new GroomCurveData.Strand { first = 0, count = 2 }, new GroomCurveData.Strand { first = 2, count = 2 } },
                positions = new[] { Vector3.zero, Vector3.up, Vector3.right, Vector3.right + Vector3.up },
                widths = new[] { .01f, .01f, .02f, .02f }
            };
            var selected = new System.Collections.Generic.Dictionary<int,float> { [1] = 17f / 255f };
            var groups = new[] { new GroomRibbonBaker.GroupSettings { widthCentimetres = .02f, rootScale = 1, tipScale = 1 } };
            var result = GroomRibbonBaker.Build(source, Matrix4x4.identity, groups, 1, 64, true, true, true, selected);
            try
            {
                Assert.That(result.sourceCurveSelection, Is.True); Assert.That(result.renderStrands, Is.EqualTo(1));
                Assert.That(result.meshes[0].vertices[0], Is.EqualTo(Vector3.right));
                var widths = new System.Collections.Generic.List<Vector2>(); result.meshes[0].GetUVs(1,widths);
                Assert.That(widths[0].y, Is.EqualTo(17f / 255f));
                Assert.That(result.groupMaximumSourceWidths[0], Is.EqualTo(.02f), "Width normalization uses the source group, independently of selection.");
            }
            finally { foreach (var mesh in result.meshes) Object.DestroyImmediate(mesh); }
            selected[1] = float.NaN;
            Assert.Throws<ArgumentException>(() => GroomRibbonBaker.Build(source, Matrix4x4.identity, groups, sourceCurveSeeds:selected));
        }
        [Test] public void SourceMaterialFrameIsOrthogonalAndIgnoresUnequalSegmentLengths()
        {
            var source = new GroomCurveData {
                strands = new[] { new GroomCurveData.Strand { first = 0, count = 3 } },
                positions = new[] { Vector3.zero, Vector3.right, Vector3.right + Vector3.forward * 3 },
                widths = new[] { .01f, .01f, .01f }
            };
            var rotation = Quaternion.Euler(27, -42, 11);
            var transform = Matrix4x4.TRS(new Vector3(1, 2, 3), rotation, Vector3.one * .01f);
            var result = GroomRibbonBaker.Build(source, transform,
                new[] { new GroomRibbonBaker.GroupSettings { widthCentimetres = .01f, rootScale = 1, tipScale = 1 } },
                1, 64, true, true, true);
            try
            {
                var mesh = result.meshes[0];
                Assert.That(Vector3.Distance(mesh.normals[2], rotation * (Vector3.right + Vector3.forward).normalized), Is.LessThan(1e-5));
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    var x = (Vector3)mesh.tangents[i]; var z = mesh.normals[i];
                    Assert.That(x.magnitude, Is.EqualTo(1).Within(1e-5));
                    Assert.That(Mathf.Abs(Vector3.Dot(x, z)), Is.LessThan(1e-5));
                    Assert.That(Vector3.Cross(z, x).magnitude, Is.EqualTo(1).Within(1e-5));
                }
            }
            finally { foreach (var mesh in result.meshes) Object.DestroyImmediate(mesh); }
        }
        static MemoryStream Fixture(int first = 0, float point = 0)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("MHGRM001")); writer.Write(1); writer.Write(2);
                writer.Write(first); writer.Write(2); writer.Write(0); writer.Write(0);
                writer.Write(0.2f); writer.Write(0.3f); writer.Write(0f);
                for (int i = 0; i < 2; i++) { writer.Write(point); writer.Write((float)i); writer.Write(0f); writer.Write(0.01f); }
            }
            stream.Position = 0; return stream;
        }
        [Test] public void GroomInterchangeRejectsInvalidRangesNonfinitePointsAndTruncatedBuffers()
        {
            using var good = Fixture();
            var data = GroomCurveData.Read(good);
            Assert.That(data.positions[1], Is.EqualTo(Vector3.up));
            using var badRange = Fixture(1);
            Assert.Throws<InvalidDataException>(() => GroomCurveData.Read(badRange));
            using var badPoint = Fixture(0, float.NaN);
            Assert.Throws<InvalidDataException>(() => GroomCurveData.Read(badPoint));
            using var truncated = Fixture(); truncated.SetLength(truncated.Length - 1);
            Assert.Throws<InvalidDataException>(() => GroomCurveData.Read(truncated));
        }
        [Test] public void RibbonResamplingKeepsEndpointsExcludesGuidesAndPreservesPhysicalDiameter()
        {
            var source = new GroomCurveData { strands = new GroomCurveData.Strand[2], positions = new Vector3[30], widths = new float[30] };
            source.strands[0] = new GroomCurveData.Strand { first = 0, count = 15 };
            source.strands[1] = new GroomCurveData.Strand { first = 15, count = 15, guide = true };
            for (int i = 0; i < 30; i++) source.positions[i] = new Vector3(0, i, i * i * 0.01f);
            var settings = new[] { new GroomRibbonBaker.GroupSettings { widthCentimetres = 0.02f, rootScale = 1, tipScale = 0.5f } };
            var transform = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(0, 90, 0), Vector3.one * 0.01f);
            var built = GroomRibbonBaker.Build(source, transform, settings, 1, 6);
            try
            {
                Assert.That(built.renderStrands, Is.EqualTo(1)); Assert.That(built.guidesSkipped, Is.EqualTo(1));
                var mesh = built.meshes[0]; var points = mesh.vertices; var widths = new System.Collections.Generic.List<Vector2>(); mesh.GetUVs(1, widths);
                Assert.That(mesh.vertexCount, Is.EqualTo(12)); Assert.That(mesh.triangles.Length, Is.EqualTo(30));
                Assert.That(Vector3.Distance(points[0], transform.MultiplyPoint3x4(source.positions[0])), Is.LessThan(1e-6));
                Assert.That(Vector3.Distance(points[10], transform.MultiplyPoint3x4(source.positions[14])), Is.LessThan(1e-6));
                Assert.That(widths[0].x, Is.EqualTo(0.0002f).Within(1e-7)); Assert.That(widths[10].x, Is.EqualTo(0.0001f).Within(1e-7));
            }
            finally { foreach (var mesh in built.meshes) Object.DestroyImmediate(mesh); }
        }

        [Test] public void SourceWidthsPreserveZeroTipsAndArclengthWithoutGuideWidthContamination()
        {
            var source = new GroomCurveData {
                strands = new[] {
                    new GroomCurveData.Strand { first = 0, count = 3, width = .9f, rootUV = new Vector2(.2f,.3f) },
                    new GroomCurveData.Strand { first = 3, count = 2, guide = true } },
                positions = new[] { Vector3.zero, Vector3.up, Vector3.up * 4, Vector3.zero, Vector3.up },
                widths = new[] { .04f, .02f, 0, 1f, 1f }
            };
            var built = GroomRibbonBaker.Build(source, Matrix4x4.Scale(Vector3.one * .01f),
                new[] { new GroomRibbonBaker.GroupSettings { widthCentimetres = .02f, rootScale = 1, tipScale = .5f } }, 1, 64, true, true);
            try
            {
                var uv = new System.Collections.Generic.List<Vector2>(); var widths = new System.Collections.Generic.List<Vector2>(); var roots = new System.Collections.Generic.List<Vector2>();
                built.meshes[0].GetUVs(0, uv); built.meshes[0].GetUVs(1, widths); built.meshes[0].GetUVs(2, roots);
                Assert.That(built.meshes[0].vertexCount, Is.EqualTo(6));
                Assert.That(built.groupMaximumSourceWidths[0], Is.EqualTo(.04f));
                Assert.That(uv[2].y, Is.EqualTo(.25f));
                Assert.That(widths[0].x, Is.EqualTo(.0002f).Within(1e-8));
                Assert.That(widths[2].x, Is.EqualTo(.0000875f).Within(1e-8));
                Assert.That(widths[4].x, Is.Zero, "A zero-width point in a present vertex attribute must not fall back to a positive strand width.");
                Assert.That(roots[2], Is.EqualTo(new Vector2(.2f,.3f)));
            }
            finally { foreach (var mesh in built.meshes) Object.DestroyImmediate(mesh); }
        }
    }
}
