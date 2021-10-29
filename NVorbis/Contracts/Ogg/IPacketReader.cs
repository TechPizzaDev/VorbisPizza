using System;

namespace NVorbis.Contracts.Ogg
{
    interface IPacketReader
    {
        Memory<byte> GetPacketData(PacketDataPart dataPart);

        void InvalidatePacketCache(DataPacket packet);
    }
}
