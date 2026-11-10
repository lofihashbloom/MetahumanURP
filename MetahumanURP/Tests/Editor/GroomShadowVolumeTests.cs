using System.Collections.Generic;
using MetaHuman.Groom;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Tests
{
    public sealed class GroomShadowVolumeTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var o in owned) if (o) Object.DestroyImmediate(o);
            owned.Clear();
        }

        T Own<T>(T o) where T : Object { owned.Add(o); return o; }

        // GroomRibbonBaker layout: two centre vertices per point, quads (a, a+2, a+1, a+1, a+2, a+3), diameter in UV1.x.
        Mesh Ribbon(IReadOnlyList<Vector3[]> strands, float diameter)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>(); var indices = new List<int>();
            foreach (var points in strands)
            {
                int start = vertices.Count;
                for (int p = 0; p < points.Length; p++)
                {
                    var tangent = (points[Mathf.Min(p + 1, points.Length - 1)] - points[Mathf.Max(p - 1, 0)]).normalized;
                    for (int side = 0; side < 2; side++)
                    {
                        vertices.Add(points[p]); normals.Add(tangent);
                        uv0.Add(new Vector2(side == 0 ? -1 : 1, p / (points.Length - 1f))); uv1.Add(new Vector2(diameter, .5f));
                    }
                    if (p == points.Length - 1) continue;
                    int a = start + p * 2;
                    indices.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            var mesh = Own(new Mesh { indexFormat = IndexFormat.UInt32 });
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1);
            mesh.SetTriangles(indices, 0);
            return mesh;
        }

        static Vector3[] Line(Vector3 from, Vector3 to, int points)
        {
            var result = new Vector3[points];
            for (int i = 0; i < points; i++) result[i] = Vector3.Lerp(from, to, i / (points - 1f));
            return result;
        }

        static double Area(GroomShadowVolume.DensityGrid grid)
        {
            double sum = 0;
            foreach (var v in grid.values) sum += v;
            return sum * grid.voxel * grid.voxel;
        }

        [Test] public void DensityDepositsTheMeanProjectedFibreAreaInTheTargetFrame()
        {
            var mesh = Ribbon(new[] { Line(new Vector3(-.1f, 0, 0), new Vector3(.1f, .05f, .02f), 17) }, .001f);
            float length = new Vector3(.2f, .05f, .02f).magnitude;
            double expected = Mathf.PI / 4 * .001 * length;
            var grid = GroomShadowVolume.BuildDensity(new[] { (mesh, Matrix4x4.identity) }, 32);
            Assert.AreEqual(16, grid.segments);
            Assert.AreEqual(32, Mathf.Max(grid.cells.x, Mathf.Max(grid.cells.y, grid.cells.z)));
            Assert.That(Area(grid), Is.EqualTo(expected).Within(1e-4 * expected));
            // A uniformly scaled target frame scales both length and diameter: area x4.
            var scaled = GroomShadowVolume.BuildDensity(new[] { (mesh, Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), Vector3.one * 2)) }, 32);
            Assert.That(Area(scaled), Is.EqualTo(4 * expected).Within(1e-4 * 4 * expected));
        }

        [Test] public void NonRibbonTrianglesAreIgnored()
        {
            var mesh = Own(new Mesh());
            mesh.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one });
            mesh.SetUVs(1, new[] { Vector2.one, Vector2.one, Vector2.one, Vector2.one });
            mesh.SetTriangles(new[] { 0, 1, 2, 2, 1, 3 }, 0);
            Assert.AreEqual(0, GroomShadowVolume.BuildDensity(new[] { (mesh, Matrix4x4.identity) }, 16).segments);
        }

        Light NewLight(string name, LightType type, Vector3 position, float intensity, float range = 10)
        {
            var light = Own(new GameObject(name)).AddComponent<Light>();
            light.type = type; light.transform.position = position; light.intensity = intensity; light.range = range;
            return light;
        }

        [Test] public void LightSelectionUsesExplicitLightsElseDirectionalThenIntensityOverDistanceSquared()
        {
            var sun = NewLight("Sun", LightType.Directional, Vector3.zero, .1f);
            var near = NewLight("Near", LightType.Spot, new Vector3(0, 0, 1), 1);
            var far = NewLight("Far", LightType.Point, new Vector3(0, 0, 3), 4);
            var outOfRange = NewLight("OutOfRange", LightType.Point, new Vector3(0, 0, 9), 100, 1);
            var off = NewLight("Off", LightType.Point, new Vector3(0, 0, 1), 100); off.enabled = false;
            var all = new[] { far, outOfRange, near, off, sun };
            CollectionAssert.AreEqual(new[] { sun, near, far }, GroomShadowVolume.SelectLights(null, all, Vector3.zero, .2f));
            CollectionAssert.AreEqual(new[] { far }, GroomShadowVolume.SelectLights(new[] { null, far, off }, all, Vector3.zero, .2f));
        }

        [Test] public void AutomaticLightsIncludeToolCreatedDontSaveLights()
        {
            var head = Own(new GameObject("Head"));
            var strandObject = new GameObject("Strands"); strandObject.transform.SetParent(head.transform, false);
            strandObject.AddComponent<MeshFilter>().sharedMesh = Ribbon(new[] { Line(Vector3.zero, new Vector3(0, .1f, 0), 8) }, .001f);
            strandObject.AddComponent<MeshRenderer>().sharedMaterial = Own(new Material(Shader.Find("MetaHuman/URP/GroomStrands")));
            // LookDev creates its rig lights like this; FindObjectsByType does not return them.
            var rig = NewLight("Rig", LightType.Point, new Vector3(0, .05f, .5f), 1000);
            rig.gameObject.hideFlags = HideFlags.DontSave;
            var volume = head.AddComponent<GroomShadowVolume>();
            volume.Prepare(new List<GroomShadowVolume.Dispatch>());
            var slots = new List<Light>();
            for (int s = 0; s < GroomShadowVolume.SlotCount; s++) if (volume.GetSlotLight(s)) slots.Add(volume.GetSlotLight(s));
            CollectionAssert.Contains(slots, rig);
        }

        [Test] public void AdditionalLightIdsFollowBufferOrderSkippingTheMainLight()
        {
            var l = new[] { NewLight("A", LightType.Point, Vector3.zero, 1), NewLight("B", LightType.Point, Vector3.zero, 1), NewLight("C", LightType.Point, Vector3.zero, 1) };
            var ids = GroomShadowFeature.MapAdditionalLightIds(l, 1, 256, out float main);
            Assert.AreEqual(GroomShadowVolume.LightId(l[1]), main);
            CollectionAssert.AreEqual(new float[] { GroomShadowVolume.LightId(l[0]), GroomShadowVolume.LightId(l[2]) }, ids);
            Assert.AreNotEqual(GroomShadowVolume.LightId(l[0]), GroomShadowVolume.LightId(l[2]));
            ids = GroomShadowFeature.MapAdditionalLightIds(l, -1, 2, out main);
            Assert.AreEqual(0, main);
            Assert.AreEqual(2, ids.Length);
        }

        [Test] public void ShadersCompileWithoutErrors()
        {
            var strands = Shader.Find("MetaHuman/URP/GroomStrands");
            Assert.IsNotNull(strands);
            Assert.IsFalse(ShaderUtil.ShaderHasError(strands), "GroomStrands has compile errors");
            var compute = Resources.Load<ComputeShader>("MetaHumanGroomShadowTransmittance");
            Assert.IsNotNull(compute);
            Assert.IsTrue(compute.HasKernel("March"));
            foreach (var message in ShaderUtil.GetComputeShaderMessages(compute))
                Assert.AreNotEqual("Error", message.severity.ToString(), message.message);
        }

        static float[] ReadTau(RenderTexture target)
        {
            var request = AsyncGPUReadback.Request(target, 0, 0, target.width, 0, target.height, 0, target.volumeDepth, TextureFormat.RHalf, null);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "Transmittance readback failed");
            var result = new float[target.width * target.height * target.volumeDepth];
            for (int z = 0; z < target.volumeDepth; z++)
            {
                var slice = request.GetData<ushort>(z);
                for (int i = 0; i < slice.Length; i++) result[z * target.width * target.height + i] = Mathf.HalfToFloat(slice[i]);
            }
            return result;
        }

        [Test] public void TransmittanceMarchIntegratesASlabTowardTheLight()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DRenderTextures) Assert.Ignore("Needs compute shaders and 3D render textures.");
            const int n = 16;
            const float perVoxel = .25f; // sigma * voxel
            var values = new ushort[n * n * n];
            for (int z = 8; z < 12; z++) for (int i = 0; i < n * n; i++) values[z * n * n + i] = Mathf.FloatToHalf(perVoxel);
            var density = Own(new Texture3D(n, n, n, TextureFormat.RHalf, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            density.SetPixelData(values, 0); density.Apply(false, false);
            var target = Own(new RenderTexture(n, n, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
                { dimension = TextureDimension.Tex3D, volumeDepth = n, enableRandomWrite = true });
            target.Create();
            var compute = Resources.Load<ComputeShader>("MetaHumanGroomShadowTransmittance");
            float voxel = .01f;
            float[] Run(Vector4 light, float scale)
            {
                using var cmd = new CommandBuffer();
                GroomShadowFeature.Encode(cmd, compute, compute.FindKernel("March"), new GroomShadowVolume.Dispatch
                {
                    density = density, target = target,
                    resolution = new Vector4(n, n, n, .5f), size = new Vector4(n * voxel, n * voxel, n * voxel, voxel),
                    light = light, march = new Vector4(1, scale, 512, 0),
                });
                Graphics.ExecuteCommandBuffer(cmd);
                return ReadTau(target);
            }
            int At(int x, int y, int z) => (z * n + y) * n + x;
            // Directional light toward +z: below the slab the whole slab (4 voxels) is in the way, above it nothing.
            var tau = Run(new Vector4(0, 0, 1, 0), 1);
            Assert.That(tau[At(8, 8, 2)], Is.EqualTo(4 * perVoxel).Within(.05f));
            Assert.That(tau[At(8, 8, 14)], Is.EqualTo(0).Within(1e-3f));
            Assert.That(tau[At(8, 8, 9)], Is.GreaterThan(0).And.LessThan(4 * perVoxel));
            // Extinction scale multiplies the optical depth.
            tau = Run(new Vector4(0, 0, 1, 0), .5f);
            Assert.That(tau[At(8, 8, 2)], Is.EqualTo(2 * perVoxel).Within(.03f));
            // A point light inside the volume, below the slab, stops the march at the light: nothing in between.
            tau = Run(new Vector4(8.5f * voxel, 8.5f * voxel, 6.5f * voxel, 1), 1);
            Assert.That(tau[At(8, 8, 2)], Is.EqualTo(0).Within(1e-3f));
            Assert.That(tau[At(8, 8, 14)], Is.EqualTo(4 * perVoxel).Within(.05f));
        }

        [Test] public void FullMarchMaskIsTheFibreVoxelsDilatedByTheMargin()
        {
            var grid = new GroomShadowVolume.DensityGrid { cells = new Vector3Int(9, 10, 11), values = new float[9 * 10 * 11], voxel = .01f };
            grid.values[(5 * 10 + 4) * 9 + 4] = 1;      // centre
            grid.values[(0 * 10 + 0) * 9 + 8] = .001f;  // a corner: the dilation stops at the grid
            int Count(int margin) { int n = 0; foreach (byte m in GroomShadowVolume.BuildFullMarchMask(grid, margin)) n += m; return n; }
            Assert.AreEqual(27 + 8, Count(1));
            Assert.AreEqual(125 + 27, Count(2));
            var mask = GroomShadowVolume.BuildFullMarchMask(grid, 1);
            Assert.AreEqual(1, mask[(6 * 10 + 5) * 9 + 5]);
            Assert.AreEqual(0, mask[(7 * 10 + 4) * 9 + 4]);
        }

        [Test] public void CoarseDensityIsTheBlockMeanAndKeepsTheTotal()
        {
            var grid = new GroomShadowVolume.DensityGrid { cells = new Vector3Int(6, 4, 9), values = new float[6 * 4 * 9], voxel = .01f };
            double total = 0;
            for (int i = 0; i < grid.values.Length; i++) { grid.values[i] = (i * 37 % 11) * .1f; total += grid.values[i]; }
            var coarse = GroomShadowVolume.BuildCoarse(grid, out var cells);
            Assert.AreEqual(new Vector3Int(2, 1, 3), cells);
            Assert.AreEqual(6, coarse.Length);
            double sum = 0; foreach (var v in coarse) sum += v;
            int block = GroomShadowVolume.CoarseFactor * GroomShadowVolume.CoarseFactor * GroomShadowVolume.CoarseFactor;
            Assert.That(sum * block, Is.EqualTo(total).Within(1e-3));
            // Cell (1, 0, 2): voxels x 4-5, y 0-3, z 8 (the rest of the block lies past the grid and counts as empty).
            double cell = 0;
            for (int y = 0; y < 4; y++) for (int x = 4; x < 6; x++) cell += grid.values[(8 * 4 + y) * 6 + x];
            Assert.That(coarse[(2 * 1 + 0) * 2 + 1] * block, Is.EqualTo(cell).Within(1e-4));
        }

        [Test] public void LightToleranceIsAnAngleSeenFromTheVolume()
        {
            var size = new Vector3(.2f, .2f, .2f);
            Vector4 Direction(float degrees) => Quaternion.Euler(0, degrees, 0) * Vector3.forward;
            Assert.IsFalse(GroomShadowVolume.LightMoved(Direction(0), Direction(0), size, 0));
            Assert.IsTrue(GroomShadowVolume.LightMoved(Direction(.05f), Direction(0), size, 0), "No tolerance: any motion");
            Assert.IsFalse(GroomShadowVolume.LightMoved(Direction(.05f), Direction(0), size, .1f));
            Assert.IsTrue(GroomShadowVolume.LightMoved(Direction(.2f), Direction(0), size, .1f));
            // A point light 2 m from the volume's bounding sphere (radius 0.173 m): 0.1 degrees is 3.5 mm there.
            Vector4 Point(float x, float z) => new Vector4(.1f + x, .1f, .1f + z, 1);
            float far = 2 + size.magnitude * .5f;
            Assert.IsFalse(GroomShadowVolume.LightMoved(Point(.002f, far), Point(0, far), size, .1f));
            Assert.IsTrue(GroomShadowVolume.LightMoved(Point(.005f, far), Point(0, far), size, .1f));
            Assert.IsTrue(GroomShadowVolume.LightMoved(Point(.002f, far), Point(0, far), size, 0));
            // Inside the bounding sphere some voxel is arbitrarily close: any motion counts.
            Assert.IsTrue(GroomShadowVolume.LightMoved(Point(.0001f, .05f), Point(0, .05f), size, 1));
            // A light that changed type is another light.
            Assert.IsTrue(GroomShadowVolume.LightMoved(new Vector4(0, 0, 1, 1), new Vector4(0, 0, 1, 0), size, 1));
        }

        [Test] public void EmptySpaceMarchIsCoarseAndVoxelsNearFibresKeepTheFullMarch()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DRenderTextures) Assert.Ignore("Needs compute shaders and 3D render textures.");
            const int n = 16;
            const float perVoxel = .25f, voxel = .01f;
            var grid = new GroomShadowVolume.DensityGrid { cells = new Vector3Int(n, n, n), values = new float[n * n * n], voxel = voxel };
            // A slab with a hole, so the optical depth varies across the volume.
            for (int z = 8; z < 12; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                if (x < 5 || x > 6) grid.values[(z * n + y) * n + x] = perVoxel;
            Texture3D Half(float[] values, Vector3Int cells)
            {
                var half = new ushort[values.Length];
                for (int i = 0; i < half.Length; i++) half[i] = Mathf.FloatToHalf(values[i]);
                var texture = Own(new Texture3D(cells.x, cells.y, cells.z, TextureFormat.RHalf, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
                texture.SetPixelData(half, 0); texture.Apply(false, false);
                return texture;
            }
            var density = Half(grid.values, grid.cells);
            var coarse = Half(GroomShadowVolume.BuildCoarse(grid, out var coarseCells), coarseCells);
            // Full march for x < 8 only.
            var maskValues = new byte[n * n * n];
            for (int i = 0; i < maskValues.Length; i++) maskValues[i] = i % n < 8 ? (byte)1 : (byte)0;
            var mask = Own(new Texture3D(n, n, n, TextureFormat.R8, false) { filterMode = FilterMode.Point });
            mask.SetPixelData(maskValues, 0); mask.Apply(false, false);
            var target = Own(new RenderTexture(n, n, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
                { dimension = TextureDimension.Tex3D, volumeDepth = n, enableRandomWrite = true });
            target.Create();
            var compute = Resources.Load<ComputeShader>("MetaHumanGroomShadowTransmittance");
            float[] Run(bool useCoarse)
            {
                using var cmd = new CommandBuffer();
                float cell = GroomShadowVolume.CoarseFactor * voxel;
                GroomShadowFeature.Encode(cmd, compute, compute.FindKernel("March"), new GroomShadowVolume.Dispatch
                {
                    density = density, target = target, coarse = coarse, fullMarch = mask,
                    coarseScale = new Vector4(1 / (coarseCells.x * cell), 1 / (coarseCells.y * cell), 1 / (coarseCells.z * cell), useCoarse ? GroomShadowVolume.CoarseFactor : 0),
                    resolution = new Vector4(n, n, n, 1), size = new Vector4(n * voxel, n * voxel, n * voxel, voxel),
                    light = new Vector4(0, 0, 1, 0), march = new Vector4(1, 1, 512, 1),
                });
                Graphics.ExecuteCommandBuffer(cmd);
                return ReadTau(target);
            }
            var full = Run(false); var mixed = Run(true);
            int At(int x, int y, int z) => (z * n + y) * n + x;
            for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < 8; x++)
                Assert.AreEqual(full[At(x, y, z)], mixed[At(x, y, z)], $"Marked voxel {x},{y},{z} keeps the full march");
            // Unmarked voxels: the same optical depth through the slab from the coarse copy, exactly none above it.
            Assert.That(full[At(12, 8, 2)], Is.EqualTo(4 * perVoxel).Within(.05f));
            Assert.That(mixed[At(12, 8, 2)], Is.EqualTo(4 * perVoxel).Within(.1f));
            Assert.That(mixed[At(12, 8, 14)], Is.EqualTo(0).Within(1e-3f));
            bool differs = false;
            for (int i = 0; i < full.Length && !differs; i++) differs = full[i] != mixed[i];
            Assert.IsTrue(differs, "The coarse march ran");
        }

        [Test] public void SmallLightMotionWithinTheToleranceDoesNotRedispatch()
        {
            var head = Own(new GameObject("Head"));
            var strandObject = new GameObject("Strands"); strandObject.transform.SetParent(head.transform, false);
            var strands = new List<Vector3[]>();
            for (int i = 0; i < 20; i++) strands.Add(Line(new Vector3(i * .005f, 0, 0), new Vector3(i * .005f, .1f, 0), 8));
            strands.Add(Line(new Vector3(0, 0, .1f), new Vector3(0, .1f, .1f), 8));   // far strand: most of the volume is empty
            strandObject.AddComponent<MeshFilter>().sharedMesh = Ribbon(strands, .0008f);
            strandObject.AddComponent<MeshRenderer>().sharedMaterial = Own(new Material(Shader.Find("MetaHuman/URP/GroomStrands")));
            var sun = NewLight("Sun", LightType.Directional, Vector3.zero, 1);
            sun.transform.rotation = Quaternion.Euler(0, 30, 0);   // horizontal: a head turn about Y turns it by the same angle
            var volume = head.AddComponent<GroomShadowVolume>();
            volume.lights = new[] { sun };
            Assert.AreEqual(GroomShadowVolume.DefaultLightTolerance, volume.lightTolerance);
            Assert.IsTrue(volume.coarseEmptySpace);
            Assert.That(volume.FullMarchFraction, Is.GreaterThan(0f).And.LessThan(1f));
            StringAssert.Contains("% near fibres", volume.Status);

            var dispatches = new List<GroomShadowVolume.Dispatch>();
            volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);
            Assert.IsNotNull(dispatches[0].coarse); Assert.IsNotNull(dispatches[0].fullMarch);
            Assert.AreEqual(GroomShadowVolume.CoarseFactor, dispatches[0].coarseScale.w);
            // Three turns of 0.04 degrees: the third leaves the tolerance, measured from the pose that was marched.
            int marches = 0;
            for (int step = 1; step <= 3; step++)
            {
                head.transform.rotation = Quaternion.Euler(0, .04f * step, 0);
                dispatches.Clear(); volume.Prepare(dispatches);
                marches += dispatches.Count;
                Assert.AreEqual(step == 3 ? 1 : 0, dispatches.Count, "step " + step);
            }
            Assert.AreEqual(1, marches);
            // Without tolerance the same small turn re-marches.
            volume.lightTolerance = 0;
            head.transform.rotation = Quaternion.Euler(0, .16f, 0);
            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);
            // Switching the empty-space march re-marches, with the full march everywhere.
            volume.coarseEmptySpace = false;
            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);
            Assert.AreEqual(0, dispatches[0].coarseScale.w);
            // A new margin rebuilds the mask.
            float fraction = volume.FullMarchFraction;
            volume.fullMarchMargin = 4;
            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);
            Assert.That(volume.FullMarchFraction, Is.GreaterThan(fraction));
        }

        [Test] public void VolumeBindsRenderersAndRedispatchesOnlyWhenALightMovesRelativeToIt()
        {
            var head = Own(new GameObject("Head"));
            var strandObject = new GameObject("Strands"); strandObject.transform.SetParent(head.transform, false);
            var strands = new List<Vector3[]>();
            for (int i = 0; i < 20; i++) strands.Add(Line(new Vector3(i * .005f, 0, 0), new Vector3(i * .005f, .1f, 0), 8));
            strandObject.AddComponent<MeshFilter>().sharedMesh = Ribbon(strands, .0008f);
            var renderer = strandObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Own(new Material(Shader.Find("MetaHuman/URP/GroomStrands")));
            var sun = NewLight("Sun", LightType.Directional, Vector3.zero, 1);
            sun.transform.rotation = Quaternion.Euler(50, 30, 0);
            var volume = head.AddComponent<GroomShadowVolume>();
            volume.lights = new[] { sun };
            StringAssert.Contains("140 segments", volume.Status);
            CollectionAssert.AreEqual(new Renderer[] { renderer }, volume.renderers);

            var dispatches = new List<GroomShadowVolume.Dispatch>();
            volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);
            Assert.AreEqual(sun, volume.GetSlotLight(0));
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            Assert.AreEqual(1, block.GetFloat("_MHGroomShadowEnabled"));
            Assert.AreEqual(GroomShadowVolume.LightId(sun), block.GetVector("_MHGroomShadowSlotIds").x);
            Assert.AreEqual(volume.GetTransmittance(0), block.GetTexture("_MHGroomShadowTau0"));

            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(0, dispatches.Count, "Nothing moved: no new march");
            // Translating the head does not change a directional light in the head frame.
            head.transform.position += new Vector3(1, 2, 3);
            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(0, dispatches.Count);
            renderer.GetPropertyBlock(block, 0);
            var q = block.GetMatrix("_MHGroomShadowWorldToVolume").MultiplyPoint3x4(strandObject.transform.TransformPoint(new Vector3(.05f, .05f, 0)));
            Assert.That(q.x, Is.InRange(0f, 1f)); Assert.That(q.y, Is.InRange(0f, 1f)); Assert.That(q.z, Is.InRange(0f, 1f));
            // Turning the head does.
            head.transform.rotation = Quaternion.Euler(0, 40, 0);
            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);
            // So does a parameter change.
            volume.density = .5f;
            dispatches.Clear(); volume.Prepare(dispatches);
            Assert.AreEqual(1, dispatches.Count);

            volume.enabled = false;
            renderer.GetPropertyBlock(block, 0);
            Assert.AreEqual(0, block.GetFloat("_MHGroomShadowEnabled"));
            // The density survives disable so re-enabling does not rebuild; destroying the component frees it.
            var density = volume.Density;
            Assert.IsNotNull(density);
            volume.enabled = true;
            Assert.AreSame(density, volume.Density);
            Object.DestroyImmediate(volume);
            Assert.IsFalse(density);
        }
    }
}
