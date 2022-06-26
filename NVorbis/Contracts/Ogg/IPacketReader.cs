using System;

namespace NVorbis.Contracts.Ogg
{
    internal interface IPacketReader
    {
        ArraySegment<byte> GetPacketData(PacketDataPart dataPart);

        void InvalidatePacketCache(in VorbisPacket packet);
    }
}
