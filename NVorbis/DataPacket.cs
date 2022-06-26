using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NVorbis
{
    /// <summary>
    /// Describes an abstract packet of data from a data stream.
    /// </summary>
    public abstract class DataPacket
    {
        /// <summary>
        /// Defines flags to apply to the current packet
        /// </summary>
        [Flags]
        // for now, let's use a byte... if we find we need more space, we can always expand it...
        protected enum PacketFlags : byte
        {
            /// <summary>
            /// Packet is first since reader had to resync with stream.
            /// </summary>
            IsResync = 0x01,
            /// <summary>
            /// Packet is the last in the logical stream.
            /// </summary>
            IsEndOfStream = 0x02,
            /// <summary>
            /// Packet does not have all its data available.
            /// </summary>
            IsShort = 0x04,

            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User0 = 0x08,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User1 = 0x10,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User2 = 0x20,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User3 = 0x40,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User4 = 0x80,
        }

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

        private bool GetFlag(PacketFlags flag)
        {
            return (_packetFlags & flag) == flag;
        }

        private void SetFlag(PacketFlags flag, bool value)
        {
            if (value)
            {
                _packetFlags |= flag;
            }
            else
            {
                _packetFlags &= ~flag;
            }
        }

        /// <summary>
        /// Reads the next bytes in the packet.
        /// </summary>
        /// <returns>The amount of read bytes, or <c>0</c> if no more data is available.</returns>
        protected abstract int ReadBytes(Span<byte> destination);

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

        /// <summary>
        /// Reads the specified number of bits from the packet and advances the read position.
        /// </summary>
        /// <param name="count">The number of bits to read.</param>
        /// <returns>The value read. If not enough bits remained, this will be a truncated value.</returns>
        public ulong ReadBits(int count)
        {
            // short-circuit 0
            if (count == 0) return 0UL;

            ulong value = TryPeekBits(count, out _);

            SkipBits(count);

            return value;
        }

        [SkipLocalsInit]
        private unsafe ulong RefillBits(ref int count)
        {
            byte* buffer = stackalloc byte[8];
            uint toRead = (71 - (uint)_bitCount) / 8;
            Span<byte> span = new(buffer, (int)toRead);
            int bytesRead = ReadBytes(span);

            int i = 0;
            for (; i + 4 <= bytesRead; i += 4)
            {
                _bitBucket |= (ulong)*(uint*)(buffer + i) << _bitCount;
                _bitCount += 32;
            }

            for (; i < bytesRead; i++)
            {
                _bitBucket |= (ulong)buffer[i] << _bitCount;
                _bitCount += 8;
            }

            if (bytesRead > 0 && _bitCount > 64)
                _overflowBits = (byte)(buffer[bytesRead - 1] >> (72 - _bitCount));

            if (count > _bitCount)
                count = _bitCount;

            ulong value = _bitBucket;
            if (count < 64)
                value &= (1UL << count) - 1;

            return value;
        }

        /// <summary>
        /// Attempts to read the specified number of bits from the packet. Does not advance the read position.
        /// </summary>
        /// <param name="count">The number of bits to read.</param>
        /// <param name="bitsRead">Outputs the actual number of bits read.</param>
        /// <returns>The value of the bits read.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong TryPeekBits(int count, out int bitsRead)
        {
            Debug.Assert((uint)count <= 64);

            bitsRead = count;
            if (_bitCount < count)
                return RefillBits(ref bitsRead);

            ulong value = _bitBucket;
            if (count < 64)
                value &= (1UL << count) - 1;

            return value;
        }

        /// <summary>
        /// Advances the read position by the the specified number of bits.
        /// </summary>
        /// <param name="count">The number of bits to skip reading.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SkipBits(int count)
        {
            if (_bitCount >= count)
            {
                // we still have bits left over...
                _bitBucket >>= count;

                if (_bitCount > 64)
                    SkipOverflow(count);

                _bitCount -= count;
                _readBits += count;
                return count;
            }
            else //  _bitCount < count
            {
                // we have to move more bits than we have available...
                return SkipExtraBits(count);
            }
        }

        private void SkipOverflow(int count)
        {
            int overflowCount = _bitCount - 64;
            _bitBucket |= (ulong)_overflowBits << (_bitCount - count - overflowCount);

            if (overflowCount > count)
            {
                // ugh, we have to keep bits in overflow
                _overflowBits >>= count;
            }
        }

        private int SkipExtraBits(int count)
        {
            Span<byte> tmp = stackalloc byte[1];
            if (count <= 0)
            {
                return 0;
            }

            int startReadBits = _readBits;

            count -= _bitCount;
            _readBits += _bitCount;
            _bitCount = 0;
            _bitBucket = 0;

            while (count > 8)
            {
                if (ReadBytes(tmp) == 0)
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
                int r = ReadBytes(tmp);
                if (r == 0)
                {
                    IsShort = true;
                }
                else
                {
                    _bitBucket = (ulong)(tmp[0] >> count);
                    _bitCount = 8 - count;
                    _readBits += count;
                }
            }

            return _readBits - startReadBits;
        }
    }
}
