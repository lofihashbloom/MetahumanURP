using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Original, versioned source-curve interchange; asset-local centimetres, not render buffers.</summary>
    public sealed class GroomCurveData
    {
        public struct Strand { public int first, count, group; public bool guide; public Vector2 rootUV; public float width; }
        public Strand[] strands;
        public Vector3[] positions;
        public float[] widths;

        public static GroomCurveData Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "MHGRM001") throw new InvalidDataException("Unknown groom curve format.");
            int curves = reader.ReadInt32(), points = reader.ReadInt32();
            long length = 16L + 28L * curves + 16L * points;
            if (curves < 1 || curves > 1000000 || points < curves || points > 10000000 || !stream.CanSeek || stream.Length != length)
                throw new InvalidDataException("Invalid groom counts or file length.");
            var data = new GroomCurveData { strands = new Strand[curves], positions = new Vector3[points], widths = new float[points] };
            int next = 0;
            for (int i = 0; i < curves; i++)
            {
                int first = reader.ReadInt32(), count = reader.ReadInt32(), guide = reader.ReadInt32(), group = reader.ReadInt32();
                float u = reader.ReadSingle(), v = reader.ReadSingle(), width = reader.ReadSingle();
                if (first != next || count < 2 || count > points - next || guide < 0 || guide > 1 || group < 0 || group > 1023 ||
                    !Finite(u) || !Finite(v) || !Finite(width) || width < 0)
                    throw new InvalidDataException("Invalid groom strand record.");
                data.strands[i] = new Strand { first = first, count = count, guide = guide == 1, group = group, rootUV = new Vector2(u, v), width = width };
                next += count;
            }
            if (next != points) throw new InvalidDataException("Strands do not cover the point buffer.");
            for (int i = 0; i < points; i++)
            {
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle(), width = reader.ReadSingle();
                if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(width) || width < 0)
                    throw new InvalidDataException("Nonfinite position or invalid groom width.");
                data.positions[i] = new Vector3(x, y, z); data.widths[i] = width;
            }
            return data;
        }
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
