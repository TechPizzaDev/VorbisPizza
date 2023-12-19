using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace NVorbis
{
    internal static class Vector256Helper
    {
        // TODO: AdvSimd

        public static bool IsSupported => Vector256.IsHardwareAccelerated && Avx2.IsSupported;

        public static bool IsAcceleratedGather => IsSupported;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Vector256<float> Gather(
            float* baseAddress, 
            Vector256<int> index,
            [ConstantExpected(Min = 1, Max = 8)] byte scale)
        {
            if (Avx2.IsSupported)
            {
                return Avx2.GatherVector256(baseAddress, index, scale);
            }

            ThrowUnreachableException();
            return default;
        }

        [DoesNotReturn]
        private static void ThrowUnreachableException()
        {
            throw new UnreachableException();
        }
    }
}
