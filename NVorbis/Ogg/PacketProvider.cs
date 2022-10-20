using System;
using System.Collections.Concurrent;
using NVorbis.Contracts;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class PacketProvider : IPacketProvider
    {
        private static ConcurrentQueue<PacketData[]> _dataPartPool = new();
        private const int DataPartInitialArraySize = 2;

        private IStreamPageReader _reader;
        private bool _isDisposed;

        private long _pageIndex;
        private int _packetIndex;

        public bool CanSeek => true;

        public int StreamSerial { get; }

        internal PacketProvider(IStreamPageReader reader, int streamSerial)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));

            StreamSerial = streamSerial;
        }

        public long GetGranuleCount()
        {
            if (_reader == null)
                throw new ObjectDisposedException(nameof(PacketProvider));

            if (!_reader.HasAllPages)
            {
                // this will force the reader to attempt to read all pages
                _reader.GetPage(int.MaxValue, out _, out _, out _, out _, out _, out _);
            }
            return _reader.MaxGranulePosition.GetValueOrDefault();
        }

        public VorbisPacket GetNextPacket()
        {
            return GetNextPacket(ref _pageIndex, ref _packetIndex);
        }

        public long SeekTo(long granulePos, uint preRoll, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            if (_reader == null)
                throw new ObjectDisposedException(nameof(PacketProvider));

            long pageIndex = _reader.FindPage(granulePos);
            int packetIndex = FindPacket(pageIndex, preRoll, ref granulePos, packetGranuleCountProvider);

            if (!NormalizePacketIndex(ref pageIndex, ref packetIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(granulePos));
            }

            _pageIndex = pageIndex;
            _packetIndex = (byte)packetIndex;
            return granulePos;
        }

        private (long lastPageGranulePos, int lastPagePacketLength, int firstRealPacket) GetPreviousPageInfo(long pageIndex, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            if (pageIndex > 0)
            {
                int lastPagePacketLength;
                if (_reader.GetPage(pageIndex - 1, out var lastPageGranulePos, out _, out _, out var isContinued, out var lastPacketCount, out _))
                {
                    if (pageIndex > _reader.FirstDataPageIndex)
                    {
                        --pageIndex;
                        int lastPacketIndex = lastPacketCount - 1;
                        
                        // this will either be a continued packet OR the last packet of the last page
                        // in both cases that's precisely the value we need
                        VorbisPacket lastPacket = CreatePacket(ref pageIndex, ref lastPacketIndex, false, 0, false, isContinued, lastPacketCount, 0);
                        if (!lastPacket.IsValid)
                        {
                            throw new System.IO.InvalidDataException("Could not find end of continuation!");
                        }
                        
                        lastPagePacketLength = packetGranuleCountProvider.GetPacketGranuleCount(ref lastPacket);
                    }
                    else
                    {
                        lastPagePacketLength = 0;
                    }
                    return (lastPageGranulePos, lastPagePacketLength, isContinued ? 1 : 0);
                }
                throw new System.IO.InvalidDataException("Could not get preceding page?!");
            }
            else
            {
                return (0, 0, 0);
            }
        }

        private (long[] gps, long endGP) GetTargetPageInfo(long pageIndex, int firstRealPacket, int lastPagePacketLength, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            if (!_reader.GetPage(
                pageIndex, out long pageGranulePos, out bool isResync, out bool isContinuation, out bool isContinued,
                out ushort packetCount, out _))
            {
                throw new System.IO.InvalidDataException("Could not get found page?!");
            }

            if (isContinued)
            {
                // if continued, the last packet index doesn't apply
                packetCount--;
            }

            // get the granule positions of all packets in the page
            long[] gps = new long[packetCount];
            long endGP = pageGranulePos;
            for (int packetIndex = packetCount - 1; packetIndex >= firstRealPacket; packetIndex--)
            {
                gps[packetIndex] = endGP;

                // it would be nice to pass false instead of isContinued, but (hypothetically) we don't know if getPacketGranuleCount(...) needs the whole thing...
                // Vorbis doesn't, but someone might decide to try to use us for another purpose so we'll be good here.
                
                VorbisPacket packet = CreatePacket(
                    ref pageIndex, ref packetIndex, false, pageGranulePos, packetIndex == 0 && isResync, 
                    isContinued, packetCount, 0);
                
                if (!packet.IsValid)
                {
                    throw new System.IO.InvalidDataException("Could not find end of continuation!");
                }

                int count = packetGranuleCountProvider.GetPacketGranuleCount(ref packet);
                endGP -= count;
            }

            // if we're contnued, the the continued packet ends on our calcualted endGP
            if (firstRealPacket == 1)
            {
                gps[0] = endGP;
                endGP -= lastPagePacketLength;
            }

            return (gps, endGP);
        }

        private int FindPacket(long pageIndex, long[] gps, long endGP, long lastPageGranulePos, int lastPagePacketLength, ref long granulePos)
        {
            // next check for a bugged vorbis encoder...
            if (endGP != lastPageGranulePos)
            {
                long diff = endGP - lastPageGranulePos;
                if (GetIsVorbisBugDiff(diff))
                {
                    if (diff > 0)
                    {
                        // the last packet in the last page is a long block that was mis-counted by libvorbis
                        // if the requested granulePos is <= endGP, it's in that packet
                        // otherwise, the normal logic should be fine
                        // NOTE that this bug does not appear to happen on a continued packet, which makes this safe
                        if (granulePos <= endGP)
                        {
                            granulePos = endGP - lastPagePacketLength;
                            return -1;
                        }
                    }
                    else
                    {
                        // our pageGranulePos is wrong, so adjust everything and let the normal logic apply
                        for (int i = 0; i < gps.Length; i++)
                        {
                            gps[i] -= diff;
                        }
                    }
                }
                // if we're not on the first page, there's a problem...
                // technically there could still be a problem on the first page, but we're ignoring it
                else if (pageIndex > _reader.FirstDataPageIndex)
                {
                    // unknown error...
                    throw new System.IO.InvalidDataException($"GranulePos mismatch: Page {pageIndex}, expected {lastPageGranulePos}, calculated {endGP}");
                }
            }

            // finally, find the packet with the requested granulePos
            for (int i = 0; i < gps.Length; i++)
            {
                if (gps[i] >= granulePos)
                {
                    if (i == 0)
                    {
                        granulePos = endGP;
                    }
                    else
                    {
                        granulePos = gps[i - 1];
                    }
                    return i;
                }
            }

            throw new System.IO.InvalidDataException("Could not find seek packet?!");
        }

        private int FindPacket(long pageIndex, uint preRoll, ref long granulePos, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            // pageIndex is _probably_ the correct page (bugs in libogg mean long->short over page boundary isn't always correct).
            // We check for this by looking for a difference in the previous page's granulePos vs. the calculated value

            // first we look at the page info to see how it is set up
            var (lastPageGranulePos, lastPagePacketLength, firstRealPacket) = GetPreviousPageInfo(pageIndex, packetGranuleCountProvider);

            // now get the info on the target page
            var (gps, endGP) = GetTargetPageInfo(pageIndex, firstRealPacket, lastPagePacketLength, packetGranuleCountProvider);

            // finally figure out which packet in our known info we need to use
            int packetIndex = FindPacket(pageIndex, gps, endGP, lastPageGranulePos, lastPagePacketLength, ref granulePos);

            // then apply the preRoll (but only if we're not seeking into the first packet, which is its own preRoll)
            if (endGP > 0 || packetIndex > 1)
            {
                packetIndex -= (int)preRoll;
            }
            return packetIndex;
        }

        private static bool GetIsVorbisBugDiff(long diff)
        {
            // This requires either breaking abstractions OR doing some fancy bit math...
            // We're gonna use the latter to keep the abstractions clean.
            // For our bug, the bit pattern is x set bits followed by y cleared bits:
            //   x = the number of bits between short & long block sizes
            //   y = the number of bits in the short block size - 2
            // So in binary it looks like 111000000 for 2048/256 block sizes.
            // We pre-adjust the "/ 4" out by starting at 0 for y instead of 2

            // we have to use the absolute value for this to work right
            diff = Math.Abs(diff);

            // find the count for y
            long temp = diff;
            int shortBlockBits = 0;
            while (temp > 0 && (temp & 1) == 0)
            {
                ++shortBlockBits;
                temp >>= 1;
            }

            // find the count for x (shortcut: start from shortBlockBits since this is really an offset from there)
            int longBlockBits = shortBlockBits;
            while ((temp & 1) == 1)
            {
                ++longBlockBits;
                temp >>= 1;
            }

            // if we've encountered the bug, temp will be 0 and diff will equal longBlock / 4 - shortBLock /4
            return temp == 0 && diff == (1 << longBlockBits) - (1 << shortBlockBits);
        }

        // this method calc's the appropriate page and packet prior to the one specified, honoring continuations and handling negative packetIndex values
        // if packet index is larger than the current page allows, we just return it as-is
        private bool NormalizePacketIndex(ref long pageIndex, ref int packetIndex)
        {
            if (!_reader.GetPage(
                pageIndex, out _, out bool isResync, out bool isContinuation, out _, out _, out _))
            {
                return false;
            }

            long pgIdx = pageIndex;
            int pktIdx = packetIndex;

            while (pktIdx < (isContinuation ? 1 : 0))
            {
                // can't merge across resync
                if (isContinuation && isResync)
                    return false;

                // get the previous packet
                bool wasContinuation = isContinuation;
                if (!_reader.GetPage(
                    --pgIdx, out _, out isResync, out isContinuation, out bool isContinued, out ushort packetCount, out _))
                {
                    return false;
                }

                // can't merge if continuation flags don't match
                if (wasContinuation && !isContinued) return false;

                // add the previous packet's packetCount
                pktIdx += packetCount - (wasContinuation ? 1 : 0);
            }

            pageIndex = pgIdx;
            packetIndex = pktIdx;
            return true;
        }

        private VorbisPacket GetNextPacket(ref long pageIndex, ref int packetIndex)
        {
            if (_reader == null)
                throw new ObjectDisposedException(nameof(PacketProvider));

            if (_reader.GetPage(
                pageIndex, out long granulePos, out bool isResync, out _, out bool isContinued,
                out ushort packetCount, out int pageOverhead))
            {
                VorbisPacket packet = CreatePacket(
                    ref pageIndex, ref packetIndex, true, granulePos, isResync, isContinued, packetCount, pageOverhead);

                return packet;
            }

            return default;
        }

        private static PacketData[] GetDataPartArray(int minimumLength)
        {
            if (minimumLength == DataPartInitialArraySize)
            {
                if (_dataPartPool.TryDequeue(out PacketData[]? array))
                {
                    return array;
                }
            }
            return new PacketData[minimumLength];
        }

        private static void ReturnDataPartArray(PacketData[] array)
        {
            if (array.Length == DataPartInitialArraySize)
            {
                _dataPartPool.Enqueue(array);
            }
        }

        private VorbisPacket CreatePacket(
            ref long pageIndex, ref int packetIndex,
            bool advance, long granulePos, bool isResync, bool isContinued, ushort packetCount, int pageOverhead)
        {
            // create the packet list and add the item to it
            PacketData[] dataParts = GetDataPartArray(DataPartInitialArraySize);
            int partCount = 0;

            PacketLocation firstLocation = new((ulong)pageIndex, (uint)packetIndex);
            PageSlice firstSlice = GetPacketData(firstLocation);
            dataParts[partCount++] = new PacketData(firstLocation, firstSlice);

            // make sure we handle continuations
            bool isLastPacket;
            bool isFirstPacket;
            long finalPage = pageIndex;
            if (isContinued && packetIndex == packetCount - 1)
            {
                // by definition, it's the first packet in the page it ends on
                isFirstPacket = true;

                // but we don't want to include the current page's overhead if we didn't start the page
                if (packetIndex > 0)
                {
                    pageOverhead = 0;
                }

                // go read the next page(s) that include this packet
                long contPageIdx = pageIndex;
                while (isContinued)
                {
                    if (!_reader.GetPage(
                        ++contPageIdx, out granulePos, out isResync, out bool isContinuation, out isContinued,
                        out packetCount, out int contPageOverhead))
                    {
                        // no more pages?  In any case, we can't satify the request
                        return default;
                    }
                    pageOverhead += contPageOverhead;

                    // if the next page isn't a continuation or is a resync,
                    // the stream is broken so we'll just return what we could get
                    if (!isContinuation || isResync)
                    {
                        break;
                    }

                    // if the next page is continued, only keep reading if there are no more packets in the page
                    if (isContinued && packetCount > 1)
                    {
                        isContinued = false;
                    }

                    // add the packet to the list
                    if (dataParts.Length <= partCount)
                    {
                        Array.Resize(ref dataParts, dataParts.Length + 2);
                    }

                    PacketLocation location = new(contPageIdx, 0);
                    PageSlice slice = GetPacketData(location);
                    dataParts[partCount++] = new PacketData(location, slice);
                }

                // we're now the first packet in the final page, so we'll act like it...
                isLastPacket = packetCount == 1;

                // track the final page read
                finalPage = contPageIdx;
            }
            else
            {
                isFirstPacket = packetIndex == 0;
                isLastPacket = packetIndex == packetCount - 1;
            }

            // create the packet instance and populate it with the appropriate initial data
            VorbisPacket packet = new(this, new ArraySegment<PacketData>(dataParts, 0, partCount))
            {
                IsResync = isResync
            };

            // if it's the first packet, associate the container overhead with it
            if (isFirstPacket)
            {
                packet.ContainerOverheadBits = pageOverhead * 8;
            }

            // if we're the last packet completed in the page, set the .GranulePosition
            if (isLastPacket)
            {
                packet.GranulePosition = granulePos;

                // if we're the last packet completed in the page, no more pages are available,
                // and _hasAllPages is set, set .IsEndOfStream
                if (_reader.HasAllPages && finalPage == _reader.PageCount - 1)
                {
                    packet.IsEndOfStream = true;
                }
            }
            else
            {
                packet.GranulePosition = -1;
            }

            if (advance)
            {
                // if we've advanced a page, we continued a packet and should pick up with the next page
                if (finalPage != pageIndex)
                {
                    // we're on the final page now
                    pageIndex = finalPage;

                    // the packet index will be modified below, so set it to the end of the continued packet
                    packetIndex = 0;
                }

                // if we're on the last packet in the page, move to the next page
                // we can't use isLast here because the logic is different; last in page granule vs. last in physical page
                if (packetIndex == packetCount - 1)
                {
                    ++pageIndex;
                    packetIndex = 0;
                }
                // otherwise, just move to the next packet
                else
                {
                    ++packetIndex;
                }
            }

            // done!
            return packet;
        }

        public PageSlice GetPacketData(PacketLocation location)
        {
            ulong pageIndex = location.PageIndex;
            uint packetIndex = location.PacketIndex;

            PageData? pageData = _reader.GetPage((long)pageIndex);
            return pageData.GetPacket(packetIndex);
        }

        public void FinishPacket(ref VorbisPacket packet)
        {
            if (packet.DataParts.Array != null)
            {
                ReturnDataPartArray(packet.DataParts.Array);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
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
