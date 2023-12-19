using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace NVorbis
{
    internal static class Vector128Helper
    {
        // TODO: AdvSimd

        public static bool IsSupported => Vector128.IsHardwareAccelerated && Sse.IsSupported;

        public static bool IsAcceleratedGather => IsSupported && Avx2.IsSupported;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<T> LoadUnsafe<T>(ref T source, int elementOffset)
            where T : struct
        {
            return Vector128.LoadUnsafe(ref source, (nuint)elementOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreUnsafe<T>(this Vector128<T> source, ref T destination, int elementOffset)
            where T : struct
        {
            Vector128.StoreUnsafe(source, ref destination, (nuint)elementOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> ShuffleLower(Vector128<float> left, Vector128<float> right)
        {
            if (Sse.IsSupported)
            {
                return Sse.Shuffle(left, right, 0b01_00_01_00);
            }

            ThrowUnreachableException();
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> ShuffleUpper(Vector128<float> left, Vector128<float> right)
        {
            if (Sse.IsSupported)
            {
                return Sse.Shuffle(left, right, 0b11_10_11_10);
            }

            ThrowUnreachableException();
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> ShuffleInterleave(Vector128<float> left, Vector128<float> right)
        {
            if (Sse.IsSupported)
            {
                return Sse.Shuffle(left, right, 0b11_01_10_00);
            }

            ThrowUnreachableException();
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Vector128<float> Gather(
            float* baseAddress,
            Vector128<int> index,
            [ConstantExpected(Min = 1, Max = 8)] byte scale)
        {
            if (Avx2.IsSupported)
            {
                return Avx2.GatherVector128(baseAddress, index, scale);
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
