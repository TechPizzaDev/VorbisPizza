using System;
using System.Buffers.Binary;

namespace NVorbis.Ogg
{
    internal ref partial struct Crc
    {
        //static Crc()
        //{
        //    const uint CRC32_POLY = 0x04c11db7;
        //
        //    uint[] crcTable = new uint[1024];
        //    Init(crcTable, false, 32, CRC32_POLY);
        //    s_crcTable = crcTable;
        //}
        //
        //private static int Init(Span<uint> table, bool le, int bits, uint poly)
        //{
        //    for (int i = 0; i < 256; i++)
        //    {
        //        if (le)
        //        {
        //            uint c = (uint)i;
        //            for (int j = 0; j < 8; j++)
        //                c = (c >> 1) ^ (poly & (uint)(-(c & 1)));
        //            table[i] = c;
        //        }
        //        else
        //        {
        //            uint c = (uint)(i << 24);
        //            for (int j = 0; j < 8; j++)
        //                c = (c << 1) ^ ((poly << (32 - bits)) & (uint)(((int)c) >> 31));
        //            table[i] = BinaryPrimitives.ReverseEndianness(c);
        //        }
        //    }
        //    table[256] = 1;
        //
        //    if (table.Length >= 1024)
        //    {
        //        for (int i = 0; i < 256; i++)
        //        {
        //            for (int j = 0; j < 3; j++)
        //            {
        //                table[256 * (j + 1) + i] =
        //                    (table[256 * j + i] >> 8) ^ table[(int)(table[256 * j + i] & 0xFF)];
        //            }
        //        }
        //    }
        //    return 0;
        //}

        private uint _crc;
        private Span<uint> _table;

        public static Crc Create()
        {
            return new Crc
            {
                _crc = 0U,
                _table = s_crcTable,
            };
        }

        private static unsafe uint Update(uint* table, uint crc, byte* buffer, nint length)
        {
            byte* end = buffer + length;
            while (((nint)buffer & 3) != 0 && buffer < end)
            {
                crc = table[((byte)crc) ^ *buffer++] ^ (crc >> 8);
            }

            while (buffer < end - 3)
            {
                uint value = !BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReverseEndianness(*(uint*)buffer)
                    : *(uint*)buffer;
                buffer += 4;

                crc ^= value;
                crc = table[3 * 256 + (crc & 0xFF)] ^
                      table[2 * 256 + ((crc >> 8) & 0xFF)] ^
                      table[1 * 256 + ((crc >> 16) & 0xFF)] ^
                      table[0 * 256 + ((crc >> 24))];
            }

            while (buffer < end)
            {
                crc = table[((byte)crc) ^ *buffer++] ^ (crc >> 8);
            }
            return crc;
        }

        public unsafe void Update(ReadOnlySpan<byte> values)
        {
            fixed (uint* table = _table)
            fixed (byte* ptr = values)
            {
                _crc = Update(table, _crc, ptr, values.Length);
            }
        }

        public unsafe void Update(byte value)
        {
            _crc = _table[((byte)_crc) ^ value] ^ (_crc >> 8);
        }

        public bool Test(uint checkCrc)
        {
            return _crc == checkCrc;
        }
    }
}
