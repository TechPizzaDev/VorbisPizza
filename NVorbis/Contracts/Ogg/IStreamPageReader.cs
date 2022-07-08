using System;
using NVorbis.Ogg;

namespace NVorbis.Contracts.Ogg
{
    internal interface IStreamPageReader : IDisposable
    {
        IPacketProvider PacketProvider { get; }

        void AddPage(PageData page, long pageOffset);

        ArraySegment<byte>[] GetPagePackets(ulong pageIndex);

        ulong FindPage(long granulePos);

        bool GetPage(
            ulong pageIndex, 
            out long granulePos,
            out bool isResync,
            out bool isContinuation, 
            out bool isContinued, 
            out ushort packetCount, 
            out int pageOverhead);

        void SetEndOfStream();

        ulong PageCount { get; }

        bool HasAllPages { get; }

        long? MaxGranulePosition { get; }

        ulong FirstDataPageIndex { get; }
    }
}
