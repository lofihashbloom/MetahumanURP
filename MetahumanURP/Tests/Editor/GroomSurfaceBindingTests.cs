using MetaHuman.Groom;
using NUnit.Framework;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class GroomSurfaceBindingTests
    {
        [Test]
        public void ClosestPointOnTriangleReturnsBarycentrics()
        {
            var q = GroomSurfaceBinding.Closest(new Vector3(.25f, .25f, 1), Vector3.zero, Vector3.right, Vector3.up, out var bary);
            Assert.That(Vector3.Distance(q, new Vector3(.25f, .25f, 0)), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(bary, new Vector3(.5f, .25f, .25f)), Is.LessThan(1e-6f));
            q = GroomSurfaceBinding.Closest(new Vector3(2, -1, 0), Vector3.zero, Vector3.right, Vector3.up, out bary);
            Assert.That(q, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void TriangleGridMatchesABruteForceScan()
        {
            // Random triangle soup (small, large and sliver triangles) and query points on, near and far from it.
            var random = new System.Random(7);
            float R(float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new int[3 * 600];
            for (int t = 0; t < 600; t++)
            {
                var centre = new Vector3(R(-.1f, .1f), R(-.1f, .1f), R(-.02f, .02f));
                float size = t % 10 == 0 ? .04f : .004f;
                for (int k = 0; k < 3; k++) { triangles[3 * t + k] = vertices.Count; vertices.Add(centre + new Vector3(R(-size, size), R(-size, size), R(-size, size) * (t % 7 == 0 ? .01f : 1))); }
            }
            var candidates = new System.Collections.Generic.List<int>();
            for (int t = 0; t < triangles.Length; t += 3) candidates.Add(t);
            var bounds = new Bounds(Vector3.zero, new Vector3(.3f, .3f, .2f));
            var grid = new GroomSurfaceBinding.TriangleGrid(vertices, triangles, candidates, bounds);
            for (int i = 0; i < 3000; i++)
            {
                var p = i % 3 == 0 ? vertices[random.Next(vertices.Count)] : new Vector3(R(-.14f, .14f), R(-.14f, .14f), R(-.09f, .09f));
                float best = float.MaxValue; int bestT = -1;
                foreach (int t in candidates)
                {
                    var q = GroomSurfaceBinding.Closest(p, vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]], out _);
                    float d = (q - p).sqrMagnitude;
                    if (d < best) { best = d; bestT = t; }
                }
                grid.Closest(p, out int gridT, out _, out _, out float gridSq);
                Assert.That(gridT, Is.EqualTo(bestT), "query " + i);
                Assert.That(gridSq, Is.EqualTo(best));
            }
        }

        [Test]
        public void StrandsFollowTheSkinnedSurfaceAndRestoreOnRelease()
        {
            var root = new GameObject("binding test");
            var meshes = new Mesh[2];
            try
            {
                // A two-triangle skin patch weighted to one bone, and a strand mesh 5 mm above it.
                var bone = new GameObject("bone").transform; bone.SetParent(root.transform, false);
                var skinObject = new GameObject("skin"); skinObject.transform.SetParent(root.transform, false);
                var skin = skinObject.AddComponent<SkinnedMeshRenderer>();
                var patch = meshes[0] = new Mesh { vertices = new[] { new Vector3(-.01f, 0, -.01f), new Vector3(.01f, 0, -.01f), new Vector3(.01f, 0, .01f), new Vector3(-.01f, 0, .01f) }, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
                patch.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 } };
                patch.bindposes = new[] { bone.worldToLocalMatrix * skinObject.transform.localToWorldMatrix };
                skin.sharedMesh = patch; skin.bones = new[] { bone }; skin.rootBone = bone;
                var strandObject = new GameObject("strands"); strandObject.transform.SetParent(root.transform, false);
                var filter = strandObject.AddComponent<MeshFilter>();
                var strands = meshes[1] = new Mesh { vertices = new[] { new Vector3(0, .005f, 0), new Vector3(.002f, .006f, .001f) }, normals = new[] { Vector3.right, Vector3.right } };
                strands.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
                filter.sharedMesh = strands;
                var binding = strandObject.AddComponent<GroomSurfaceBinding>();
                binding.face = skin; binding.strands = filter;
                binding.Bind();
                Assert.That(binding.BoundVertices, Is.EqualTo(2));
                Assert.That(binding.MaximumBindDistance, Is.EqualTo(.006f).Within(1e-5f));
                // Rigid bone motion: the strands move with it, offsets and directions included.
                bone.SetPositionAndRotation(new Vector3(.1f, .2f, .3f), Quaternion.Euler(30, 40, 50));
                binding.UpdateNow();
                var moved = filter.sharedMesh;
                Assert.That(moved, Is.Not.SameAs(strands));
                for (int i = 0; i < 2; i++)
                {
                    Assert.That(Vector3.Distance(moved.vertices[i], bone.TransformPoint(strands.vertices[i])), Is.LessThan(1e-5f));
                    Assert.That(Vector3.Angle(moved.normals[i], bone.TransformDirection(Vector3.right)), Is.LessThan(.01f));
                }
                binding.Release();
                Assert.That(filter.sharedMesh, Is.SameAs(strands));
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var m in meshes) if (m) Object.DestroyImmediate(m);
            }
        }

        // Two skin triangles on two bones; one strand rooted over the first with its tip over the second. Moving the
        // second bone drags the tip with per-point binding; rigid strands keep the whole strand on the root's triangle.
        [Test]
        public void RigidStrandsFollowTheirRootTriangle()
        {
            foreach (bool rigid in new[] { false, true })
            {
                var root = new GameObject("rigid binding test"); var meshes = new Mesh[2];
                try
                {
                    var boneA = new GameObject("A").transform; boneA.SetParent(root.transform, false);
                    var boneB = new GameObject("B").transform; boneB.SetParent(root.transform, false);
                    var skinObject = new GameObject("skin"); skinObject.transform.SetParent(root.transform, false);
                    var skin = skinObject.AddComponent<SkinnedMeshRenderer>();
                    var patch = meshes[0] = new Mesh
                    {
                        vertices = new[] { new Vector3(-.02f, 0, -.01f), new Vector3(-.02f, 0, .01f), new Vector3(-.001f, 0, 0), new Vector3(.001f, 0, 0), new Vector3(.02f, 0, .01f), new Vector3(.02f, 0, -.01f) },
                        triangles = new[] { 0, 1, 2, 3, 4, 5 }
                    };
                    patch.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 },
                                                new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 1, weight0 = 1 } };
                    patch.bindposes = new[] { boneA.worldToLocalMatrix * skinObject.transform.localToWorldMatrix, boneB.worldToLocalMatrix * skinObject.transform.localToWorldMatrix };
                    skin.sharedMesh = patch; skin.bones = new[] { boneA, boneB }; skin.rootBone = boneA;
                    var strandObject = new GameObject("strands"); strandObject.transform.SetParent(root.transform, false);
                    var filter = strandObject.AddComponent<MeshFilter>();
                    // Ribbon layout (GroomRibbonBaker): each point twice, UV0 (side, along); then a second strand.
                    Vector3 r0 = new Vector3(-.012f, .002f, 0), r1 = new Vector3(0, .003f, 0), r2 = new Vector3(.012f, .004f, 0), s0 = new Vector3(.012f, .002f, .002f);
                    var strands = meshes[1] = new Mesh { vertices = new[] { r0, r0, r1, r1, r2, r2, s0, s0 } };
                    strands.SetUVs(0, new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(-1, .5f), new Vector2(1, .5f), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, 0), new Vector2(1, 0) });
                    strands.SetIndices(new[] { 0, 2, 2, 4, 6, 7 }, MeshTopology.Lines, 0);
                    filter.sharedMesh = strands;
                    var binding = strandObject.AddComponent<GroomSurfaceBinding>();
                    binding.face = skin; binding.strands = filter; binding.rigidStrands = rigid;
                    binding.Bind();
                    boneB.SetPositionAndRotation(new Vector3(0, .01f, 0), Quaternion.Euler(0, 0, 40));
                    binding.UpdateNow();
                    var bound = filter.sharedMesh.vertices;
                    for (int tipSide = 4; tipSide <= 5; tipSide++)
                        if (rigid) Assert.That(Vector3.Distance(bound[tipSide], strands.vertices[tipSide]), Is.LessThan(1e-5f), "rigid: the tip stays with the root's triangle");
                        else Assert.That(Vector3.Distance(bound[tipSide], strands.vertices[tipSide]), Is.GreaterThan(.005f), "per point: the tip rides the second triangle");
                    // The second strand starts over (its root is over the second triangle): it moves with that bone either way.
                    Assert.That(Vector3.Distance(bound[6], strands.vertices[6]), Is.GreaterThan(.005f), "second strand follows its own root");
                    binding.Release();
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    foreach (var m in meshes) if (m) Object.DestroyImmediate(m);
                }
            }
        }
    }
}
