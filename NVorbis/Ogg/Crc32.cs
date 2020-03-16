/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/

namespace NVorbis.Ogg
{
    struct Crc32
    {
        const uint CRC32_POLY = 0x04c11db7;
        static readonly uint[] _crcTable = new uint[256];

        static Crc32()
        {
            for (uint i = 0; i < _crcTable.Length; i++)
            {
                uint s = i << 24;
                for (int j = 0; j < 8; ++j)
                    s = (s << 1) ^ (s >= (1U << 31) ? CRC32_POLY : 0);
                _crcTable[i] = s;
            }
        }

        public uint Hash;

        public void Reset()
        {
            Hash = 0U;
        }

        public void Update(int nextVal)
        {
            Hash = (Hash << 8) ^ _crcTable[nextVal ^ (Hash >> 24)];
        }

        public bool Test(uint checkCrc)
        {
            return Hash == checkCrc;
        }
    }
}
