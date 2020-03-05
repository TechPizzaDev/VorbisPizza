using NVorbis.Contracts.Ogg;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NVorbis.Ogg
{
    class PageReader : PageReaderBase, IPageData
    {
        internal static Func<IPageData, int, IStreamPageReader> CreateStreamPageReader { get; set; } = 
            (pr, ss) => new StreamPageReader(pr, ss);

        private readonly Dictionary<int, IStreamPageReader> _streamReaders = new Dictionary<int, IStreamPageReader>();
        private readonly Func<Contracts.IPacketProvider, bool> _newStreamCallback;
        private readonly object _readLock = new object();

        private long _nextPageOffset;
        private ushort _pageSize;
        Memory<byte>[] _packets;

        public PageReader(Stream stream, bool leaveOpen, Func<Contracts.IPacketProvider, bool> newStreamCallback)
            : base(stream, leaveOpen)
        {
            _newStreamCallback = newStreamCallback;
        }

        private ushort ParsePageHeader(ReadOnlySpan<byte> pageBuf, int? streamSerial, bool? isResync)
        {
            var segCnt = pageBuf[26];
            var dataLen = 0;
            var pktCnt = 0;
            var isContinued = false;

            var size = 0;
            for (int i = 0, index = 27; i < segCnt; i++, index++)
            {
                size += pageBuf[index];
                dataLen += size;
                if (pageBuf[index] < 255)
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

            StreamSerial = streamSerial ?? BinaryPrimitives.ReadInt32LittleEndian(pageBuf.Slice(14));
            SequenceNumber = BinaryPrimitives.ReadInt32LittleEndian(pageBuf.Slice(18));
            PageFlags = (PageFlags)pageBuf[5];
            GranulePosition = BinaryPrimitives.ReadInt32LittleEndian(pageBuf.Slice(6));
            PacketCount = (short)pktCnt;
            IsResync = isResync;
            IsContinued = isContinued;
            PageOverhead = 27 + segCnt;
            return (ushort)(PageOverhead + dataLen);
        }

        private static Memory<byte>[] ReadPackets(int packetCount, Span<byte> segments, Memory<byte> dataBuffer)
        {
            var list = new Memory<byte>[packetCount];
            var listIdx = 0;
            var dataIdx = 0;
            var size = 0;

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
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
            _packets = ReadPackets(
                PacketCount, 
                new Span<byte>(pageBuf, 27, pageBuf[26]), 
                new Memory<byte>(pageBuf, 27 + pageBuf[26], pageBuf.Length - 27 - pageBuf[26]));

            if (_streamReaders.TryGetValue(streamSerial, out var spr))
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
                var streamReader = CreateStreamPageReader(this, StreamSerial);
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
            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock()) 
                throw new InvalidOperationException("Must be locked prior to reading!");

            // this should be safe; we've already checked the page by now

            if (offset == PageOffset)
            {
                // short circuit for when we've already loaded the page
                return true;
            }

            Span<byte> hdrBuf = stackalloc byte[282];

            SeekStream(offset);
            var cnt = EnsureRead(hdrBuf.Slice(0, 27));

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
            foreach (var kvp in _streamReaders)
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

        public short PacketCount { get; private set; }

        public bool? IsResync { get; private set; }

        public bool IsContinued { get; private set; }

        public int PageOverhead { get; private set; }

        public Memory<byte>[] GetPackets()
        {
            if (!CheckLock()) 
                throw new InvalidOperationException("Must be locked!");

            if (_packets == null)
            {
                var pageBuf = new byte[_pageSize];
                SeekStream(PageOffset);
                EnsureRead(pageBuf);
                _packets = ReadPackets(
                    PacketCount, 
                    new Span<byte>(pageBuf, 27, pageBuf[26]), 
                    new Memory<byte>(pageBuf, 27 + pageBuf[26], pageBuf.Length - 27 - pageBuf[26]));
            }

            return _packets;
        }

        #endregion
    }
}