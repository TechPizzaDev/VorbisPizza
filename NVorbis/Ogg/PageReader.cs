using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal class PageReader : PageReaderBase, IPageData
    {
        private readonly Dictionary<int, IStreamPageReader> _streamReaders = new();
        private readonly NewStreamCallback _newStreamCallback;
        private readonly object _readLock = new();

        private long _nextPageOffset;
        private ushort _pageSize;
        private ArraySegment<byte>[] _packets;

        public PageReader(Stream stream, bool leaveOpen, NewStreamCallback newStreamCallback)
            : base(stream, leaveOpen)
        {
            _newStreamCallback = newStreamCallback;
        }

        private ushort ParsePageHeader(ReadOnlySpan<byte> pageBuf, int? streamSerial, bool? isResync)
        {
            byte segCnt = pageBuf[26];
            int dataLen = 0;
            int pktCnt = 0;
            bool isContinued = false;

            int size = 0;
            for (int i = 0, idx = 27; i < segCnt; i++, idx++)
            {
                byte seg = pageBuf[idx];
                size += seg;
                dataLen += seg;
                if (seg < 255)
                {
                    if (size > 0)
                    {
                        ++pktCnt;
                    }
                    size = 0;
                }
            }
            if (size > 0)
            {
                isContinued = pageBuf[segCnt + 26] == 255;
                ++pktCnt;
            }

            StreamSerial = streamSerial ?? BinaryPrimitives.ReadInt32LittleEndian(pageBuf.Slice(14, sizeof(int)));
            SequenceNumber = BinaryPrimitives.ReadInt32LittleEndian(pageBuf.Slice(18, sizeof(int)));
            PageFlags = (PageFlags)pageBuf[5];
            GranulePosition = BinaryPrimitives.ReadInt64LittleEndian(pageBuf.Slice(6, sizeof(long)));
            PacketCount = (ushort)pktCnt;
            IsResync = isResync;
            IsContinued = isContinued;
            PageOverhead = 27 + segCnt;
            return (ushort)(PageOverhead + dataLen);
        }

        private static ArraySegment<byte>[] ReadPackets(int packetCount, Span<byte> segments, ArraySegment<byte> dataBuffer)
        {
            ArraySegment<byte>[] list = new ArraySegment<byte>[packetCount];
            int listIdx = 0;
            int dataIdx = 0;
            int size = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                byte seg = segments[i];
                size += seg;
                if (seg < 255)
                {
                    if (size > 0)
                    {
                        list[listIdx++] = dataBuffer.Slice(dataIdx, size);
                        dataIdx += size;
                    }
                    size = 0;
                }
            }
            if (size > 0)
            {
                list[listIdx] = dataBuffer.Slice(dataIdx, size);
            }

            return list;
        }

        public override void Lock()
        {
            Monitor.Enter(_readLock);
        }

        protected override bool CheckLock()
        {
            return Monitor.IsEntered(_readLock);
        }

        public override bool Release()
        {
            if (Monitor.IsEntered(_readLock))
            {
                Monitor.Exit(_readLock);
                return true;
            }
            return false;
        }

        protected override void SaveNextPageSearch()
        {
            _nextPageOffset = StreamPosition;
        }

        protected override void PrepareStreamForNextPage()
        {
            SeekStream(_nextPageOffset);
        }

        protected override bool AddPage(int streamSerial, byte[] pageBuf, bool isResync)
        {
            PageOffset = StreamPosition - pageBuf.Length;
            ParsePageHeader(pageBuf, streamSerial, isResync);

            // if the page doesn't have any packets, we can't use it
            if (PacketCount == 0) return false;

            _packets = ReadPackets(
                PacketCount,
                new Span<byte>(pageBuf, 27, pageBuf[26]),
                new ArraySegment<byte>(pageBuf, 27 + pageBuf[26], pageBuf.Length - 27 - pageBuf[26]));

            if (_streamReaders.TryGetValue(streamSerial, out IStreamPageReader spr))
            {
                spr.AddPage();

                // if we've read the last page, remove from our list so cleanup can happen.
                // this is safe because the instance still has access to us for reading.
                if ((PageFlags & PageFlags.EndOfStream) == PageFlags.EndOfStream)
                {
                    _streamReaders.Remove(StreamSerial);
                }
            }
            else
            {
                StreamPageReader streamReader = new(this, StreamSerial);
                streamReader.AddPage();
                _streamReaders.Add(StreamSerial, streamReader);
                if (!_newStreamCallback(streamReader.PacketProvider))
                {
                    _streamReaders.Remove(StreamSerial);
                    return false;
                }
            }
            return true;
        }

        public override bool ReadPageAt(long offset)
        {
            Span<byte> hdrBuf = stackalloc byte[282];

            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock()) throw new InvalidOperationException("Must be locked prior to reading!");

            // this should be safe; we've already checked the page by now

            if (offset == PageOffset)
            {
                // short circuit for when we've already loaded the page
                return true;
            }

            SeekStream(offset);
            int cnt = EnsureRead(hdrBuf.Slice(0, 27));

            PageOffset = offset;
            if (VerifyHeader(hdrBuf, ref cnt))
            {
                // don't read the whole page yet; if our caller is seeking, they won't need packets anyway
                _packets = null;
                _pageSize = ParsePageHeader(hdrBuf, null, null);
                return true;
            }
            return false;
        }

        protected override void SetEndOfStreams()
        {
            foreach (KeyValuePair<int, IStreamPageReader> kvp in _streamReaders)
            {
                kvp.Value.SetEndOfStream();
            }
            _streamReaders.Clear();
        }


        #region IPacketData

        public long PageOffset { get; private set; }

        public int StreamSerial { get; private set; }

        public int SequenceNumber { get; private set; }

        public PageFlags PageFlags { get; private set; }

        public long GranulePosition { get; private set; }

        public ushort PacketCount { get; private set; }

        public bool? IsResync { get; private set; }

        public bool IsContinued { get; private set; }

        public int PageOverhead { get; private set; }

        public ArraySegment<byte>[] GetPackets()
        {
            if (!CheckLock()) throw new InvalidOperationException("Must be locked!");

            if (_packets == null)
            {
                byte[] pageBuf = new byte[_pageSize];
                SeekStream(PageOffset);
                EnsureRead(pageBuf);

                byte length = pageBuf[26];
                _packets = ReadPackets(
                    PacketCount,
                    pageBuf.AsSpan(27, length),
                    new ArraySegment<byte>(pageBuf, 27 + length, pageBuf.Length - 27 - length));
            }

            return _packets;
        }

        #endregion
    }
}