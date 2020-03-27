/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/

using System;

namespace NVorbis.Ogg
{
    struct Crc32
    {
        /// <summary>
        /// The same as the ethernet generator polynomial, although we use an
        /// unreflected alg and an init/final of 0, not 0xffffffff.
        /// </summary>
        const uint Polynomial = 0x04c11db7;

        const int MaxSlice = 16;

        static readonly uint[] _lookupTable = new uint[MaxSlice * 256];

        static Crc32()
        {
            var tmpTable = new uint[MaxSlice][];
            for (uint i = 0; i < MaxSlice; i++)
                tmpTable[i] = new uint[256];

            uint crc;
            for (uint i = 0; i < 256; i++)
            {
                crc = i << 24;

                for (uint j = 0; j < 8; j++)
                {
                    uint x = crc & (1u << 31);
                    uint shift = x != 0 ? Polynomial : 0;
                    crc = (crc << 1) ^ shift;
                }
                tmpTable[0][i] = crc;
            }

            for (uint i = 0; i < 256; i++)
            {
                for (uint j = 1; j < MaxSlice; j++)
                {
                    uint x = tmpTable[0][(tmpTable[j - 1][i] >> 24) & 0xFF];
                    tmpTable[j][i] = x ^ (tmpTable[j - 1][i] << 8);
                }
            }

            // reversing the tables makes the slicing-by-x loop slightly 
            // faster as it can access the tables linearly
            tmpTable.AsSpan().Reverse();

            for (int i = 0; i < MaxSlice; i++)
                tmpTable[i].CopyTo(_lookupTable.AsSpan(i * 256, 256));
        }

        public uint Hash;

        public void Reset()
        {
            Hash = 0;
        }

        public void Update(byte value)
        {
            // this should look at the first table so
            // look in the last table as the tables are reversed    
            Hash = (Hash << 8) ^ _lookupTable[(MaxSlice - 1) * 256 + ((Hash >> 24) & 0xff) ^ value];
        }

        /// <summary>
        /// Optimized CRC32 implementation, implemented in managed code.
        /// </summary>
        /// <remarks>
        /// This implementation has been tested with unsafe pointers,
        /// only yielding a tiny improvement. 
        /// The method is therefore implemented fully in managed code.
        /// </remarks>
        /// <param name="values"></param>
        public void Update(ReadOnlySpan<byte> values)
        {
            var table = _lookupTable; // caching into a local helps quite a lot

            while (values.Length >= MaxSlice)
            {
                Hash ^= (uint)(values[0] << 24 | values[1] << 16 | values[2] << 8 | values[3] << 0);

                Hash =
                    table[0 * 256 + (int)((Hash >> 24) & 0xff)] ^
                    table[1 * 256 + (int)((Hash >> 16) & 0xff)] ^
                    table[2 * 256 + (int)((Hash >> 8) & 0xff)] ^
                    table[3 * 256 + (int)((Hash >> 0) & 0xff)] ^
                    table[4 * 256 + values[4]] ^
                    table[5 * 256 + values[5]] ^
                    table[6 * 256 + values[6]] ^
                    table[7 * 256 + values[7]] ^
                    table[8 * 256 + values[8]] ^
                    table[9 * 256 + values[9]] ^
                    table[10 * 256 + values[10]] ^
                    table[11 * 256 + values[11]] ^
                    table[12 * 256 + values[12]] ^
                    table[13 * 256 + values[13]] ^
                    table[14 * 256 + values[14]] ^
                    table[15 * 256 + values[15]];

                values = values.Slice(MaxSlice);
            }

            for (int i = 0; i < values.Length; i++)
                Update(values[i]);
        }

        public bool Test(uint checkCrc)
        {
            return Hash == checkCrc;
        }
    }
}
