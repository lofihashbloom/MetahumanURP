using UnityEngine;

// A talking performance as raw DNA rig controls sampled per frame, with the speech it was derived from.
// Baked from the audio-driven lip-sync track (loudness, voicing and formants of the recording mapped to
// CTRL_expressions curves, plus authored blinks, gaze and head nods). Not a captured performance.
public sealed class TalkTrack : ScriptableObject
{
    public float framesPerSecond = 30;
    public int frames;
    [Tooltip("Seconds of track before the speech starts.")] public float leadSeconds;
    public AudioClip speech;
    [Tooltip("Raw DNA control names (CTRL_expressions.*).")] public string[] controls = new string[0];
    [Tooltip("controls x frames values, one control after the other.")] public float[] values = new float[0];
    [Tooltip("Head turn in degrees per frame, shared between neck and head. May be empty.")]
    public float[] headYaw = new float[0], headPitch = new float[0], headRoll = new float[0];

    public float Seconds => frames / Mathf.Max(framesPerSecond, 1e-3f);

    /// <summary>Control c at a fractional frame (linear between frames, held past the end).</summary>
    public float Sample(int c, float frame) => Sample(values, c * frames, frame);

    public float Sample(float[] curve, int start, float frame)
    {
        if (curve == null || frames == 0 || curve.Length < start + frames) return 0;
        frame = Mathf.Clamp(frame, 0, frames - 1);
        int a = (int)frame, b = Mathf.Min(a + 1, frames - 1);
        return Mathf.Lerp(curve[start + a], curve[start + b], frame - a);
    }
}
