using NVorbis.Contracts;

namespace NVorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    class Residue1 : Residue0
    {
        protected override bool WriteVectors(Codebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            var res = residue[channel];

            for (int i = 0; i < partitionSize;)
            {
                var entry = codebook.DecodeScalar(packet);
                if (entry == -1)
                {
                    return true;
                }

                var lookup = codebook.GetLookup(entry);
                for (int j = 0; j < lookup.Length; i++, j++)
                {
                    res[offset + i] += lookup[j];
                }
            }

            return false;
        }
    }
}
