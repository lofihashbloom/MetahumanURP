using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Teaser film grade in Unity: the offline grade of Tools/QA/v34/Teaser/beauty.py (and the temporal filter of
// compose_beauty_video.py) as compute passes on the centre square of a scene-linear frame (Resources/FilmGrade.compute).
// FilmGrade.Process grades three frames of history into an 8-bit display-encoded square; FilmGradeFeature
// runs it after URP's post-processing and presents the square, pillarboxed, in the camera target.
// Validation code of the teaser stage, not part of the MetaHuman package.
public static class FilmGrade
{
    [Serializable]
    public sealed class Settings
    {
        public float exposure = 1;
        public Vector3 wb = Vector3.one;
        [Tooltip("Kelvin; 6500 neutral, higher warmer")] public float temperature = 6500;
        [Tooltip("Green (-) to magenta (+)")] public float tint;
        public float sat = 1, contrast = 1, vignette;
        [Tooltip("Monochrome grain sigma in display units")] public float grain;
        public float streak, streakThreshold = 2, streakLength = .2f;
        public Vector3 streakTint = new Vector3(.45f, .62f, 1);
        [Tooltip("Lateral chromatic aberration")] public float ca;
        public float glow, glowThreshold = 1, glowRadius = .03f;

        public static Settings FromJson(string json) => JsonUtility.FromJson<Settings>(json);
    }

    static ComputeShader shader;
    static RenderTexture lin, glowA, glowB, s0, s1, s2, output;
    public static RenderTexture Output => output;

    static readonly int A = Shader.PropertyToID("_A"), B = Shader.PropertyToID("_B"), C = Shader.PropertyToID("_C"), Src = Shader.PropertyToID("_Src"), Dst = Shader.PropertyToID("_Dst"),
        Lin = Shader.PropertyToID("_Lin"), LinR = Shader.PropertyToID("_LinR"), Bright = Shader.PropertyToID("_Bright"), GlowR = Shader.PropertyToID("_GlowR"),
        S0 = Shader.PropertyToID("_S0"), S0R = Shader.PropertyToID("_S0R"), S1 = Shader.PropertyToID("_S1"), S1R = Shader.PropertyToID("_S1R"), S2 = Shader.PropertyToID("_S2"), S2R = Shader.PropertyToID("_S2R"), Out = Shader.PropertyToID("_Out");

    static RenderTexture Target(string name, int side, GraphicsFormat format)
    {
        var rt = new RenderTexture(side, side, 0, format) { name = name, enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        rt.Create();
        return rt;
    }

    public static void Release()
    {
        foreach (var rt in new[] { lin, glowA, glowB, s0, s1, s2, output }) if (rt) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        lin = glowA = glowB = s0 = s1 = s2 = output = null;
    }

    // Linear sRGB (Y = 1) of the Planckian locus (Kim et al. 2002 fit), as beauty.kelvin_rgb.
    static double[] KelvinRgb(double t)
    {
        t = Math.Min(Math.Max(t, 1667.0), 25000.0);
        double x = t <= 4000 ? -0.2661239e9 / (t * t * t) - 0.2343589e6 / (t * t) + 0.8776956e3 / t + 0.179910
                             : -3.0258469e9 / (t * t * t) + 2.1070379e6 / (t * t) + 0.2226347e3 / t + 0.240390;
        double y = t <= 2222 ? -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683
                 : t <= 4000 ? -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867
                 : 3.0817580 * x * x * x - 5.87338670 * x * x + 3.75112997 * x - 0.37001483;
        double X = x / y, Z = (1 - x - y) / y;
        return new[] { 3.2404542 * X - 1.5371385 - 0.4985314 * Z, -0.9692660 * X + 1.8760108 + 0.0415560 * Z, 0.0556434 * X - 0.2040259 + 1.0572252 * Z };
    }

    public static Vector3 Gains(Settings s)
    {
        double[] g = { s.exposure * (double)s.wb.x, s.exposure * (double)s.wb.y, s.exposure * (double)s.wb.z };
        if ((s.temperature != 0 && s.temperature != 6500) || s.tint != 0)
        {
            double[] neutral = KelvinRgb(6500), white = KelvinRgb(s.temperature != 0 ? s.temperature : 6500);
            double[] t = { neutral[0] / white[0], neutral[1] / white[1] * (1 - s.tint), neutral[2] / white[2] };
            double lum = .2126 * t[0] + .7152 * t[1] + .0722 * t[2];
            for (int i = 0; i < 3; i++) g[i] *= t[i] / lum;
        }
        return new Vector3((float)g[0], (float)g[1], (float)g[2]);
    }

    /// <summary>Grades weights.x * a + weights.y * b + weights.z * c (scene-linear frames of one size; the centre square
    /// of height x height is graded) into <see cref="Output"/> (8-bit, display-encoded sRGB values, linear texture).</summary>
    public static bool Process(CommandBuffer cmd, Texture a, Texture b, Texture c, Vector3 weights, Settings s, int frame)
    {
        if (!shader) shader = Resources.Load<ComputeShader>("FilmGrade");
        if (!shader || !a || !b || !c) return false;
        int side = Mathf.Min(a.width, a.height);
        if (!output || output.width != side)
        {
            Release();
            lin = Target("Film grade linear", side, GraphicsFormat.R32G32B32A32_SFloat);
            glowA = Target("Film grade glow A", side, GraphicsFormat.R32G32B32A32_SFloat);
            glowB = Target("Film grade glow B", side, GraphicsFormat.R32G32B32A32_SFloat);
            s0 = Target("Film grade streak source", side, GraphicsFormat.R32_SFloat);
            s1 = Target("Film grade streak forward", side, GraphicsFormat.R32_SFloat);
            s2 = Target("Film grade streak", side, GraphicsFormat.R32_SFloat);
            output = Target("Film grade output", side, GraphicsFormat.R8G8B8A8_UNorm);
        }
        int prepare = shader.FindKernel("Prepare"), boxV = shader.FindKernel("BoxV"), boxH = shader.FindKernel("BoxH"),
            streakSource = shader.FindKernel("StreakSource"), streak = shader.FindKernel("Streak"), streakBack = shader.FindKernel("StreakBack"), final = shader.FindKernel("Final");
        int tiles = (side + 7) / 8, lines = (side + 63) / 64;
        bool glowOn = s.glow > 0, streakOn = s.streak > 0;

        cmd.SetComputeIntParam(shader, "_Size", side);
        cmd.SetComputeIntParam(shader, "_OffsetX", (a.width - side) / 2);
        cmd.SetComputeIntParam(shader, "_OffsetY", (a.height - side) / 2);
        cmd.SetComputeIntParam(shader, "_Frame", frame);
        cmd.SetComputeVectorParam(shader, "_Weights", weights);
        cmd.SetComputeVectorParam(shader, "_Gain", Gains(s));
        cmd.SetComputeFloatParam(shader, "_GlowThreshold", s.glowThreshold > 0 ? s.glowThreshold : 1);
        cmd.SetComputeFloatParam(shader, "_GlowGain", glowOn ? s.glow : 0);
        cmd.SetComputeFloatParam(shader, "_StreakThreshold", s.streakThreshold > 0 ? s.streakThreshold : 2);
        cmd.SetComputeFloatParam(shader, "_StreakGain", streakOn ? s.streak : 0);
        cmd.SetComputeVectorParam(shader, "_StreakTint", s.streakTint);
        cmd.SetComputeFloatParam(shader, "_CA", s.ca);
        cmd.SetComputeFloatParam(shader, "_Sat", s.sat);
        cmd.SetComputeFloatParam(shader, "_Contrast", s.contrast);
        cmd.SetComputeFloatParam(shader, "_Vignette", s.vignette);
        cmd.SetComputeFloatParam(shader, "_Grain", s.grain);

        cmd.SetComputeTextureParam(shader, prepare, A, a);
        cmd.SetComputeTextureParam(shader, prepare, B, b);
        cmd.SetComputeTextureParam(shader, prepare, C, c);
        cmd.SetComputeTextureParam(shader, prepare, Lin, lin);
        cmd.SetComputeTextureParam(shader, prepare, Bright, glowA);
        cmd.DispatchCompute(shader, prepare, tiles, tiles, 1);

        if (glowOn)
        {
            // Three box blurs down the columns, then three along the rows; the result ends in glowA.
            cmd.SetComputeIntParam(shader, "_K", Mathf.Max(1, (int)Math.Round((s.glowRadius > 0 ? s.glowRadius : .03) * side)));
            for (int i = 0; i < 6; i++)
            {
                int kernel = i < 3 ? boxV : boxH;
                cmd.SetComputeTextureParam(shader, kernel, Src, i % 2 == 0 ? glowA : glowB);
                cmd.SetComputeTextureParam(shader, kernel, Dst, i % 2 == 0 ? glowB : glowA);
                cmd.DispatchCompute(shader, kernel, lines, 1, 1);
            }
        }
        if (streakOn)
        {
            double length = Math.Max((s.streakLength > 0 ? s.streakLength : .2) * side, 2), shortLength = length / 8;
            int r = (int)Math.Min(4 * length, side);
            double sumLong = 0, sumShort = 0;
            for (int x = -r; x <= r; x++) { sumLong += Math.Exp(-Math.Abs(x) / length); sumShort += Math.Exp(-Math.Abs(x) / shortLength); }
            cmd.SetComputeIntParam(shader, "_StreakRadius", r);
            cmd.SetComputeVectorParam(shader, "_StreakA", new Vector4((float)Math.Exp(-1 / length), (float)Math.Exp(-(r + 1) / length), (float)Math.Exp(-1 / shortLength), (float)Math.Exp(-(r + 1) / shortLength)));
            cmd.SetComputeVectorParam(shader, "_StreakW", new Vector4((float)(.7 / sumLong), (float)(.3 / sumShort), 0, 0));
            cmd.SetComputeTextureParam(shader, streakSource, LinR, lin);
            cmd.SetComputeTextureParam(shader, streakSource, GlowR, glowA);
            cmd.SetComputeTextureParam(shader, streakSource, S0, s0);
            cmd.DispatchCompute(shader, streakSource, tiles, tiles, 1);
            cmd.SetComputeTextureParam(shader, streak, S0R, s0);
            cmd.SetComputeTextureParam(shader, streak, S1, s1);
            cmd.DispatchCompute(shader, streak, lines, 1, 1);
            cmd.SetComputeTextureParam(shader, streakBack, S0R, s0);
            cmd.SetComputeTextureParam(shader, streakBack, S1R, s1);
            cmd.SetComputeTextureParam(shader, streakBack, S2, s2);
            cmd.DispatchCompute(shader, streakBack, lines, 1, 1);
        }
        cmd.SetComputeTextureParam(shader, final, LinR, lin);
        cmd.SetComputeTextureParam(shader, final, GlowR, glowA);
        cmd.SetComputeTextureParam(shader, final, S2R, s2);
        cmd.SetComputeTextureParam(shader, final, Out, output);
        cmd.DispatchCompute(shader, final, tiles, tiles, 1);
        return true;
    }
}
