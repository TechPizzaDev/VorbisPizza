using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NVorbis.Ogg
{
    internal class LightOggPacketProvider : IVorbisPacketProvider
    {
        private List<long> _pageOffsets = new List<long>();
        private List<long> _pageGranules = new List<long>();
        private List<short> _pagePacketCounts = new List<short>();
        private List<bool> _pageContinuations = new List<bool>();
        private Dictionary<int, int> _packetGranuleCounts = new Dictionary<int, int>();
        private Dictionary<int, long> _packetGranulePositions = new Dictionary<int, long>();
        
        private LightOggPageReader _reader;
        private int _lastSeqNbr;
        private bool _isEndOfStream;
        private int _packetIndex;
        private int _packetCount;
        private LightOggPacket? _lastPacket;
        private List<(long DataOffset, int Size)> _cachedSegments;
        private int _cachedPageSeqNo;
        private bool _cachedIsResync;
        private bool _cachedLastContinues;
        private int? _cachedPageIndex;

        public int StreamSerial { get; }

        public bool CanSeek => true;
        public long ContainerBits => _reader?.ContainerBits ?? 0;

        public event ParameterChangeEvent? ParameterChange;

        internal LightOggPacketProvider(LightOggPageReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));

            StreamSerial = _reader.StreamSerial;

            AddPage();
        }

        internal void AddPage()
        {
            // The Ogg spec states that partial packets are counted on their _ending_ page.
            // We count them on their _starting_ page.
            // This makes it simpler to read them in GetPacket(int).
            // As a practical matter, our storage is opaque so it doesn't matter how we count them,
            // as long as our indexing works out the same as the encoder's.

            // verify we're not already ended (_isEndOfStream)
            if (!_isEndOfStream)
            {
                // verify the new page's flags
                var isCont = false;
                var eos = false;
                if (_reader.PageFlags != OggPageFlags.None)
                {
                    isCont = (_reader.PageFlags & OggPageFlags.ContinuesPacket) != 0;
                    if ((_reader.PageFlags & OggPageFlags.BeginningOfStream) != 0)
                    {
                        isCont = false;

                        // if we're not at the beginning of the stream, something is wrong
                        // BUT, I'm not sure it really matters...  just ignore the issue for now
                    }

                    if ((_reader.PageFlags & OggPageFlags.EndOfStream) != 0)
                        eos = true;
                }

                if (_reader.IsResync ||
                    (_lastSeqNbr != 0 && _lastSeqNbr + 1 != _reader.SequenceNumber))
                {
                    // as a practical matter, if the sequence numbers are "wrong", 
                    // our logical stream is now out of sync
                    // so whether the page header sync was lost or we just got an 
                    // out of order page / sequence jump, we're counting it as a resync
                    _pageOffsets.Add(-_reader.PageOffset);
                }
                else
                {
                    _pageOffsets.Add(_reader.PageOffset);
                }

                short packetCount = _reader.PacketCount;
                if (isCont)
                    --packetCount;

                _pageGranules.Add(_reader.GranulePosition);
                _pagePacketCounts.Add(packetCount);
                _pageContinuations.Add(isCont && !_reader.IsResync);
                _lastSeqNbr = _reader.SequenceNumber;

                _packetCount += packetCount;

                _isEndOfStream |= eos;
            }
        }

        internal void SetPacketGranuleInfo(int index, int granuleCount, long granulePos)
        {
            _packetGranuleCounts[index] = granuleCount;
            if (granulePos > 0)
                _packetGranulePositions[index] = granulePos;
        }

        public long GetGranuleCount()
        {
            if (_reader == null)
                throw new ObjectDisposedException(GetType().FullName);

            _reader.Lock();
            _reader.ReadAllPages();
            _reader.Release();

            return _pageGranules[_pageGranules.Count - 1];
        }

        public int GetTotalPageCount()
        {
            if (_reader == null)
                throw new ObjectDisposedException(GetType().FullName);

            _reader.Lock();
            _reader.ReadAllPages();
            _reader.Release();

            return _pageOffsets.Count;
        }

        public VorbisDataPacket? FindPacket(
            long granulePos,
            Func<VorbisDataPacket, VorbisDataPacket, int> packetGranuleCountCallback)
        {
            if (_reader == null)
                throw new ObjectDisposedException(GetType().FullName);

            // look for the page that contains the granulePos requested, then
            int pageIndex = 0;
            int packetIndex = 0;
            while (
                pageIndex < _pageGranules.Count &&
                _pageGranules[pageIndex] < granulePos &&
                !_isEndOfStream)
            {
                packetIndex += _pagePacketCounts[pageIndex];
                if (++pageIndex == _pageGranules.Count)
                {
                    if (!GetNextPage())
                        // couldn't find it
                        return null;
                }
            }

            // look for the packet that contains the granulePos requested
            var packet = GetPacket(--packetIndex);
            do
            {
                var prvPkt = packet;

                packet = GetPacket(++packetIndex);
                if (packet == null)
                    return null;

                Debug.Assert(prvPkt != null);

                if (!_packetGranuleCounts.ContainsKey(((LightOggPacket)packet).Index))
                    packet.GranuleCount = packetGranuleCountCallback(packet, prvPkt);

                packet.GranulePosition = prvPkt.GranulePosition + packet.GranuleCount!.Value;
                prvPkt.Done();
            }
            while (packet.GranulePosition <= granulePos);

            // if we get to here, that means we found the correct packet
            return packet;
        }

        public VorbisDataPacket? GetNextPacket()
        {
            var pkt = GetPacket(_packetIndex);
            if (pkt != null)
                _packetIndex++;
            return pkt;
        }

        public VorbisDataPacket? PeekNextPacket()
        {
            return GetPacket(_packetIndex);
        }

        public void SeekToPacket(VorbisDataPacket packet, int preRoll)
        {
            if (_reader == null)
                throw new ObjectDisposedException(GetType().FullName);

            // save off the packet index in our DataPacket implementation
            if (preRoll < 0)
                throw new ArgumentOutOfRangeException(nameof(preRoll), "Must be positive or zero!");

            if (!(packet is LightOggPacket lightPacket))
                throw new ArgumentException("Must be a packet from LightContainerReader!", nameof(packet));

            // we can seek back to the first packet, but no further
            _packetIndex = Math.Max(0, lightPacket.Index - preRoll);
        }

        public VorbisDataPacket? GetPacket(int packetIndex)
        {
            if (_reader == null)
                throw new ObjectDisposedException(GetType().FullName);

            // if we're returning the same packet as last call, caller probably wants the same instance
            if (_lastPacket != null && _lastPacket.Index == packetIndex)
                return _lastPacket;

            // figure out which page the requested packet starts on, and which packet it is in the sequence
            var pageIndex = 0;
            var pktIdx = packetIndex;
            while (pageIndex < _pagePacketCounts.Count && pktIdx >= _pagePacketCounts[pageIndex])
            {
                pktIdx -= _pagePacketCounts[pageIndex];
                if (++pageIndex == _pageContinuations.Count && !_isEndOfStream)
                {
                    if (!GetNextPage())
                    {
                        // no more pages
                        _isEndOfStream = true;
                        return null;
                    }
                }
            }

            if (pageIndex == _pagePacketCounts.Count)
                // couldn't find it
                return null;

            // if the found page is a continuation, ignore the first packet (it's the continuation)
            if (_pageContinuations[pageIndex])
                ++pktIdx;

            // get all the packets in the page (including continued / continuations)
            var packets = GetPagePackets(
                pageIndex, out var lastContinues, out var isResync, out var pageSeqNo);

            Debug.Assert(packets != null);

            var packetList = new List<(long, int)>();
            packetList.Add(packets[pktIdx]);

            // if our packet is continued, read in the rest of it from the next page(s)
            var startPageIdx = pageIndex;
            var keepReading = lastContinues;
            while (keepReading && pktIdx >= _pagePacketCounts[pageIndex] - 1)
            {
                if (++pageIndex == _pagePacketCounts.Count)
                {
                    if (_isEndOfStream)
                        // per the spec, a continued packet at the end of the stream should be dropped
                        return null;

                    if (!GetNextPage())
                    {
                        // no more pages
                        _isEndOfStream = true;
                        return null;
                    }
                }

                pktIdx = 0;
                packets = GetPagePackets(
                    pageIndex, out keepReading, out var contResync, out pageSeqNo);

                if (contResync)
                    // if we're in a resync, just return what we could get.
                    break;

                Debug.Assert(packets != null);
                packetList.Add(packets[0]);
            }

            // create the packet instance and populate it with the appropriate initial data
            var packet = new LightOggPacket(_reader, this, packetIndex, packetList)
            {
                PageGranulePosition = _pageGranules[startPageIdx],
                PageSequenceNumber = pageSeqNo,
                IsResync = isResync
            };

            // if we're the last packet completed in the page, set the .GranulePosition
            Debug.Assert(packets != null);
            if (pktIdx == packets.Count - 1 || pktIdx == 0 && lastContinues)
            {
                packet.GranulePosition = packet.PageGranulePosition;

                // if we're the last packet completed in the page,
                // no more pages are available, and _isEndOfStream is set, set .IsEndOfStream
                if (pageIndex == _pageOffsets.Count - 1 && _isEndOfStream)
                    packet.IsEndOfStream = true;
            }

            if (_packetGranuleCounts.TryGetValue(packetIndex, out var granuleCount))
            {
                packet.GranuleCount = granuleCount;

                if (_packetGranulePositions.TryGetValue(packetIndex, out var granulePos))
                    packet.GranulePosition = granulePos;
            }

            _lastPacket = packet;
            return packet;
        }

        private bool GetNextPage()
        {
            _reader.Lock();
            try
            {
                while (_reader.ReadNextPage() && _packetIndex == _packetCount)
                {
                }
            }
            finally
            {
                _reader.Release();
            }
            return _packetIndex < _packetCount;
        }

        private List<(long DataOffset, int Size)>? GetPagePackets(
            int pageIndex, out bool lastContinues, out bool isResync, out int pageSeqNo)
        {
            if (_cachedPageIndex.HasValue && _cachedPageIndex.Value == pageIndex)
            {
                pageSeqNo = _cachedPageSeqNo;
                isResync = _cachedIsResync;
                lastContinues = _cachedLastContinues;
                return _cachedSegments;
            }

            long pageOffset = _pageOffsets[pageIndex];

            isResync = pageOffset < 0;
            if (isResync)
                pageOffset *= -1;

            lastContinues = false;
            int seqNo = -1;
            List<(long, int)>? packets = null;

            _reader.Lock();
            try
            {
                if (_reader.ReadPageAt(pageOffset))
                {
                    seqNo = _reader.SequenceNumber;
                    packets = _reader.GetPackets(out lastContinues);
                }
            }
            finally
            {
                _reader.Release();
            }

            if (packets != null)
            {
                if (isResync && _pageContinuations[pageIndex])
                    packets.RemoveAt(0);

                _cachedPageSeqNo = pageSeqNo = seqNo;
                _cachedIsResync = isResync;
                _cachedPageIndex = pageIndex;
                _cachedLastContinues = lastContinues;
                return _cachedSegments = packets;
            }

            pageSeqNo = -1;
            isResync = false;
            return null;
        }

        internal void SetEndOfStream()
        {
            _isEndOfStream = true;
        }

        public void Dispose()
        {
            _pageOffsets = null!;
            _pageGranules = null!;
            _pagePacketCounts = null!;
            _pageContinuations = null!;
            _packetGranuleCounts = null!;
            _cachedPageIndex = null!;
            _cachedSegments = null!;
            _lastPacket = null!;
            _reader = null!;
            _isEndOfStream = true;
            _packetCount = 0;
        }
    }
}