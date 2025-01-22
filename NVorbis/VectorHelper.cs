using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis
{
    internal static class VectorHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> Create<T>(ReadOnlySpan<T> values)
            where T : struct
        {
#if NET9_0_OR_GREATER
            return Vector.Create(values);
#else
            var span = values.Slice(0, Vector<T>.Count);
            ref byte address = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span));
            return Unsafe.ReadUnaligned<Vector<T>>(ref address);
#endif
        }
    }
}
