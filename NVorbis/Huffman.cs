using System;
using System.Collections.Generic;
using NVorbis.Contracts;

namespace NVorbis
{
    internal struct Huffman
    {
        private const int MAX_TABLE_BITS = 10;

        public int TableBits { get; private set; }
        public HuffmanListNode[] PrefixTree { get; private set; }
        public HuffmanListNode[]? OverflowList { get; private set; }

        public static Huffman GenerateTable<TList>(TList values, int[] lengthList, int[] codeList)
            where TList : IReadOnlyList<int>
        {
            HuffmanListNode[] list = new HuffmanListNode[lengthList.Length];

            int maxLen = 0;
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = new HuffmanListNode
                {
                    Value = values[i],
                    Length = lengthList[i] <= 0 ? 99999 : lengthList[i],
                    Bits = codeList[i],
                    Mask = (1 << lengthList[i]) - 1,
                };
                if (lengthList[i] > 0 && maxLen < lengthList[i])
                {
                    maxLen = lengthList[i];
                }
            }

            Array.Sort(list, 0, list.Length);

            int tableBits = maxLen > MAX_TABLE_BITS ? MAX_TABLE_BITS : maxLen;

            HuffmanListNode[] prefixList = new HuffmanListNode[1 << tableBits];

            List<HuffmanListNode>? overflowList = null;
            for (int i = 0; i < list.Length && list[i].Length < 99999; i++)
            {
                int itemBits = list[i].Length;
                if (itemBits > tableBits)
                {
                    overflowList = new List<HuffmanListNode>(list.Length - i);
                    for (; i < list.Length && list[i].Length < 99999; i++)
                    {
                        overflowList.Add(list[i]);
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

            return new Huffman
            {
                TableBits = tableBits,
                PrefixTree = prefixList,
                OverflowList = overflowList?.ToArray()
            };
        }
    }
}
