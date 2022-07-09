using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace NVorbis.Ogg
{
    internal class PageData
    {
        private readonly PageDataPool _pool;
        internal ArraySegment<byte> _pageData;
        internal int _refCount;

        public bool IsResync { get; internal set; }

        public PageHeader Header
        {
            get
            {
                if (_refCount <= 0)
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
                if (_refCount <= 0)
                {
                    ThrowObjectDisposed();
                }
                return _pageData.Count;
            }
        }

        internal PageData(PageDataPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));

            _pageData = Array.Empty<byte>();
        }

        public ArraySegment<byte> AsSegment()
        {
            if (_refCount <= 0)
            {
                ThrowObjectDisposed();
            }
            return _pageData;
        }

        public Span<byte> AsSpan()
        {
            if (_refCount <= 0)
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
            _pool.Return(this);
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