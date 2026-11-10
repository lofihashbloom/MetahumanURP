using System.Collections.Generic;
using MetaHuman.Groom;
using NUnit.Framework;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class GroomSwayTests
    {
        readonly List<Object> created = new List<Object>();
        const float Dt = 1 / 30f;

        [TearDown] public void TearDown() { foreach (var o in created) if (o) Object.DestroyImmediate(o); created.Clear(); }

        GroomSway Make(out Transform head)
        {
            var root = new GameObject("Sway test"); created.Add(root);
            head = new GameObject("head").transform; head.SetParent(root.transform, false); created.Add(head.gameObject);
            var sway = root.AddComponent<GroomSway>(); sway.head = head; sway.autoStep = false;
            return sway;
        }

        static void Run(GroomSway sway, int frames) { for (int i = 0; i < frames; i++) sway.Step(Dt); }

        [Test] public void StillHeadDoesNotSway()
        {
            var sway = Make(out var head);
            head.rotation = Quaternion.Euler(5, 20, 0);
            Run(sway, 120);
            Assert.That(sway.Angle.magnitude, Is.LessThan(1e-5f));
        }

        [Test] public void TurnLagsBehindThenSettles()
        {
            var sway = Make(out var head); sway.gravity = 0;
            Run(sway, 3);
            // A quick yaw (30 degrees over a quarter second): the hair lags, i.e. sways against the turn.
            float peakAgainst = 0;
            for (int i = 1; i <= 8; i++) { head.rotation = Quaternion.Euler(0, 30 * i / 8f, 0); sway.Step(Dt); peakAgainst = Mathf.Min(peakAgainst, sway.Angle.y); }
            Assert.That(peakAgainst, Is.LessThan(-.005f), "lag opposes the acceleration at the start of the turn");
            Run(sway, 150);   // five seconds of hold: the spring settles back to the head
            Assert.That(sway.Angle.magnitude, Is.LessThan(.002f));
        }

        [Test] public void TiltHangsBackTowardGravity()
        {
            var sway = Make(out var head); sway.gravity = .25f; sway.angularInertia = 0;
            Run(sway, 3);
            head.rotation = Quaternion.Euler(20, 0, 0);   // nod forward and hold
            Run(sway, 240);
            // Settled: a quarter of the tilt, about the tilt axis, back toward the rest pose.
            Assert.That(sway.Angle.x, Is.EqualTo(-.25f * 20 * Mathf.Deg2Rad).Within(.004f));
            Assert.That(Mathf.Abs(sway.Angle.y) + Mathf.Abs(sway.Angle.z), Is.LessThan(.004f));
        }

        [Test] public void SwayIsClampedToTheMaximumAngle()
        {
            var sway = Make(out var head); sway.maxAngle = 6;
            Run(sway, 3);
            for (int i = 0; i < 60; i++) { head.rotation = Quaternion.Euler(0, (i % 2) * 40, 0); sway.Step(Dt); }
            Assert.That(sway.Angle.magnitude, Is.LessThanOrEqualTo(6 * Mathf.Deg2Rad + 1e-5f));
        }

        [Test] public void ApplyWritesEveryMaterialBlockAndKeepsThePreviousFrame()
        {
            var sway = Make(out var head); sway.tipExponent = 1.7f;
            var go = new GameObject("strands"); created.Add(go);
            var r = go.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m0 = new Material(shader); var m1 = new Material(shader); created.Add(m0); created.Add(m1);
            r.sharedMaterials = new[] { m0, m1 };
            sway.renderers = new Renderer[] { r };
            Run(sway, 3);
            head.rotation = Quaternion.Euler(0, 25, 0); sway.Step(Dt);
            sway.Apply();
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < 2; i++)
            {
                r.GetPropertyBlock(block, i);
                var pivot = block.GetVector(GroomSway.PivotId); var axisAngle = block.GetVector(GroomSway.AxisAngleId);
                Assert.That(pivot.w, Is.EqualTo(1.7f).Within(1e-6f));
                Assert.That(axisAngle.w, Is.EqualTo(1));
                Assert.That(((Vector3)axisAngle - sway.Angle).magnitude, Is.LessThan(1e-6f));
                Assert.That(block.GetVector(GroomSway.PrevAxisAngleId), Is.EqualTo(axisAngle), "first apply: previous = current");
                var fine = block.GetVector(GroomSway.FineId); var front = block.GetVector(GroomSway.FrontId);
                Assert.That(((Vector3)fine - sway.FineAngle).magnitude, Is.LessThan(1e-6f)); Assert.That(fine.w, Is.EqualTo(sway.variation).Within(1e-6f));
                Assert.That(front.w, Is.EqualTo(sway.frontBoost).Within(1e-6f)); Assert.That(((Vector3)front).magnitude, Is.EqualTo(1).Within(1e-5f));
            }
            var first = sway.Angle; sway.Step(Dt); sway.Apply();
            r.GetPropertyBlock(block, 0);
            Assert.That(((Vector3)block.GetVector(GroomSway.PrevAxisAngleId) - first).magnitude, Is.LessThan(1e-6f), "motion vectors get last frame's sway");
            sway.Clear();
            r.GetPropertyBlock(block, 1);
            Assert.That(block.GetVector(GroomSway.AxisAngleId), Is.EqualTo(Vector4.zero));
        }

        [Test] public void LooseSpringMovesMoreThanTheMainSpring()
        {
            var sway = Make(out var head); sway.gravity = sway.fineGravity = 0;
            Run(sway, 3);
            float main = 0, loose = 0;
            for (int i = 1; i <= 40; i++)
            {
                head.rotation = Quaternion.Euler(0, 15 * Mathf.Sin(i * Dt * 2 * Mathf.PI * .7f), 0); sway.Step(Dt);
                main = Mathf.Max(main, sway.Angle.magnitude); loose = Mathf.Max(loose, sway.FineAngle.magnitude);
            }
            Assert.That(loose, Is.GreaterThan(1.5f * main), "the softer, less damped spring swings wider for the same head motion");
        }

        [Test] public void RotationVectorRoundTrips()
        {
            var axis = new Vector3(.3f, -.8f, .5f).normalized;
            var v = GroomSway.RotationVector(Quaternion.AngleAxis(35, axis));
            Assert.That((v - axis * 35 * Mathf.Deg2Rad).magnitude, Is.LessThan(1e-5f));
            Assert.That((GroomSway.RotationVector(Quaternion.AngleAxis(-200, axis)) - axis * 160 * Mathf.Deg2Rad).magnitude, Is.LessThan(1e-4f), "shortest way");
        }
    }
}
