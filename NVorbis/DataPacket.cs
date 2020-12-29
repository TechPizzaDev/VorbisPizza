using System;
using NVorbis.Contracts;

namespace NVorbis
{
    /// <summary>
    /// Provides a concrete base implementation of <see cref="IPacket"/>.
    /// </summary>
    public abstract partial class DataPacket : IPacket
    {
        private ulong _bitBucket;
        private int _bitCount;
        private byte _overflowBits;
        private PacketFlags _packetFlags;
        private int _readBits;

        /// <summary>
        /// Gets the number of container overhead bits associated with this packet.
        /// </summary>
        public int ContainerOverheadBits { get; set; }

        /// <summary>
        /// Gets the granule position of the packet, if known.
        /// </summary>
        public long? GranulePosition { get; set; }

        /// <summary>
        /// Gets whether this packet occurs immediately following a loss of sync in the stream.
        /// </summary>
        public bool IsResync
        {
            get => GetFlag(PacketFlags.IsResync);
            set => SetFlag(PacketFlags.IsResync, value);
        }

        /// <summary>
        /// Gets whether this packet did not read its full data.
        /// </summary>
        public bool IsShort
        {
            get => GetFlag(PacketFlags.IsShort);
            private set => SetFlag(PacketFlags.IsShort, value);
        }

        /// <summary>
        /// Gets whether the packet is the last packet of the stream.
        /// </summary>
        public bool IsEndOfStream
        {
            get => GetFlag(PacketFlags.IsEndOfStream);
            set => SetFlag(PacketFlags.IsEndOfStream, value);
        }

        /// <summary>
        /// Gets the number of bits read from the packet.
        /// </summary>
        public int BitsRead => _readBits;

        /// <summary>
        /// Gets the number of bits left in the packet.
        /// </summary>
        public int BitsRemaining => TotalBits - _readBits;

        /// <summary>
        /// Gets the total number of bits in the packet.
        /// </summary>
        protected abstract int TotalBits { get; }

        private bool GetFlag(PacketFlags flag) => _packetFlags.HasFlag(flag);

        private void SetFlag(PacketFlags flag, bool value)
        {
            if (value)
                _packetFlags |= flag;
            else
                _packetFlags &= ~flag;
        }

        /// <summary>
        /// Reads the next byte in the packet.
        /// </summary>
        /// <returns>The next byte in the packet, or <c>-1</c> if no more data is available.</returns>
        protected abstract int ReadNextByte();

        /// <summary>
        /// Frees the buffers and caching for the packet instance.
        /// </summary>
        public virtual void Done()
        {
            // no-op for base
        }

        /// <summary>
        /// Resets the read buffers to the beginning of the packet.
        /// </summary>
        public virtual void Reset()
        {
            _bitBucket = 0;
            _bitCount = 0;
            _overflowBits = 0;
            _readBits = 0;
        }

        /// <inheritdoc/>
        public ulong ReadBits(int count)
        {
            // short-circuit 0
            if (count == 0)
                return 0UL;

            ulong value = TryPeekBits(count, out _);

            SkipBits(count);

            return value;
        }

        /// <summary>
        /// Attempts to read the specified number of bits from the packet.  Does not advance the read position.
        /// </summary>
        /// <param name="count">The number of bits to read.</param>
        /// <param name="bitsRead">Outputs the actual number of bits read.</param>
        /// <returns>The value of the bits read.</returns>
        public ulong TryPeekBits(int count, out int bitsRead)
        {
            if (count < 0 || count > 64)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
            {
                bitsRead = 0;
                return 0UL;
            }

            ulong value;
            while (_bitCount < count)
            {
                var val = ReadNextByte();
                if (val == -1)
                {
                    bitsRead = _bitCount;
                    value = _bitBucket;
                    return value;
                }
                _bitBucket = (ulong)(val & 0xFF) << _bitCount | _bitBucket;
                _bitCount += 8;

                if (_bitCount > 64)
                    _overflowBits = (byte)(val >> (72 - _bitCount));
            }

            value = _bitBucket;

            if (count < 64)
                value &= (1UL << count) - 1;

            bitsRead = count;
            return value;
        }

        /// <summary>
        /// Advances the read position by the the specified number of bits.
        /// </summary>
        /// <param name="count">The number of bits to skip reading.</param>
        public void SkipBits(int count)
        {
            if (count <= 0)
                return;

            if (_bitCount > count)
            {
                // we still have bits left over...
                if (count > 63)
                    _bitBucket = 0;
                else
                    _bitBucket >>= count;

                if (_bitCount > 64)
                {
                    int overflowCount = _bitCount - 64;
                    _bitBucket |= (ulong)_overflowBits << (_bitCount - count - overflowCount);

                    if (overflowCount > count)
                    {
                        // ugh, we have to keep bits in overflow
                        _overflowBits >>= count;
                    }
                }

                _bitCount -= count;
                _readBits += count;
            }
            else if (_bitCount == count)
            {
                _bitBucket = 0UL;
                _bitCount = 0;
                _readBits += count;
            }
            else //  _bitCount < count
            {
                // we have to move more bits than we have available...
                count -= _bitCount;
                _readBits += _bitCount;
                _bitCount = 0;
                _bitBucket = 0;

                while (count > 8)
                {
                    if (ReadNextByte() == -1)
                    {
                        count = 0;
                        IsShort = true;
                        break;
                    }
                    count -= 8;
                    _readBits += 8;
                }

                if (count > 0)
                {
                    int tmp = ReadNextByte();
                    if (tmp == -1)
                    {
                        IsShort = true;
                    }
                    else
                    {
                        _bitBucket = (ulong)(tmp >> count);
                        _bitCount = 8 - count;
                        _readBits += count;
                    }
                }
            }
        }
    }
}
