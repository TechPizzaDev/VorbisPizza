using System;
using System.Collections.Generic;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class StreamPageReader : IStreamPageReader
    {
        private readonly IPageData _reader;
        private readonly List<long> _pageOffsets = new();

        private int _lastSeqNbr;
        private ulong? _firstDataPageIndex;
        private long _maxGranulePos;

        private ulong _lastPageIndex = PacketDataPart.MaxPageIndex;
        private long _lastPageGranulePos;
        private bool _lastPageIsResync;
        private bool _lastPageIsContinuation;
        private bool _lastPageIsContinued;
        private uint _lastPagePacketCount;
        private int _lastPageOverhead;

        private ArraySegment<byte>[]? _cachedPagePackets;

        public Contracts.IPacketProvider PacketProvider { get; private set; }

        public StreamPageReader(IPageData pageReader, int streamSerial)
        {
            _reader = pageReader;

            // The packet provider has a reference to us, and we have a reference to it.
            // The page reader has a reference to us.
            // The container reader has a _weak_ reference to the packet provider.
            // The user has a reference to the packet provider.
            // So long as the user doesn't drop their reference and the page reader doesn't drop us,
            //  the packet provider will stay alive.
            // This is important since the container reader only holds a week reference to it.
            PacketProvider = new PacketProvider(this, streamSerial);
        }

        public void AddPage()
        {
            // verify we haven't read all pages
            if (!HasAllPages)
            {
                // verify the new page's flags

                // if the page's granule position is 0 or less it doesn't have any sample
                if (_reader.GranulePosition != -1)
                {
                    if (_firstDataPageIndex == null && _reader.GranulePosition > 0)
                    {
                        _firstDataPageIndex = (uint)_pageOffsets.Count;
                    }
                    else if (_maxGranulePos > _reader.GranulePosition)
                    {
                        // uuuuh, what?!
                        throw new System.IO.InvalidDataException("Granule Position regressed?!");
                    }
                    _maxGranulePos = _reader.GranulePosition;
                }
                // granule position == -1, so this page doesn't complete any packets
                // we don't really care if it's a continuation itself, only that it is continued and has a single packet
                else if (_firstDataPageIndex.HasValue && (!_reader.IsContinued || _reader.PacketCount != 1))
                {
                    throw new System.IO.InvalidDataException("Granule Position was -1 but page does not have exactly 1 continued packet.");
                }

                if ((_reader.PageFlags & PageFlags.EndOfStream) != 0)
                {
                    HasAllPages = true;
                }

                if (_reader.IsResync.GetValueOrDefault() || (_lastSeqNbr != 0 && _lastSeqNbr + 1 != _reader.SequenceNumber))
                {
                    // as a practical matter, if the sequence numbers are "wrong",
                    // our logical stream is now out of sync so whether the page header sync was lost
                    // or we just got an out of order page / sequence jump, we're counting it as a resync
                    _pageOffsets.Add(-_reader.PageOffset);
                }
                else
                {
                    _pageOffsets.Add(_reader.PageOffset);
                }

                _lastSeqNbr = _reader.SequenceNumber;
            }
        }

        public ArraySegment<byte>[] GetPagePackets(ulong pageIndex)
        {
            if (_cachedPagePackets != null && _lastPageIndex == pageIndex)
            {
                return _cachedPagePackets;
            }

            long pageOffset = _pageOffsets[(int)pageIndex];
            if (pageOffset < 0)
            {
                pageOffset = -pageOffset;
            }

            _reader.Lock();
            try
            {
                _reader.ReadPageAt(pageOffset);
                ArraySegment<byte>[] packets = _reader.GetPackets();
                if (pageIndex == _lastPageIndex)
                {
                    _cachedPagePackets = packets;
                }
                return packets;
            }
            finally
            {
                _reader.Release();
            }
        }

        public ulong FindPage(long granulePos)
        {
            // if we're being asked for the first granule, just grab the very first data page
            ulong pageIndex = ulong.MaxValue;
            if (granulePos == 0)
            {
                pageIndex = FindFirstDataPage();
            }
            else
            {
                // start by looking at the last read page's position...
                uint lastPageIndex = (uint)(_pageOffsets.Count - 1);
                if (GetPageRaw(lastPageIndex, out long pageGP))
                {
                    // most likely, we can look at previous pages for the appropriate one...
                    if (granulePos < pageGP)
                    {
                        pageIndex = FindPageBisection(granulePos, FindFirstDataPage(), lastPageIndex, pageGP);
                    }
                    // unless we're seeking forward, which is merely an excercise in reading forward...
                    else if (granulePos > pageGP)
                    {
                        pageIndex = FindPageForward(lastPageIndex, pageGP, granulePos);
                    }
                    // but of course, it's possible (though highly unlikely) that the last read page ended on the granule we're looking for.
                    else
                    {
                        pageIndex = lastPageIndex + 1;
                    }
                }
            }
            if (pageIndex == ulong.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(granulePos));
            }
            return pageIndex;
        }

        private ulong FindFirstDataPage()
        {
            while (!_firstDataPageIndex.HasValue)
            {
                if (!GetPageRaw((ulong)_pageOffsets.Count, out _))
                {
                    return uint.MaxValue;
                }
            }
            return _firstDataPageIndex.Value;
        }

        private uint FindPageForward(uint pageIndex, long pageGranulePos, long granulePos)
        {
            while (pageGranulePos <= granulePos)
            {
                if (++pageIndex == _pageOffsets.Count)
                {
                    if (!GetNextPageGranulePos(out pageGranulePos))
                    {
                        // if we couldn't get a page because we're EOS, allow finding the last granulePos
                        if (MaxGranulePosition < granulePos)
                        {
                            pageIndex = uint.MaxValue;
                        }
                        break;
                    }
                }
                else
                {
                    if (!GetPageRaw(pageIndex, out pageGranulePos))
                    {
                        pageIndex = uint.MaxValue;
                        break;
                    }
                }
            }
            return pageIndex;
        }

        private bool GetNextPageGranulePos(out long granulePos)
        {
            int pageCount = _pageOffsets.Count;
            while (pageCount == _pageOffsets.Count && !HasAllPages)
            {
                _reader.Lock();
                try
                {
                    if (!_reader.ReadNextPage())
                    {
                        HasAllPages = true;
                        continue;
                    }

                    if (pageCount < _pageOffsets.Count)
                    {
                        granulePos = _reader.GranulePosition;
                        return true;
                    }
                }
                finally
                {
                    _reader.Release();
                }
            }
            granulePos = 0;
            return false;
        }

        private ulong FindPageBisection(long granulePos, ulong low, ulong high, long highGranulePos)
        {
            // we can treat low as always being before the first sample; later work will correct that if needed
            long lowGranulePos = 0L;
            ulong dist;
            while ((dist = high - low) > 0)
            {
                // try to find the right page by assumming they are all about the same size
                ulong index = low + (ulong)(dist * ((granulePos - lowGranulePos) / (double)(highGranulePos - lowGranulePos)));

                // go get the actual position of the selected page
                if (!GetPageRaw(index, out long idxGranulePos))
                {
                    return ulong.MaxValue;
                }

                // figure out where to go from here
                if (idxGranulePos > granulePos)
                {
                    // we read a page after our target (could be the right one, but we don't know yet)
                    high = index;
                    highGranulePos = idxGranulePos;
                }
                else if (idxGranulePos < granulePos)
                {
                    // we read a page before our target
                    low = index + 1;
                    lowGranulePos = idxGranulePos + 1;
                }
                else
                {
                    // direct hit
                    return index + 1;
                }
            }
            return low;
        }

        private bool GetPageRaw(ulong pageIndex, out long pageGranulePos)
        {
            long offset = _pageOffsets[(int)pageIndex];
            if (offset < 0)
            {
                offset = -offset;
            }

            _reader.Lock();
            try
            {
                if (_reader.ReadPageAt(offset))
                {
                    pageGranulePos = _reader.GranulePosition;
                    return true;
                }
                pageGranulePos = 0;
                return false;
            }
            finally
            {
                _reader.Release();
            }
        }

        public bool GetPage(
            ulong pageIndex, out long granulePos, out bool isResync, out bool isContinuation, out bool isContinued,
            out uint packetCount, out int pageOverhead)
        {
            if (_lastPageIndex == pageIndex)
            {
                granulePos = _lastPageGranulePos;
                isResync = _lastPageIsResync;
                isContinuation = _lastPageIsContinuation;
                isContinued = _lastPageIsContinued;
                packetCount = _lastPagePacketCount;
                pageOverhead = _lastPageOverhead;
                return true;
            }

            _reader.Lock();
            try
            {
                while (pageIndex >= (ulong)_pageOffsets.Count && !HasAllPages)
                {
                    if (_reader.ReadNextPage())
                    {
                        // if we found our page, return it from here so we don't have to do further processing
                        if (pageIndex < (ulong)_pageOffsets.Count)
                        {
                            isResync = _reader.IsResync.GetValueOrDefault();
                            ReadPageData(pageIndex, out granulePos, out isContinuation, out isContinued, out packetCount, out pageOverhead);
                            return true;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                _reader.Release();
            }

            if (pageIndex < (ulong)_pageOffsets.Count)
            {
                long offset = _pageOffsets[(int)pageIndex];
                if (offset < 0)
                {
                    isResync = true;
                    offset = -offset;
                }
                else
                {
                    isResync = false;
                }

                _reader.Lock();
                try
                {
                    if (_reader.ReadPageAt(offset))
                    {
                        _lastPageIsResync = isResync;
                        ReadPageData(pageIndex, out granulePos, out isContinuation, out isContinued, out packetCount, out pageOverhead);
                        return true;
                    }
                }
                finally
                {
                    _reader.Release();
                }
            }

            granulePos = 0;
            isResync = false;
            isContinuation = false;
            isContinued = false;
            packetCount = 0;
            pageOverhead = 0;
            return false;
        }

        private void ReadPageData(
            ulong pageIndex, out long granulePos, out bool isContinuation, out bool isContinued, out uint packetCount, out int pageOverhead)
        {
            _cachedPagePackets = null;
            _lastPageGranulePos = granulePos = _reader.GranulePosition;
            _lastPageIsContinuation = isContinuation = (_reader.PageFlags & PageFlags.ContinuesPacket) != 0;
            _lastPageIsContinued = isContinued = _reader.IsContinued;
            _lastPagePacketCount = packetCount = _reader.PacketCount;
            _lastPageOverhead = pageOverhead = _reader.PageOverhead;
            _lastPageIndex = pageIndex;
        }

        public void SetEndOfStream()
        {
            HasAllPages = true;
        }

        public ulong PageCount => (uint)_pageOffsets.Count;

        public bool HasAllPages { get; private set; }

        public long? MaxGranulePosition => HasAllPages ? _maxGranulePos : null;

        public ulong FirstDataPageIndex => FindFirstDataPage();
    }
}
