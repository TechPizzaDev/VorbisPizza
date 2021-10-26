using System;

namespace NVorbis.Contracts
{
    interface IResidue
    {
        void Init(DataPacket packet, int channels, Codebook[] codebooks);
        void Decode(DataPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, float[][] buffer);
    }
}
