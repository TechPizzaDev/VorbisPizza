using System;
using System.Collections.Generic;
using System.IO;
using NVorbis.Contracts;

namespace NVorbis
{
    internal class Codebook : ICodebook
    {
        private int[] _lengths;
        private float[] _lookupTable;
        private IReadOnlyList<HuffmanListNode> _overflowList;
        private IReadOnlyList<HuffmanListNode> _prefixList;
        private int _prefixBitLength;
        private int _maxBits;

        public int Dimensions { get; private set; }
        public int Entries { get; private set; }
        public int MapType { get; private set; }

        public float this[int entry, int dim] => _lookupTable[entry * Dimensions + dim];

        public Codebook()
        {
            _lengths = null!;
            _lookupTable = null!;
            _overflowList = null!;
            _prefixList = null!;
        }

        public void Init(IPacket packet, IHuffman huffman)
        {
            // first, check the sync pattern
            ulong bookSignature = packet.ReadBits(24);
            if (bookSignature != 0x564342UL)
                throw new InvalidDataException("Book header has invalid signature.");

            // get the counts
            Dimensions = (int)packet.ReadBits(16);
            Entries = (int)packet.ReadBits(24);

            // init the storage
            _lengths = new int[Entries];

            InitTree(packet, huffman);
            InitLookupTable(packet);

            _prefixList = huffman.PrefixTree;
            _prefixBitLength = huffman.TableBits;
            _overflowList = huffman.OverflowList;
        }

        private void InitTree(IPacket packet, IHuffman huffman)
        {
            int total = 0;
            int maxLength;
            bool sparse;

            if (packet.ReadBit())
            {
                // ordered
                int length = (int)packet.ReadBits(5) + 1;
                for (int i = 0; i < Entries;)
                {
                    int count = (int)packet.ReadBits(Utils.ILog(Entries - i));

                    while (--count >= 0)
                        _lengths[i++] = length;

                    length++;
                }
                total = 0;
                sparse = false;
                maxLength = length;
            }
            else
            {
                // unordered
                maxLength = -1;
                sparse = packet.ReadBit();
                for (int i = 0; i < Entries; i++)
                {
                    if (!sparse || packet.ReadBit())
                    {
                        _lengths[i] = (int)packet.ReadBits(5) + 1;
                        ++total;
                    }
                    else
                    {
                        // mark the entry as unused
                        _lengths[i] = -1;
                    }

                    if (_lengths[i] > maxLength)
                        maxLength = _lengths[i];
                }
            }

            // figure out the maximum bit size; if all are unused, don't do anything else
            if ((_maxBits = maxLength) <= -1)
                return;

            int[]? codewordLengths = null;
            if (sparse && total >= Entries >> 2)
            {
                codewordLengths = new int[Entries];
                Array.Copy(_lengths, codewordLengths, Entries);

                sparse = false;
            }

            // compute size of sorted tables

            int[]? values = null;
            int[] codewords;

            if (sparse)
            {
                codewordLengths = new int[total];
                codewords = new int[total];
                values = new int[total];
            }
            else
            {
                codewords = new int[Entries];
            }

            if (!ComputeCodewords(sparse, codewords, codewordLengths, _lengths, Entries, values))
                throw new InvalidDataException();

            var valueList = (IReadOnlyList<int>?)values ?? ThreadStaticRange.Get(0, codewords.Length);
            huffman.GenerateTable(valueList, codewordLengths ?? _lengths, codewords);
        }

