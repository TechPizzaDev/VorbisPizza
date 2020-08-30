using System;

namespace NVorbis.Contracts
{
    internal interface IResidue
    {
        void Init(IPacket packet, int channels, ICodebook[] codebooks);
        void Decode(IPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, float[][] buffer);
    }
}
