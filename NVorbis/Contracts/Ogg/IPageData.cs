using System;

namespace NVorbis.Contracts.Ogg
{
    internal interface IPageData : IPageReader
    {
        long PageOffset { get; }
        int StreamSerial { get; }
        int SequenceNumber { get; }
        PageFlags PageFlags { get; }
        long GranulePosition { get; }
        ushort PacketCount { get; }
        bool? IsResync { get; }
        bool IsContinued { get; }
        int PageOverhead { get; }

        ArraySegment<byte>[] GetPackets();
    }
}