        private static bool ComputeCodewords(
            bool sparse, int[] codewords, int[] codewordLengths, int[] len, int n, int[] values)
        {
            int i, k, m = 0;
            Span<uint> available = stackalloc uint[32];

            for (k = 0; k < n; ++k)
                if (len[k] > 0)
                    break;
            if (k == n)
                return true;

            AddEntry(sparse, codewords, codewordLengths, 0, k, m++, len[k], values);

            for (i = 1; i <= len[k]; ++i)
                available[i] = 1U << (32 - i);

            for (i = k + 1; i < n; ++i)
            {
                uint res;
                int z = len[i], y;
                if (z <= 0)
                    continue;

                while (z > 0 && available[z] == 0)
                    z--;
                if (z == 0)
                    return false;

                res = available[z];
                available[z] = 0;
                AddEntry(sparse, codewords, codewordLengths, Utils.BitReverse(res), i, m++, len[i], values);

                if (z != len[i])
                {
                    for (y = len[i]; y > z; --y)
                        available[y] = res + (1U << (32 - y));
                }
            }

            return true;
        }

        private static void AddEntry(
            bool sparse, int[] codewords, int[] codewordLengths,
            uint huffCode, int symbol, int count, int length, int[] values)
        {
            if (sparse)
            {
                codewords[count] = (int)huffCode;
                codewordLengths[count] = length;
                values[count] = symbol;
            }
            else
            {
                codewords[symbol] = (int)huffCode;
            }
        }

        private void InitLookupTable(IPacket packet)
        {
            MapType = (int)packet.ReadBits(4);
            if (MapType == 0)
                return;

            float minValue = Utils.ConvertFromVorbisFloat32((uint)packet.ReadBits(32));
            float deltaValue = Utils.ConvertFromVorbisFloat32((uint)packet.ReadBits(32));
            int valueBits = (int)packet.ReadBits(4) + 1;
            bool sequence_p = packet.ReadBit();

            int lookupValueCount = Entries * Dimensions;
            var lookupTable = new float[lookupValueCount];
            if (MapType == 1)
                lookupValueCount = Lookup1_values();

            var multiplicands = new uint[lookupValueCount];
            for (int i = 0; i < lookupValueCount; i++)
                multiplicands[i] = (uint)packet.ReadBits(valueBits);

            // now that we have the initial data read in, calculate the entry tree
            if (MapType == 1)
            {
                for (int idx = 0; idx < Entries; idx++)
                {
                    double last = 0.0;
                    int idxDiv = 1;
                    for (int i = 0; i < Dimensions; i++)
                    {
                        int moff = idx / idxDiv % lookupValueCount;
                        double value = multiplicands[moff] * deltaValue + minValue + last;
                        lookupTable[idx * Dimensions + i] = (float)value;

                        if (sequence_p)
                            last = value;

                        idxDiv *= lookupValueCount;
                    }
                }
            }
            else
            {
                for (int idx = 0; idx < Entries; idx++)
                {
                    double last = 0.0;
                    int moff = idx * Dimensions;
                    for (int i = 0; i < Dimensions; i++)
                    {
                        double value = multiplicands[moff] * deltaValue + minValue + last;
                        lookupTable[idx * Dimensions + i] = (float)value;

                        if (sequence_p)
                            last = value;

                        ++moff;
                    }
                }
            }

            _lookupTable = lookupTable;
        }

        private int Lookup1_values()
        {
            int r = (int)Math.Floor(Math.Exp(Math.Log(Entries) / Dimensions));

            if (Math.Floor(Math.Pow(r + 1, Dimensions)) <= Entries)
                r++;

            return r;
        }

        public int DecodeScalar(IPacket packet)
        {
            int data = (int)packet.TryPeekBits(_prefixBitLength, out var bitsRead);
            if (bitsRead == 0)
                return -1;

            // try to get the value from the prefix list...
            var node = _prefixList[data];
            if (node != null)
            {
                packet.SkipBits(node.Length);
                return node.Value;
            }

            // nope, not possible... run through the overflow nodes
            data = (int)packet.TryPeekBits(_maxBits, out _);

            for (int i = 0; i < _overflowList.Count; i++)
            {
                node = _overflowList[i];
                if (node.Bits == (data & node.Mask))
                {
                    packet.SkipBits(node.Length);
                    return node.Value;
                }
            }
            return -1;
        }
    }
}
