using System.Collections.Generic;
using LookDev.Avatar;
using MetaHuman.Dna;
using UnityEngine;

// Maps ARKit curves (Live Link Face order) onto MetaHuman DNA raw controls. Blinks and gaze are not mapped: the idle
// performance owns the eyes, whatever the lipsync module outputs on those channels.
public sealed class ArkitToDna
{
    const string P = "CTRL_expressions.";

    static readonly HashSet<string> UpperFace = new HashSet<string>
    {
        "EyeSquintLeft", "EyeSquintRight", "EyeWideLeft", "EyeWideRight", "BrowDownLeft", "BrowDownRight", "BrowInnerUp",
        "BrowOuterUpLeft", "BrowOuterUpRight", "CheekSquintLeft", "CheekSquintRight", "NoseSneerLeft", "NoseSneerRight",
    };

    static readonly Dictionary<string, (string control, float weight)[]> Table = new Dictionary<string, (string, float)[]>
    {
        ["JawForward"] = new[] { ("jawFwd", 1f) },
        ["JawRight"] = new[] { ("jawRight", 1f) },
        ["JawLeft"] = new[] { ("jawLeft", 1f) },
        ["JawOpen"] = new[] { ("jawOpen", 1f) },
        ["MouthClose"] = new[] { ("mouthLipsTogetherUL", 1f), ("mouthLipsTogetherUR", 1f), ("mouthLipsTogetherDL", 1f), ("mouthLipsTogetherDR", 1f) },
        ["MouthFunnel"] = new[] { ("mouthFunnelUL", 1f), ("mouthFunnelUR", 1f), ("mouthFunnelDL", 1f), ("mouthFunnelDR", 1f) },
        ["MouthPucker"] = new[] { ("mouthLipsPurseUL", 1f), ("mouthLipsPurseUR", 1f), ("mouthLipsPurseDL", 1f), ("mouthLipsPurseDR", 1f),
                                  ("mouthLipsTowardsUL", .5f), ("mouthLipsTowardsUR", .5f), ("mouthLipsTowardsDL", .5f), ("mouthLipsTowardsDR", .5f) },
        ["MouthRight"] = new[] { ("mouthRight", 1f) },
        ["MouthLeft"] = new[] { ("mouthLeft", 1f) },
        ["MouthSmileLeft"] = new[] { ("mouthCornerPullL", 1f) },
        ["MouthSmileRight"] = new[] { ("mouthCornerPullR", 1f) },
        ["MouthFrownLeft"] = new[] { ("mouthCornerDepressL", 1f) },
        ["MouthFrownRight"] = new[] { ("mouthCornerDepressR", 1f) },
        ["MouthDimpleLeft"] = new[] { ("mouthDimpleL", 1f) },
        ["MouthDimpleRight"] = new[] { ("mouthDimpleR", 1f) },
        ["MouthStretchLeft"] = new[] { ("mouthStretchL", 1f) },
        ["MouthStretchRight"] = new[] { ("mouthStretchR", 1f) },
        ["MouthRollLower"] = new[] { ("mouthLowerLipRollInL", 1f), ("mouthLowerLipRollInR", 1f) },
        ["MouthRollUpper"] = new[] { ("mouthUpperLipRollInL", 1f), ("mouthUpperLipRollInR", 1f) },
        ["MouthShrugLower"] = new[] { ("jawChinRaiseDL", 1f), ("jawChinRaiseDR", 1f) },
        ["MouthShrugUpper"] = new[] { ("jawChinRaiseUL", 1f), ("jawChinRaiseUR", 1f) },
        ["MouthPressLeft"] = new[] { ("mouthLipsPressL", 1f) },
        ["MouthPressRight"] = new[] { ("mouthLipsPressR", 1f) },
        ["MouthLowerDownLeft"] = new[] { ("mouthLowerLipDepressL", 1f) },
        ["MouthLowerDownRight"] = new[] { ("mouthLowerLipDepressR", 1f) },
        ["MouthUpperUpLeft"] = new[] { ("mouthUpperLipRaiseL", 1f) },
        ["MouthUpperUpRight"] = new[] { ("mouthUpperLipRaiseR", 1f) },
        ["CheekPuff"] = new[] { ("mouthCheekBlowL", 1f), ("mouthCheekBlowR", 1f) },
        ["CheekSquintLeft"] = new[] { ("eyeCheekRaiseL", 1f) },
        ["CheekSquintRight"] = new[] { ("eyeCheekRaiseR", 1f) },
        ["TongueOut"] = new[] { ("tongueOut", 1f) },
        ["EyeSquintLeft"] = new[] { ("eyeSquintInnerL", 1f) },
        ["EyeSquintRight"] = new[] { ("eyeSquintInnerR", 1f) },
        // the full widen lifts the upper lid well above the iris
        ["EyeWideLeft"] = new[] { ("eyeWidenL", .3f) },
        ["EyeWideRight"] = new[] { ("eyeWidenR", .3f) },
        ["BrowDownLeft"] = new[] { ("browDownL", 1f), ("browLateralL", 1f) },
        ["BrowDownRight"] = new[] { ("browDownR", 1f), ("browLateralR", 1f) },
        ["BrowInnerUp"] = new[] { ("browRaiseInL", 1f), ("browRaiseInR", 1f) },
        ["BrowOuterUpLeft"] = new[] { ("browRaiseOuterL", 1f) },
        ["BrowOuterUpRight"] = new[] { ("browRaiseOuterR", 1f) },
        ["NoseSneerLeft"] = new[] { ("noseWrinkleL", 1f) },
        ["NoseSneerRight"] = new[] { ("noseWrinkleR", 1f) },
    };

