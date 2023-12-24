using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace NVorbis
{
    // all channels in one pass, interleaved
    internal sealed class Residue2 : Residue0
    {
        private int _channels;

        public Residue2(ref VorbisPacket packet, int channels, Codebook[] codebooks) : base(ref packet, 1, codebooks)
        {
            _channels = channels;
        }

        public override void Decode(
            ref VorbisPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, ReadOnlySpan<float[]> buffers)
        {
            // since we're doing all channels in a single pass, the block size has to be multiplied.
            // otherwise this is just a pass-through call
            base.Decode(ref packet, doNotDecodeChannel, blockSize * _channels, buffers);
        }

        protected override bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, ReadOnlySpan<float[]> residues, int channel, int offset, int partitionSize)
        {
            uint dimensions = (uint) codebook.Dimensions;
            uint channels = (uint) _channels;
            Debug.Assert(residues.Length == _channels);

            if (dimensions != 1 && channels == 2)
            {
                return WriteVectors<WriteVectorStereo>(codebook, ref packet, residues, offset, partitionSize);
            }
            else if (channels == 1)
            {
                return WriteVectors<WriteVectorMono>(codebook, ref packet, residues, offset, partitionSize);
            }
            else
            {
                return WriteVectors<WriteVectorFallback>(codebook, ref packet, residues, offset, partitionSize);
            }
        }

        private bool WriteVectors<TState>(
            Codebook codebook, ref VorbisPacket packet, ReadOnlySpan<float[]> residues, int offset, int partitionSize)
            where TState : IWriteVectorState
        {
            uint dimensions = (uint) codebook.Dimensions;
            uint channels = (uint) _channels;
            uint o = (uint) offset / channels;

            ref float lookupTable = ref MemoryMarshal.GetReference(codebook.GetLookupTable());

            for (uint c = 0; c < partitionSize; c += dimensions)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                ref float lookup = ref Unsafe.Add(ref lookupTable, (uint) entry * dimensions);
                TState.Invoke(residues, ref lookup, dimensions, ref o);
            }
            return false;
        }

        private struct WriteVectorStereo : IWriteVectorState
        {
            public static void Invoke(ReadOnlySpan<float[]> residues, ref float lookup, uint dimensions, ref uint o)
            {
                ref float res0 = ref MemoryMarshal.GetArrayDataReference(residues[0]);
                ref float res1 = ref MemoryMarshal.GetArrayDataReference(residues[1]);

                uint d = 0;

                if (Vector128.IsHardwareAccelerated)
                {
                    for (; d + 8 <= dimensions; d += 8, o += 4)
                    {
                        Vector128<float> lookup0 = Vector128.LoadUnsafe(ref lookup, d + 0);
                        Vector128<float> lookup1 = Vector128.LoadUnsafe(ref lookup, d + 4);

                        Vector128<float> vres0 = Vector128.LoadUnsafe(ref res0, o);
                        Vector128<float> vres1 = Vector128.LoadUnsafe(ref res1, o);

                        Vector128<float> sum0 = vres0 + Vector128Helper.UnpackLow(lookup0, lookup1);
                        Vector128<float> sum1 = vres1 + Vector128Helper.UnpackHigh(lookup0, lookup1);

                        sum0.StoreUnsafe(ref res0, o);
                        sum1.StoreUnsafe(ref res1, o);
                    }
                }

                for (; d < dimensions; d += 2, o++)
                {
                    Unsafe.Add(ref res0, o) += Unsafe.Add(ref lookup, d + 0);
                    Unsafe.Add(ref res1, o) += Unsafe.Add(ref lookup, d + 1);
                }
            }
        }

        private struct WriteVectorMono : IWriteVectorState
        {
            public static void Invoke(ReadOnlySpan<float[]> residues, ref float lookup, uint dimensions, ref uint o)
            {
                ref float res0 = ref MemoryMarshal.GetArrayDataReference(residues[0]);

                for (uint d = 0; d < dimensions; d += 1, o++)
                {
                    Unsafe.Add(ref res0, o) += Unsafe.Add(ref lookup, d);
                }
            }
        }

        private struct WriteVectorFallback : IWriteVectorState
        {
            public static void Invoke(ReadOnlySpan<float[]> residues, ref float lookup, uint dimensions, ref uint o)
            {
                for (uint d = 0; d < dimensions; d += (uint) residues.Length, o++)
                {
                    for (int ch = 0; ch < residues.Length; ch++)
                    {
                        residues[ch][o] += Unsafe.Add(ref lookup, d + (uint) ch);
                    }
                }
            }
        }

        private interface IWriteVectorState
        {
            static abstract void Invoke(ReadOnlySpan<float[]> residues, ref float lookup, uint dimensions, ref uint o);
        }
    }
}
