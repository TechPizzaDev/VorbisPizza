using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NVorbis.Contracts;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class ForwardOnlyPacketProvider : IForwardOnlyPacketProvider
    {
        private int _lastSeqNo;
        private readonly Queue<(PageData page, bool isResync)> _pageQueue = new();

        private readonly IPageReader _reader;
        private int _packetIndex;
        private bool _isEndOfStream;

        private bool _isPacketFinished;

        private List<PageSlice> _packetPages = new();
        private PacketData[] _packetParts = Array.Empty<PacketData>();
        private bool _isDisposed;

        public ForwardOnlyPacketProvider(IPageReader reader, int streamSerial)
        {
            _reader = reader;
            StreamSerial = streamSerial;

            // force the first page to read
            _packetIndex = int.MaxValue;
            _isPacketFinished = true;
        }

        public bool CanSeek => false;

        public int StreamSerial { get; }

        public bool AddPage(PageData pageData)
        {
            ReadOnlySpan<byte> pageSpan = pageData.AsSpan();
            PageHeader header = new(pageSpan);
            bool isResync = pageData.IsResync;

            if ((header.PageFlags & PageFlags.BeginningOfStream) != 0)
            {
                if (_isEndOfStream)
                {
                    pageData.DecrementRef();
                    return false;
                }
                isResync = true;
                _lastSeqNo = header.SequenceNumber;
            }
            else
            {
                // check the sequence number
                int seqNo = header.SequenceNumber;
                isResync |= seqNo != _lastSeqNo + 1;
                _lastSeqNo = seqNo;
            }

            // there must be at least one packet with data
            int ttl = 0;
            int segmentCount = header.SegmentCount;
            for (int i = 0; i < segmentCount; i++)
            {
                ttl += pageSpan[27 + i];
            }
            if (ttl == 0)
            {
                pageData.DecrementRef();
                return false;
            }

            _pageQueue.Enqueue((pageData, isResync));

            return true;
        }

        public void SetEndOfStream()
        {
            _isEndOfStream = true;
        }

        public VorbisPacket GetNextPacket()
        {
            if (!_isPacketFinished)
            {
                throw new InvalidOperationException("The previous packet has not been finished.");
            }

            // if we don't already have a page, grab it
            PageData? pageData;
            bool isResync;
            int dataStart;
            int packetIndex;
            bool isCont, isCntd;

            if (_packetPages.Count > 0 &&
                _packetIndex < _packetPages[^1].Page.Header.PageOverhead)
            {
                PageSlice lastSlice = _packetPages[^1];
                pageData = lastSlice.Page;
                isResync = false;
                dataStart = lastSlice.Start + lastSlice.Length;
                packetIndex = _packetIndex;
                isCont = false;
                isCntd = pageData.AsSpan()[pageData.Header.PageOverhead - 1] == 255;

                // Prevent the page from being disposed.
                _packetPages.RemoveAt(_packetPages.Count - 1);
            }
            else
            {
                if (!ReadNextPage(out pageData, out isResync, out dataStart, out packetIndex, out isCont, out isCntd))
                {
                    // couldn't read the next page...
                    return default;
                }
            }

            ClearPacketPages();

            ReadOnlySpan<byte> pageSpan = pageData.AsSpan();
            PageHeader header = new(pageSpan);
            int pageOverhead = header.PageOverhead;

            // first, set flags from the start page
            int contOverhead = dataStart;
            bool isFirst = packetIndex == 27;
            if (isCont)
            {
                if (isFirst)
                {
                    // if it's a continuation, we just read it for a new packet and there's a continuity problem
                    isResync = true;

                    // skip the first packet; it's a partial
                    contOverhead += GetPacketLength(pageSpan, ref packetIndex);

                    // if we moved to the end of the page, we can't satisfy the request from here...
                    if (packetIndex == pageOverhead)
                    {
                        // ... so we'll just recurse and try again
                        pageData.DecrementRef();
                        return GetNextPacket();
                    }
                }
            }
            if (!isFirst)
            {
                contOverhead = 0;
            }

            // second, determine how long the packet is
            int dataLen = GetPacketLength(pageSpan, ref packetIndex);
            _packetPages.Add(new PageSlice(pageData, dataStart, dataLen));

            // third, determine if the packet is the last one in the page
            bool isLast = packetIndex == pageOverhead;
            if (isCntd)
            {
                if (isLast)
                {
                    // we're on the continued packet, so it really counts with the next page
                    isLast = false;
                }
                else
                {
                    // whelp, not quite...  gotta account for the continued packet
                    int pi = packetIndex;
                    GetPacketLength(pageData.AsSpan(), ref pi);
                    isLast = pi == pageOverhead;
                }
            }

            // forth, if it is the last one, process continuations or flags & granulePos
            bool isEos = false;
            long granulePos = -1;
            if (isLast)
            {
                granulePos = header.GranulePosition;

                // fifth, set flags from the end page
                if ((header.PageFlags & PageFlags.EndOfStream) != 0 || (_isEndOfStream && _pageQueue.Count == 0))
                {
                    isEos = true;
                }
            }
            else
            {
                while (isCntd && packetIndex == pageData.Header.PageOverhead)
                {
                    if (!ReadNextPage(out pageData, out isResync, out dataStart, out packetIndex, out isCont, out isCntd)
                        || isResync || !isCont)
                    {
                        // just use what data we can...
                        break;
                    }

                    // we're in the right spot!
                    ReadOnlySpan<byte> span = pageData.AsSpan();

                    // update the overhead count
                    contOverhead += new PageHeader(span).PageOverhead;

                    // get the size of this page's portion
                    int contSz = GetPacketLength(span, ref packetIndex);

                    _packetPages.Add(new PageSlice(pageData, dataStart, contSz));
                }
            }

            // last, save off our state and return true
            ArraySegment<PacketData> packetParts = GetPacketParts(_packetPages.Count);

            VorbisPacket packet = new(this, packetParts)
            {
                IsResync = isResync,
                GranulePosition = granulePos,
                IsEndOfStream = isEos,
                ContainerOverheadBits = contOverhead * 8
            };

            _packetIndex = packetIndex;
            _isEndOfStream |= isEos;
            _isPacketFinished = false;

            return packet;
        }

        private bool ReadNextPage(
            [MaybeNullWhen(false)] out PageData pageData,
            out bool isResync, out int dataStart, out int packetIndex, out bool isContinuation, out bool isContinued)
        {
            while (_pageQueue.Count == 0)
            {
                // Don't do anything with the read pages,
                // they will be handled by AddPage.
                if (_isEndOfStream || !_reader.ReadNextPage(out _))
                {
                    // we must be done
                    pageData = null;
                    isResync = false;
                    dataStart = 0;
                    packetIndex = 0;
                    isContinuation = false;
                    isContinued = false;
                    return false;
                }
            }

            (PageData page, bool isResync) temp = _pageQueue.Dequeue();
            pageData = temp.page;
            isResync = temp.isResync;

            ReadOnlySpan<byte> pageSpan = pageData.AsSpan();
            PageHeader header = new(pageSpan);
            dataStart = header.PageOverhead;
            packetIndex = 27;
            isContinuation = (header.PageFlags & PageFlags.ContinuesPacket) != 0;
            isContinued = pageSpan[dataStart - 1] == 255;
            return true;
        }

        private ArraySegment<PacketData> GetPacketParts(int count)
        {
            if (_packetParts.Length < count)
            {
                int previousLength = _packetParts.Length;
                Array.Resize(ref _packetParts, (count + 3) / 4 * 4);

                for (int i = previousLength; i < _packetParts.Length; i++)
                {
                    _packetParts[i] = new PacketData(new PacketLocation(i, 0));
                }
            }
            return new ArraySegment<PacketData>(_packetParts, 0, count);
        }

        private static int GetPacketLength(ReadOnlySpan<byte> pageData, ref int packetIndex)
        {
            int len = 0;
            while (pageData[packetIndex] == 255 && packetIndex < pageData[26] + 27)
            {
                len += pageData[packetIndex];
                ++packetIndex;
            }
            if (packetIndex < pageData[26] + 27)
            {
                len += pageData[packetIndex];
                ++packetIndex;
            }
            return len;
        }

        public PageSlice GetPacketData(PacketLocation location)
        {
            ulong packetIndex = location.PageIndex;
            if (packetIndex < (ulong)_packetPages.Count)
            {
                PageSlice slice = _packetPages[(int)packetIndex];
                slice.Page.IncrementRef();
                return slice;
            }
            return default;
        }

        public void FinishPacket(ref VorbisPacket packet)
        {
            _isPacketFinished = true;
        }

        private void ClearPacketPages()
        {
            for (int i = 0; i < _packetPages.Count; i++)
            {
                _packetPages[i].Page.DecrementRef();
            }
            _packetPages.Clear();
        }

        long IPacketProvider.GetGranuleCount()
        {
            throw new NotSupportedException();
        }

        long IPacketProvider.SeekTo(long granulePos, uint preRoll, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            throw new NotSupportedException();
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    while (_pageQueue.TryDequeue(out (PageData page, bool isResync) item))
                    {
                        item.page.DecrementRef();
                    }

                    ClearPacketPages();
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
