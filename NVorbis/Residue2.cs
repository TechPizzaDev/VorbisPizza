using System;

namespace NVorbis
{
    // all channels in one pass, interleaved
    class Residue2 : Residue0
    {
        int _channels;

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

        protected override bool WriteVectors(
            Codebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            nint ch = 0;
            nint channels = _channels;
            nint o = offset / channels;
            for (int c = 0; c < partitionSize;)
            {
                var entry = codebook.DecodeScalar(packet);
                if (entry == -1)
                {
                    return true;
                }

                var lookup = codebook.GetLookup(entry);
                for (var d = 0; d < lookup.Length; d++, c++)
                {
                    residue[ch][o] += lookup[d];
                    if (++ch == channels)
                    {
                        ch = 0;
                        o++;
                    }
                }
            }

            return false;
        }
    }
}
