using System;

namespace NVorbis.Contracts.Ogg
{
    internal interface IStreamPageReader
    {
        IPacketProvider PacketProvider { get; }

        void AddPage();

        ArraySegment<byte>[] GetPagePackets(ulong pageIndex);

        ulong FindPage(long granulePos);

        bool GetPage(ulong pageIndex, out long granulePos, out bool isResync, out bool isContinuation, out bool isContinued, out uint packetCount, out int pageOverhead);

        void SetEndOfStream();

        ulong PageCount { get; }

        bool HasAllPages { get; }

        long? MaxGranulePosition { get; }

        ulong FirstDataPageIndex { get; }
    }
}
