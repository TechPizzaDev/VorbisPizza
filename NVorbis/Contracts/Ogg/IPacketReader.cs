using System;

namespace NVorbis.Contracts.Ogg
{
    interface IPacketReader
    {
        ArraySegment<byte> GetPacketData(PacketDataPart dataPart);

        void InvalidatePacketCache(DataPacket packet);
    }
}
