using NVorbis.Contracts;
using System;

namespace NVorbis
{
    /// <summary>
    /// Provides extension methods for NVorbis types.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Reads into the specified buffer.
        /// </summary>
        /// <param name="packet">The packet instance to use.</param>
        /// <param name="buffer">The buffer to read into.</param>
        /// <returns>The number of bytes actually read into the buffer.</returns>
        public static int Read(this IPacket packet, Span<byte> buffer)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            for (int i = 0; i < buffer.Length; i++)
            {
                byte value = (byte)packet.TryPeekBits(8, out int bitsRead);
                if (bitsRead == 0)
                    return i;
                
                buffer[i] = value;
                packet.SkipBits(8);
            }
            return buffer.Length;
        }

        /// <summary>
        /// Reads the specified number of bytes from the packet and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A byte array holding the data read.</returns>
        public static byte[] ReadBytes(this IPacket packet, int count)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            var buf = new byte[count];
            int actualCount = Read(packet, buf.AsSpan(0, count));
            if (actualCount < count)
            {
                var tmp = new byte[actualCount];
                Buffer.BlockCopy(buf, 0, tmp, 0, actualCount);
                return tmp;
            }
            return buf;
        }

        /// <summary>
        /// Reads one bit from the packet and advances the read position.
        /// </summary>
        /// <returns><see langword="true"/> if the bit was a one, otehrwise <see langword="false"/>.</returns>
        public static bool ReadBit(this IPacket packet)
        {
            return packet.ReadBits(1) == 1;
        }

        /// <summary>
        /// Reads the next byte from the packet. Does not advance the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The byte read from the packet.</returns>
        public static byte PeekByte(this IPacket packet)
        {
            return (byte)packet.TryPeekBits(8, out _);
        }

        /// <summary>
        /// Reads the next byte from the packet and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The byte read from the packet.</returns>
        public static byte ReadByte(this IPacket packet)
        {
            return (byte)packet.ReadBits(8);
        }

        /// <summary>
        /// Reads the next 16 bits from the packet as a <see cref="short"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 16 bits.</returns>
        public static short ReadInt16(this IPacket packet)
        {
            return (short)packet.ReadBits(16);
        }

        /// <summary>
        /// Reads the next 32 bits from the packet as a <see cref="int"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 32 bits.</returns>
        public static int ReadInt32(this IPacket packet)
        {
            return (int)packet.ReadBits(32);
        }

        /// <summary>
        /// Reads the next 64 bits from the packet as a <see cref="long"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 64 bits.</returns>
        public static long ReadInt64(this IPacket packet)
        {
            return (long)packet.ReadBits(64);
        }

        /// <summary>
        /// Reads the next 16 bits from the packet as a <see cref="ushort"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 16 bits.</returns>
        public static ushort ReadUInt16(this IPacket packet)
        {
            return (ushort)packet.ReadBits(16);
        }

        /// <summary>
        /// Reads the next 32 bits from the packet as a <see cref="uint"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 32 bits.</returns>
        public static uint ReadUInt32(this IPacket packet)
        {
            return (uint)packet.ReadBits(32);
        }

        /// <summary>
        /// Reads the next 64 bits from the packet as a <see cref="ulong"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 64 bits.</returns>
        public static ulong ReadUInt64(this IPacket packet)
        {
            return packet.ReadBits(64);
        }

        /// <summary>
        /// Advances the position counter by the specified number of bytes.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="count">The number of bytes to advance.</param>
        public static void SkipBytes(this IPacket packet, int count)
        {
            packet.SkipBits(count * 8);
        }
    }
}
