using System;

namespace NVorbis.Contracts.Ogg
{
    internal interface IStreamPageReader
    {
        IPacketProvider PacketProvider { get; }

        void AddPage();

        ArraySegment<byte>[] GetPagePackets(uint pageIndex);

        uint FindPage(long granulePos);

        bool GetPage(uint pageIndex, out long granulePos, out bool isResync, out bool isContinuation, out bool isContinued, out uint packetCount, out int pageOverhead);

        void SetEndOfStream();

        uint PageCount { get; }

        bool HasAllPages { get; }

        long? MaxGranulePosition { get; }

        uint FirstDataPageIndex { get; }
    }
}
