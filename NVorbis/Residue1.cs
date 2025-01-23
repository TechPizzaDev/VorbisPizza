using System;

namespace NVorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    internal class Residue1 : Residue0
    {
        public Residue1(ref VorbisPacket packet, Codebook[] codebooks) : base(ref packet, codebooks)
        {
        }

        protected override bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, Span<float> channelBuf, int offset, int partitionSize)
        {
            for (int i = 0; i < partitionSize;)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                ReadOnlySpan<float> lookup = codebook.GetLookup(entry);
                Span<float> res = channelBuf.Slice(offset + i, lookup.Length);

                for (int j = 0; j < lookup.Length; j++)
                {
                    res[j] += lookup[j];
                }
                i += lookup.Length;
            }

            return false;
        }
    }
}
