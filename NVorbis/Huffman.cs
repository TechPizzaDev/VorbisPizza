using NVorbis.Contracts;
using System;
using System.Collections.Generic;

namespace NVorbis
{
    internal class Huffman : IHuffman, IComparer<HuffmanListNode>
    {
        private const int MaxTableBits = 10;

        public int TableBits { get; private set; }
        public IReadOnlyList<HuffmanListNode> PrefixTree { get; private set; } = Array.Empty<HuffmanListNode>();
        public IReadOnlyList<HuffmanListNode> OverflowList { get; private set; } = Array.Empty<HuffmanListNode>();

        public void GenerateTable(IReadOnlyList<int> values, int[] lengthList, int[] codeList)
        {
            var initialNodes = new HuffmanListNode[lengthList.Length];

            int maxLength = 0;
            for (int i = 0; i < initialNodes.Length; i++)
            {
                initialNodes[i] = new HuffmanListNode(
                    value: values[i],
                    length: lengthList[i] <= 0 ? 99999 : lengthList[i],
                    bits: codeList[i],
                    mask: (1 << lengthList[i]) - 1);

                if (lengthList[i] > 0 && maxLength < lengthList[i])
                    maxLength = lengthList[i];
            }

            Array.Sort(initialNodes, 0, initialNodes.Length, this);

            int tableBits = maxLength > MaxTableBits ? MaxTableBits : maxLength;

            var prefixList = new HuffmanListNode[1 << tableBits];

            List<HuffmanListNode>? overflowList = null;

            for (int i = 0; i < initialNodes.Length && initialNodes[i].Length < 99999; i++)
            {
                int itemBits = initialNodes[i].Length;
                if (itemBits > tableBits)
                {
                    overflowList = new List<HuffmanListNode>(initialNodes.Length - i);

                    for (; i < initialNodes.Length && initialNodes[i].Length < 99999; i++)
                        overflowList.Add(initialNodes[i]);
                }
                else
                {
                    int maxValue = 1 << (tableBits - itemBits);
                    var item = initialNodes[i];
                    for (int j = 0; j < maxValue; j++)
                    {
                        int index = (j << itemBits) | item.Bits;
                        prefixList[index] = item;
                    }
                }
            }

            TableBits = tableBits;
            PrefixTree = prefixList;

            if (overflowList != null)
                OverflowList = overflowList;
        }

        public int Compare(HuffmanListNode? x, HuffmanListNode? y)
        {
            if (x == null && y == null)
                return 0;
            else if (x == null)
                return -1;
            else if (y == null)
                return 1;

            int len = x.Length - y.Length;
            if (len == 0)
                return x.Bits - y.Bits;

            return len;
        }
    }
}
