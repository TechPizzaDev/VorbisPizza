using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NVorbis.Contracts;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class ForwardOnlyPacketProvider : IForwardOnlyPacketProvider
    {
        private int _lastSeqNo;
        private readonly Queue<(byte[] buf, bool isResync)> _pageQueue = new();

        private readonly IPageReader _reader;
        private byte[]? _pageBuf;
        private int _packetIndex;
        private bool _isEndOfStream;
        private int _dataStart;

        private bool _hasPacketData;
        private bool _isPacketFinished;
        private ArraySegment<byte> _packetBuf;
        private PacketDataPart[] _dataParts;

        public ForwardOnlyPacketProvider(IPageReader reader, int streamSerial)
        {
            _reader = reader;
            StreamSerial = streamSerial;

            // force the first page to read
            _packetIndex = int.MaxValue;
            _isPacketFinished = true;

            _dataParts = new PacketDataPart[1];
        }

        public bool CanSeek => false;

        public int StreamSerial { get; }

        public bool AddPage(byte[] buf, bool isResync)
        {
            if (((PageFlags)buf[5] & PageFlags.BeginningOfStream) != 0)
            {
                if (_isEndOfStream)
                {
                    return false;
                }
                isResync = true;
                _lastSeqNo = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(18, sizeof(int)));
            }
            else
            {
                // check the sequence number
                int seqNo = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(18, sizeof(int)));
                isResync |= seqNo != _lastSeqNo + 1;
                _lastSeqNo = seqNo;
            }

            // there must be at least one packet with data
            int ttl = 0;
            for (int i = 0; i < buf[26]; i++)
            {
                ttl += buf[27 + i];
            }
            if (ttl == 0)
            {
                return false;
            }

            _pageQueue.Enqueue((buf, isResync));
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
            byte[]? pageBuf;
            bool isResync;
            int dataStart;
            int packetIndex;
            bool isCont, isCntd;
            if (_pageBuf != null && _packetIndex < 27 + _pageBuf[26])
            {
                pageBuf = _pageBuf;
                isResync = false;
                dataStart = _dataStart;
                packetIndex = _packetIndex;
                isCont = false;
                isCntd = pageBuf[26 + pageBuf[26]] == 255;
            }
            else
            {
                if (!ReadNextPage(out pageBuf, out isResync, out dataStart, out packetIndex, out isCont, out isCntd))
                {
                    // couldn't read the next page...
                    return default;
                }
            }

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
                    contOverhead += GetPacketLength(pageBuf, ref packetIndex);

                    // if we moved to the end of the page, we can't satisfy the request from here...
                    if (packetIndex == 27 + pageBuf[26])
                    {
                        // ... so we'll just recurse and try again
                        return GetNextPacket();
                    }
                }
            }
            if (!isFirst)
            {
                contOverhead = 0;
            }

            // second, determine how long the packet is
            int dataLen = GetPacketLength(pageBuf, ref packetIndex);
            ArraySegment<byte> packetBuf = new(pageBuf, dataStart, dataLen);
            dataStart += dataLen;

            // third, determine if the packet is the last one in the page
            bool isLast = packetIndex == 27 + pageBuf[26];
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
                    GetPacketLength(pageBuf, ref pi);
                    isLast = pi == 27 + pageBuf[26];
                }
            }

            // forth, if it is the last one, process continuations or flags & granulePos
            bool isEos = false;
            long granulePos = -1;
            if (isLast)
            {
                granulePos = BinaryPrimitives.ReadInt64LittleEndian(pageBuf.AsSpan(6, sizeof(long)));

                // fifth, set flags from the end page
                if (((PageFlags)pageBuf[5] & PageFlags.EndOfStream) != 0 || (_isEndOfStream && _pageQueue.Count == 0))
                {
                    isEos = true;
                }
            }
            else
            {
                while (isCntd && packetIndex == 27 + pageBuf[26])
                {
                    if (!ReadNextPage(out pageBuf, out isResync, out dataStart, out packetIndex, out isCont, out isCntd)
                        || isResync || !isCont)
                    {
                        // just use what data we can...
                        break;
                    }

                    // we're in the right spot!

                    // update the overhead count
                    contOverhead += 27 + pageBuf[26];

                    // save off the previous buffer data
                    ArraySegment<byte> prevBuf = packetBuf;

                    // get the size of this page's portion
                    int contSz = GetPacketLength(pageBuf, ref packetIndex);

                    // set up the new buffer and fill it
                    packetBuf = new(new byte[prevBuf.Count + contSz]);
                    prevBuf.CopyTo(packetBuf);
                    pageBuf.AsSpan(dataStart, contSz).CopyTo(packetBuf.Slice(prevBuf.Count));

                    // now that we've read, update our start position
                    dataStart += contSz;
                }
            }

            // last, save off our state and return true
            VorbisPacket packet = new(this, _dataParts)
            {
                IsResync = isResync,
                GranulePosition = granulePos,
                IsEndOfStream = isEos,
                ContainerOverheadBits = contOverhead * 8
            };
            _pageBuf = pageBuf;
            _dataStart = dataStart;
            _packetIndex = packetIndex;
            _packetBuf = packetBuf;
            _isEndOfStream |= isEos;
            _hasPacketData = true;
            _isPacketFinished = false;
            packet.Reset();
            return packet;
        }

        private bool ReadNextPage(
            [MaybeNullWhen(false)] out byte[] pageBuf,
            out bool isResync, out int dataStart, out int packetIndex, out bool isContinuation, out bool isContinued)
        {
            while (_pageQueue.Count == 0)
            {
                if (_isEndOfStream || !_reader.ReadNextPage())
                {
                    // we must be done
                    pageBuf = null;
                    isResync = false;
                    dataStart = 0;
                    packetIndex = 0;
                    isContinuation = false;
                    isContinued = false;
                    return false;
                }
            }

            (byte[] buf, bool isResync) temp = _pageQueue.Dequeue();
            pageBuf = temp.buf;
            isResync = temp.isResync;

            dataStart = pageBuf[26] + 27;
            packetIndex = 27;
            isContinuation = ((PageFlags)pageBuf[5] & PageFlags.ContinuesPacket) != 0;
            isContinued = pageBuf[26 + pageBuf[26]] == 255;
            return true;
        }

        private static int GetPacketLength(byte[] pageBuf, ref int packetIndex)
        {
            int len = 0;
            while (pageBuf[packetIndex] == 255 && packetIndex < pageBuf[26] + 27)
            {
                len += pageBuf[packetIndex];
                ++packetIndex;
            }
            if (packetIndex < pageBuf[26] + 27)
            {
                len += pageBuf[packetIndex];
                ++packetIndex;
            }
            return len;
        }

        public ArraySegment<byte> GetPacketData(PacketDataPart dataPart)
        {
            if (_hasPacketData)
            {
                return _packetBuf;
            }

            _hasPacketData = false;
            return ArraySegment<byte>.Empty;
        }

        public void FinishPacket(in VorbisPacket packet)
        {
            _packetBuf = ArraySegment<byte>.Empty;
            _isPacketFinished = true;
        }

        long IPacketProvider.GetGranuleCount()
        {
            throw new NotSupportedException();
        }

        long IPacketProvider.SeekTo(long granulePos, uint preRoll, GetPacketGranuleCount getPacketGranuleCount)
        {
            throw new NotSupportedException();
        }
    }
}
