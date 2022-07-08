using System;
using System.Buffers.Binary;
using System.Diagnostics;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal ref struct PageHeader
    {
        public const int MaxHeaderSize = 282;

        public ReadOnlySpan<byte> Data { get; }

        public int StreamSerial => BinaryPrimitives.ReadInt32LittleEndian(Data.Slice(14, sizeof(int)));
        public int SequenceNumber => BinaryPrimitives.ReadInt32LittleEndian(Data.Slice(18, sizeof(int)));
        public PageFlags PageFlags => (PageFlags)Data[5];
        public long GranulePosition => BinaryPrimitives.ReadInt64LittleEndian(Data.Slice(6, sizeof(long)));
        public byte SegmentCount => Data[26];
        public int PageOverhead => 27 + SegmentCount;

        public PageHeader(ReadOnlySpan<byte> headerData)
        {
            Debug.Assert(headerData.Length >= 27);

            Data = headerData;

            Debug.Assert(headerData.Length >= PageOverhead);
        }

        public void GetPacketCount(out ushort packetCount, out int dataLength, out bool isContinued)
        {
            GetPacketCount(Data, out packetCount, out dataLength, out isContinued);
        }

        public static void GetPacketCount(
            ReadOnlySpan<byte> headerData, out ushort packetCount, out int dataLength, out bool isContinued)
        {
            byte segCnt = headerData[26];
            int dataLen = 0;
            ushort pktCnt = 0;

            int size = 0;
            for (int i = 0, idx = 27; i < segCnt; i++, idx++)
            {
                byte seg = headerData[idx];
                size += seg;
                dataLen += seg;
                if (seg < 255)
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
                isContinued = headerData[segCnt + 26] == 255;
                ++pktCnt;
            }
            else
            {
                isContinued = false;
            }

            packetCount = pktCnt;
            dataLength = dataLen;
        }
    }
}