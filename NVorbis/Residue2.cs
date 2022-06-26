using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis
{
    // all channels in one pass, interleaved
    internal class Residue2 : Residue0
    {
        private int _channels;

        public Residue2(DataPacket packet, int channels, Codebook[] codebooks) : base(packet, 1, codebooks)
        {
            _channels = channels;
        }

        public override void Decode(
            DataPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, float[][] buffer)
        {
            // since we're doing all channels in a single pass, the block size has to be multiplied.
            // otherwise this is just a pass-through call
            base.Decode(packet, doNotDecodeChannel, blockSize * _channels, buffer);
        }

        protected override unsafe bool WriteVectors(
            Codebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            uint dimensions = (uint)codebook.Dimensions;
            uint ch = 0;
            uint channels = (uint)_channels;
            uint o = (uint)offset / channels;

            fixed (float* res0 = residue[0])
            fixed (float* res1 = residue.Length > 1 ? residue[1] : Array.Empty<float>())
            fixed (float* lookupTable = codebook.GetLookupTable())
            {
                for (uint c = 0; c < partitionSize; c += dimensions)
                {
                    int entry = codebook.DecodeScalar(packet);
                    if (entry == -1)
                    {
                        return true;
                    }

                    float* lookup = lookupTable + (uint)entry * dimensions;
                    if (dimensions != 1 && channels == 2)
                    {
                        for (uint d = 0; d < dimensions; d += 2, o++)
                        {
                            res0[o] += lookup[d + 0];
                            res1[o] += lookup[d + 1];
                        }
                    }
                    else if (channels == 1)
                    {
                        for (uint d = 0; d < dimensions; d++, o++)
                        {
                            res0[o] += lookup[d];
                        }
                    }
                    else
                    {
                        for (uint d = 0; d < dimensions; d++)
                        {
                            residue[ch][o] += lookup[d];
                            if (++ch == channels)
                            {
                                ch = 0;
                                o++;
                            }
                        }
                    }
                }
                return false;
            }
        }
    }
}