    readonly string[] controls;
    readonly List<(int channel, int control, float weight, bool upper)> links = new List<(int, int, float, bool)>();
    readonly int jaw;
    readonly float[] values;

    /// <summary>The jawOpen value written by the last Apply (0 to 1).</summary>
    public float JawOpen => jaw >= 0 ? Mathf.Clamp01(values[jaw]) : 0;

    public ArkitToDna(ICollection<string> rigControls)
    {
        var index = new Dictionary<string, int>();
        var names = new List<string>();
        var missing = new List<string>();
        for (int c = 0; c < FaceCurves.LiveLinkNames.Length; c++)
        {
            if (!Table.TryGetValue(FaceCurves.LiveLinkNames[c], out var targets)) continue;
            bool upper = UpperFace.Contains(FaceCurves.LiveLinkNames[c]);
            foreach (var (control, weight) in targets)
            {
                string full = P + control;
                if (!rigControls.Contains(full)) { missing.Add(full); continue; }
                if (!index.TryGetValue(full, out int i)) { i = names.Count; index.Add(full, i); names.Add(full); }
                links.Add((c, i, weight, upper));
            }
        }
        controls = names.ToArray();
        jaw = names.IndexOf(P + "jawOpen");
        values = new float[controls.Length];
        if (missing.Count > 0) Debug.LogWarning("DNA controls not in this rig: " + string.Join(", ", missing));
    }

    // One interpolated frame of curves whose frames are `stride` values wide (channels 0..51 in Live Link order);
    // null writes the rest pose.
    public void Apply(DnaRigDriver rig, float[] frames, int stride, float frame, float gain, float upperGain = 1)
    {
        System.Array.Clear(values, 0, values.Length);
        if (frames != null)
        {
            int n = frames.Length / stride;
            frame = Mathf.Clamp(frame, 0, n - 1);
            int a = (int)frame, b = Mathf.Min(a + 1, n - 1);
            float t = frame - a;
            foreach (var (channel, control, weight, upper) in links)
            {
                float v = Mathf.Lerp(frames[a * stride + channel], frames[b * stride + channel], t);
                values[control] += weight * v * (upper ? upperGain : gain);
            }
        }
        for (int i = 0; i < controls.Length; i++) rig.SetRawControl(controls[i], Mathf.Clamp01(values[i]));
    }
}
