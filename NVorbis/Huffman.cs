using System;
using System.IO;
using System.Numerics;
using NVorbis.Contracts;

namespace NVorbis
{
    internal readonly struct Huffman
    {
        public static Huffman Empty { get; } = new Huffman([], []);

        private const int MAX_TABLE_BITS = 10;

        public byte TableBits => (byte)BitOperations.Log2((uint)PrefixTree.Length);
        public HuffmanListNode[] PrefixTree { get; }
        public HuffmanListNode[] OverflowList { get; }

        private Huffman(HuffmanListNode[] prefixTree, HuffmanListNode[] overflowList)
        {
            PrefixTree = prefixTree;
            OverflowList = overflowList;
        }

        public static Huffman GenerateTable(int[]? values, int[] lengthList, int[] codeList)
        {
            HuffmanListNode[] list = new HuffmanListNode[lengthList.Length];

            int count = 0;
            int lastValidIdx = -1;

            int maxLen = 0;
            for (int i = 0; i < list.Length; i++)
            {
                int length = lengthList[i];
                if (length != 0)
                {
                    count++;
                    lastValidIdx = i;
                }

                list[i] = new HuffmanListNode
                {
                    Value = values != null ? values[i] : i,
                    Length = lengthList[i] <= 0 ? 99999 : lengthList[i],
                    Bits = codeList[i],
                    Mask = (1 << lengthList[i]) - 1,
                };
                if (lengthList[i] > 0)
                {
                    maxLen = Math.Max(maxLen, lengthList[i]);
                }
            }

            if (count == 1)
            {
                if (lengthList[lastValidIdx] != 1)
                {
                    throw new InvalidDataException("Invalid single entry.");
                }
            }

            list.AsSpan().Sort();

            int tableBits = maxLen > MAX_TABLE_BITS ? MAX_TABLE_BITS : maxLen;

            HuffmanListNode[] prefixList = new HuffmanListNode[1 << tableBits];

            HuffmanListNode[] overflowList = Array.Empty<HuffmanListNode>();
            int overflowIndex = 0;

            for (int i = 0; i < list.Length && list[i].Length < 99999; i++)
            {
                int itemBits = list[i].Length;
                if (itemBits > tableBits)
                {
                    int maxOverflowLength = list.Length - i;
                    if (overflowList.Length < maxOverflowLength)
                        overflowList = new HuffmanListNode[maxOverflowLength];

                    overflowIndex = 0;

                    for (; i < list.Length && list[i].Length < 99999; i++)
                    {
                        overflowList[overflowIndex++] = list[i];
                    }
                }
                else
                {
                    int maxVal = 1 << (tableBits - itemBits);
                    HuffmanListNode item = list[i];
                    for (int j = 0; j < maxVal; j++)
                    {
                        int idx = (j << itemBits) | item.Bits;
                        prefixList[idx] = item;
                    }
                }
            }

            if (overflowIndex < overflowList.Length)
            {
                Array.Resize(ref overflowList, overflowIndex);
            }

            return new Huffman(prefixList, overflowList);
        }
    }
}
