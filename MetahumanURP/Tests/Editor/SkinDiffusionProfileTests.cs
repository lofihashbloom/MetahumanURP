using System;
using MetaHuman.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MetaHuman.Tests
{
    // Pins the Unreal subsurface-profile conversion (SkinDiffusionSetup.GetScatterDistances) to Unreal 5.8's own Burley
    // math, recomputed here from the engine formulas rather than from the package code:
    //   SubsurfaceProfile.cpp:    profile DMFP (mm) = GetDiffuseMeanFreePathFromMeanFreePath(albedo, MFPColor x MFPDistance cm) x 10
    //   SubsurfaceScatteringMath: DMFP = MFP x SearchLight(A) / (Perpendicular(A) x 0.6)
    //   BurleyNormalizedSSSCommon (LIGHT_PERPENDICULAR_DIFFUSE_SURFACE, the default): D = DMFP / SearchLight(A)
    //   SubsurfaceBurleyNormalized.ush CalculateBurleyScale with SSSScaleX = P00 / SUBSURFACE_KERNEL_SIZE x 0.5
    //   (PostProcessSubsurface.cpp): UV = WorldUnitScale x r_mm x P00 / (20 z) = 0.5 P00 d / z, so real mm = profile mm x WorldUnitScale.
    public sealed class SkinDiffusionProfileTests
    {
        // Epic's SSP_Head as exported (MetaHumanSource/<Name>/<Name>-MaterialInputs.mhmanifest.json).
        static SubsurfaceProfileRecord Head() => new SubsurfaceProfileRecord
        {
            source = "/Game/MetaHumans/Common/Face/SubsurfaceProfiles/SSP_Head.SSP_Head", burley = true, meanFreePath = true,
            surfaceAlbedo = new[] { 0.609375f, 0.394274f, 0.317383f },
            meanFreePathColor = new[] { 0.25984427f, 0.08028142f, 0.03716606f },
            meanFreePathDistanceCentimetres = 0.1f, worldUnitScale = 1 / 3f,
        };

        static float Perpendicular(float a) => 1.85f - a + 7 * Mathf.Pow(Mathf.Abs(a - .8f), 3);
        static float SearchLight(float a) => 3.5f + 100 * Mathf.Pow(a - .33f, 4);

        [Test]
        public void HeadScatterDistancesMatchUnrealBurleyInRealMillimetres()
        {
            var head = Head();
            var actual = SkinDiffusionSetup.GetScatterDistances(head);
            for (int c = 0; c < 3; c++)
            {
                float a = head.surfaceAlbedo[c];
                float mfpMm = head.meanFreePathColor[c] * head.meanFreePathDistanceCentimetres * 10;
                float dmfpMm = mfpMm * SearchLight(a) / (Perpendicular(a) * .6f);
                float expected = dmfpMm / SearchLight(a) * head.worldUnitScale;
                Assert.That(actual[c], Is.EqualTo(expected).Within(1e-5f), "channel " + c);
            }
            // Absolute values (mm) for review: red is about 0.11 mm, i.e. sub-pixel at portrait framings, in Unreal too.
            Assert.That(actual.x, Is.EqualTo(.11198f).Within(5e-5f));
            Assert.That(actual.y, Is.EqualTo(.02319f).Within(5e-5f));
            Assert.That(actual.z, Is.EqualTo(.00890f).Within(5e-5f));
        }

        [Test]
        public void ScatterDistancesScaleLinearlyWithWorldUnitScaleAndDistance()
        {
            var head = Head(); var baseline = SkinDiffusionSetup.GetScatterDistances(head);
            head.worldUnitScale *= 3; var wider = SkinDiffusionSetup.GetScatterDistances(head);
            head.worldUnitScale /= 3; head.meanFreePathDistanceCentimetres *= 2; var longer = SkinDiffusionSetup.GetScatterDistances(head);
            for (int c = 0; c < 3; c++)
            {
                Assert.That(wider[c] / baseline[c], Is.EqualTo(3).Within(1e-4f));
                Assert.That(longer[c] / baseline[c], Is.EqualTo(2).Within(1e-4f));
            }
        }
    }
}
