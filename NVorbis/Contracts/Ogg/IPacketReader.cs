using System;

namespace NVorbis.Contracts.Ogg
{
    internal interface IPacketReader
    {
        Memory<byte> GetPacketData(int pagePacketIndex);

        void InvalidatePacketCache(IPacket packet);
    }
}
