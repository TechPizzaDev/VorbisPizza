using System;

namespace NVorbis.Ogg
{
    /// <summary>
    /// Represents the location of a packet within a logical stream.
    /// </summary>
    public readonly struct PacketDataPart : IEquatable<PacketDataPart>
    {
        /// <summary>
        /// The maximum value of <see cref="PageIndex"/>.
        /// </summary>
        public const ulong MaxPageIndex = ulong.MaxValue >> 8;

        /// <summary>
        /// The maximum value of <see cref="PacketIndex"/>.
        /// </summary>
        public const byte MaxPacketIndex = byte.MaxValue;

        private readonly ulong _value;

        /// <summary>
        /// Gets the page index of the packet.
        /// </summary>
        public ulong PageIndex => _value >> 8;

        /// <summary>
        /// Gets the packet index within the page.
        /// </summary>
        public byte PacketIndex => (byte)(_value & 0xff);

        /// <summary>
        /// Constructs the <see cref="PacketDataPart"/> with the given values.
        /// </summary>
        /// <param name="pageIndex">The page index. Cannot be greater than <see cref="MaxPacketIndex"/>.</param>
        /// <param name="packetIndex">The packet index. Cannot be greater than <see cref="MaxPacketIndex"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="pageIndex"/> or <paramref name="packetIndex"/> was out of the allowed range.
        /// </exception>
        public PacketDataPart(ulong pageIndex, uint packetIndex)
        {
            if (pageIndex > MaxPageIndex)
                throw new ArgumentOutOfRangeException(nameof(packetIndex));

            if (packetIndex > MaxPacketIndex)
                throw new ArgumentOutOfRangeException(nameof(packetIndex));

            _value = (pageIndex << 8) | packetIndex;
        }

        /// <inheritdoc cref="PacketDataPart(ulong, uint)"/>
        public PacketDataPart(long pageIndex, int packetIndex) : this((ulong)pageIndex, (uint)packetIndex)
        {
        }

        /// <inheritdoc />
        public bool Equals(PacketDataPart other)
        {
            return _value == other._value;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is PacketDataPart other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }
    }
}
