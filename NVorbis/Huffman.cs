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
    static class Huffman
    {
        const int MAX_TABLE_BITS = 10;

        static internal (HuffmanListNode[] Initial, HuffmanListNode[] Result) BuildPrefixedLinkedList<TList>(
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
                    next: -1);

                if (lengthList[i] > 0 && maxLen < lengthList[i])
                    maxLen = lengthList[i];
            }

            Array.Sort(initialNodes, 0, initialNodes.Length);

            tableBits = maxLen > MAX_TABLE_BITS ? MAX_TABLE_BITS : maxLen;

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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct HuffmanListNode : IComparable<HuffmanListNode>
    {
        public readonly int Value;
        public readonly int Length;
        public readonly int Bits;
        public readonly int Mask;
        public int NextIndex;

        public bool IsValid { get; }

        public HuffmanListNode(int value, int length, int bits, int mask, int next)
        {
            IsValid = true;
            Value = value;
            Length = length;
            Bits = bits;
            Mask = mask;
            NextIndex = next;
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
