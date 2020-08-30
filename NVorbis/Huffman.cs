using NVorbis.Contracts;
using System;
using System.Collections.Generic;

namespace NVorbis
{
    class Huffman : IHuffman, IComparer<HuffmanListNode>
    {
        private const int MaxTableBits = 10;

        public int TableBits { get; private set; }
        public IReadOnlyList<HuffmanListNode> PrefixTree { get; private set; }
        public IReadOnlyList<HuffmanListNode> OverflowList { get; private set; }

        public void GenerateTable(IReadOnlyList<int> values, int[] lengthList, int[] codeList)
        {
            var list = new HuffmanListNode[lengthList.Length];

            var maxLen = 0;
            for (int i = 0; i < initialNodes.Length; i++)
            {
                initialNodes[i] = new HuffmanListNode(
                    value: values[i],
                    length: lengthList[i] <= 0 ? 99999 : lengthList[i],
                    bits: codeList[i],
                    mask: (1 << lengthList[i]) - 1,
                    nextIndex: -1);

                if (lengthList[i] > 0 && maxLen < lengthList[i])
                    maxLen = lengthList[i];
            }

            Array.Sort(list, 0, list.Length, this);

            tableBits = maxLen > MaxTableBits ? MaxTableBits : maxLen;

            var prefixList = new HuffmanListNode[1 << tableBits];

            List<HuffmanListNode> overflowList = null;
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
                    var item = list[i];
                    for (int j = 0; j < maxVal; j++)
                    {
                        int idx = (j << itemBits) | item.Bits;
                        prefixList[idx] = item;
                    }
                }
            }

            TableBits = tableBits;
            PrefixTree = prefixList;
            OverflowList = overflowList;
        }

        public int Compare(HuffmanListNode x, HuffmanListNode y)
        {
            int len = x.Length - y.Length;
            if (len == 0)
                return x.Bits - y.Bits;

            return len;
        }
    }
}
