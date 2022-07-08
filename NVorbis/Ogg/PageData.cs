using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace NVorbis.Ogg
{
    internal class PageData
    {
        private byte[]? _pageData;
        private int _refCount;

        public bool IsResync { get; }

        public PageHeader Header
        {
            get
            {
                if (_pageData == null)
                {
                    ThrowObjectDisposed();
                }
                return new PageHeader(_pageData);
            }
        }

        public int Length
        {
            get
            {
                if (_pageData == null)
                {
                    ThrowObjectDisposed();
                }
                return _pageData.Length;
            }
        }

        public PageData(int length, bool isResync)
        {
            _refCount = 1;

            IsResync = isResync;

            _pageData = new byte[length];
        }

        public ArraySegment<byte> AsSegment()
        {
            if (_pageData == null)
            {
                ThrowObjectDisposed();
            }
            return new ArraySegment<byte>(_pageData);
        }

        public Span<byte> AsSpan()
        {
            if (_pageData == null)
            {
                ThrowObjectDisposed();
            }
            return _pageData.AsSpan();
        }

        public ArraySegment<byte>[] GetPackets()
        {
            ArraySegment<byte> segment = AsSegment();
            PageHeader header = new(segment);
            header.GetPacketCount(out ushort packetCount, out _, out _);

            byte segmentCount = header.SegmentCount;

            ArraySegment<byte>[] packets = ReadPackets(
                packetCount,
                segment.Slice(27, segmentCount),
                segment.Slice(27 + segmentCount, segment.Count - 27 - segmentCount));

            return packets;
        }

        private static ArraySegment<byte>[] ReadPackets(ushort packetCount, Span<byte> segments, ArraySegment<byte> dataBuffer)
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

        public void IncrementRef()
        {
            if (_refCount == 0)
            {
                ThrowObjectDisposed();
            }
            Interlocked.Increment(ref _refCount);
        }

        public int DecrementRef()
        {
            int count = Interlocked.Decrement(ref _refCount);
            if (count == 0)
            {
                Dispose();
            }
            return count;
        }

        private void Dispose()
        {
            _pageData = null;
        }

        [DoesNotReturn]
        private void ThrowObjectDisposed()
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        ~PageData()
        {
            if (_refCount > 0)
            {
                Dispose();
            }
        }
    }
}