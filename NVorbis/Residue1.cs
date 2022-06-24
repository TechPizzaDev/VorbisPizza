
namespace NVorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    class Residue1 : Residue0
    {
        public Residue1(DataPacket packet, int channels, Codebook[] codebooks) : base(packet, channels, codebooks)
        {
        }

        protected override bool WriteVectors(
            Codebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            float[] res = residue[channel];

            for (int i = 0; i < partitionSize;)
            {
                int entry = codebook.DecodeScalar(packet);
                if (entry == -1)
                {
                    return true;
                }

                System.ReadOnlySpan<float> lookup = codebook.GetLookup(entry);
                for (int j = 0; j < lookup.Length; i++, j++)
                {
                    res[offset + i] += lookup[j];
                }
            }

            return false;
        }
    }
}
