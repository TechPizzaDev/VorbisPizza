/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NVorbis
{
    internal static class Huffman
    {
        private const int MaxTableBits = 10;

        internal static (HuffmanListNode[] Initial, HuffmanListNode[] Result) BuildPrefixedLinkedList<TList>(
            TList values,
            int[] lengthList,
            int[] codeList,
            out int tableBits,
            out int? firstOverflowNode)
            where TList : IReadOnlyList<int>
        {
            var initialNodes = new HuffmanListNode[lengthList.Length];

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

            Array.Sort(initialNodes, 0, initialNodes.Length);

            tableBits = maxLen > MaxTableBits ? MaxTableBits : maxLen;

            var resultNodes = new HuffmanListNode[1 << tableBits];

            firstOverflowNode = null;
            for (int i = 0; i < initialNodes.Length && initialNodes[i].Length < 99999; i++)
            {
                if (firstOverflowNode == null)
                {
                    int itemBits = initialNodes[i].Length;
                    if (itemBits > tableBits)
                    {
                        firstOverflowNode = i;
                    }
                    else
                    {
                        int maxVal = 1 << (tableBits - itemBits);
                        var item = initialNodes[i];
                        for (int j = 0; j < maxVal; j++)
                        {
                            int idx = (j << itemBits) | item.Bits;
                            resultNodes[idx] = item;
                        }
                    }
                }
                else
                {
                    initialNodes[i - 1].NextIndex = i;
                }
            }
            return (initialNodes, resultNodes);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HuffmanListNode : IComparable<HuffmanListNode>
    {
        public readonly bool HasValue;
        public readonly int Value;
        public readonly int Length;
        public readonly int Bits;
        public readonly int Mask;
        public int NextIndex;

        public HuffmanListNode(int value, int length, int bits, int mask, int nextIndex)
        {
            HasValue = true;
            Value = value;
            Length = length;
            Bits = bits;
            Mask = mask;
            NextIndex = nextIndex;
        }

        public int CompareTo(HuffmanListNode other)
        {
            int length = Length - other.Length;
            if (length == 0)
                return Bits - other.Bits;
            return length;
        }
    }
}
