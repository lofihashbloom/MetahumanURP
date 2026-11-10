using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MetaHuman.Dna
{
    /// <summary>One independently owned OpenRigLogic instance. Use on one thread at a time.</summary>
    public sealed class NativeRig : IDisposable
    {
        const string Library = "metahuman_riglogic";
        sealed class RigHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public RigHandle(IntPtr pointer) : base(true) { SetHandle(pointer); }
            protected override bool ReleaseHandle() { Destroy(handle); return true; }
        }
        [DllImport(Library, EntryPoint = "mh_abi_version", CallingConvention = CallingConvention.Cdecl)] static extern int AbiVersion();
        [DllImport(Library, EntryPoint = "mh_create", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Create(byte[] bytes, int length, StringBuilder error, int capacity);
        [DllImport(Library, EntryPoint = "mh_destroy", CallingConvention = CallingConvention.Cdecl)] static extern void Destroy(IntPtr handle);
        [DllImport(Library, EntryPoint = "mh_counts", CallingConvention = CallingConvention.Cdecl)] static extern int Counts(RigHandle handle, [Out] int[] counts, int capacity);
        [DllImport(Library, EntryPoint = "mh_name", CallingConvention = CallingConvention.Cdecl)] static extern int Name(RigHandle handle, int kind, int index, StringBuilder name, int capacity);
        [DllImport(Library, EntryPoint = "mh_neutral", CallingConvention = CallingConvention.Cdecl)] static extern int Neutral(RigHandle handle, [Out] float[] values, int capacity);
        [DllImport(Library, EntryPoint = "mh_parent", CallingConvention = CallingConvention.Cdecl)] static extern int Parent(RigHandle handle, int index);
        [DllImport(Library, EntryPoint = "mh_evaluate", CallingConvention = CallingConvention.Cdecl)] static extern int EvaluateNative(RigHandle handle, [In] float[] raw, int rawCount, int lod,
            [Out] float[] joints, int jointCount, [Out] float[] morphs, int morphCount, [Out] float[] maps, int mapCount);

        RigHandle handle;
        public string[] RawControlNames { get; }
        public string[] JointNames { get; }
        public string[] BlendShapeNames { get; }
        public string[] AnimatedMapNames { get; }
        public int[] JointParents { get; }
        public int LodCount { get; }
        public float[] NeutralJoints { get; }
        public float[] JointDeltas { get; }
        public float[] BlendShapes { get; }
        public float[] AnimatedMaps { get; }

        public NativeRig(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes[0] != 'D' || bytes[1] != 'N' || bytes[2] != 'A')
                throw new ArgumentException("Expected a standalone binary DNA stream.", nameof(bytes));
            if (AbiVersion() != 1) throw new NotSupportedException("The native MetaHuman RigLogic ABI does not match this package.");
            var error = new StringBuilder(4096);
            IntPtr pointer = Create(bytes, bytes.Length, error, error.Capacity);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("OpenRigLogic: " + error);
            handle = new RigHandle(pointer);
            try
            {
                var counts = new int[7];
                if (Counts(handle, counts, counts.Length) != 1 || counts[2] != counts[1] * 10)
                    throw new InvalidOperationException("Unexpected native joint output layout; expected translation/quaternion/scale.");
                RawControlNames = Names(0, counts[0]);
                JointNames = Names(1, counts[1]);
                BlendShapeNames = Names(2, counts[3]);
                AnimatedMapNames = Names(3, counts[4]);
                LodCount = counts[5];
                JointParents = new int[counts[1]];
                for (int i = 0; i < JointParents.Length; i++) JointParents[i] = Parent(handle, i);
                NeutralJoints = new float[counts[2]];
                JointDeltas = new float[counts[2]];
                BlendShapes = new float[counts[3]];
                AnimatedMaps = new float[counts[4]];
                if (Neutral(handle, NeutralJoints, NeutralJoints.Length) != 1) throw new InvalidOperationException("Native neutral data unavailable.");
            }
            catch { Dispose(); throw; }
        }

        string[] Names(int kind, int count)
        {
            var names = new string[count];
            var buffer = new StringBuilder(1024);
            for (int i = 0; i < count; i++)
            {
                buffer.Clear();
                if (Name(handle, kind, i, buffer, buffer.Capacity) != 1) throw new InvalidOperationException("Native metadata name unavailable.");
                names[i] = buffer.ToString();
            }
            return names;
        }

        public void Evaluate(float[] rawControls, int lod = 0)
        {
            if (handle == null || handle.IsClosed) throw new ObjectDisposedException(nameof(NativeRig));
            if (rawControls == null || rawControls.Length != RawControlNames.Length) throw new ArgumentException("Raw control count does not match DNA.");
            if (lod < 0 || lod >= LodCount) throw new ArgumentOutOfRangeException(nameof(lod));
            for (int i = 0; i < rawControls.Length; i++)
                if (float.IsNaN(rawControls[i]) || float.IsInfinity(rawControls[i])) throw new ArgumentOutOfRangeException(nameof(rawControls));
            if (EvaluateNative(handle, rawControls, rawControls.Length, lod, JointDeltas, JointDeltas.Length,
                BlendShapes, BlendShapes.Length, AnimatedMaps, AnimatedMaps.Length) != 1)
                throw new InvalidOperationException("Native facial evaluation failed.");
        }

        public void Dispose() { handle?.Dispose(); handle = null; }
    }
}
