using NVorbis.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace NVorbis
{
    class Codebook : ICodebook
    {
        public VorbisCodebook(VorbisDataPacket packet, int number)
        {
            [ThreadStatic]
            static FastRange _cachedRange;

            internal static FastRange Get(int start, int count)
            {
                var fr = _cachedRange ?? (_cachedRange = new FastRange());
                fr._start = start;
                fr._count = count;
                return fr;
            }

            int _start;
            int _count;

            private FastRange() { }

            public int this[int index]
            {
                get
                {
                    if (index > _count) throw new ArgumentOutOfRangeException();
                    return _start + index;
                }
            }

            public int Count => _count;

            public IEnumerator<int> GetEnumerator()
            {
                throw new NotSupportedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        int[] _lengths;
        float[] _lookupTable;
        IReadOnlyList<HuffmanListNode> _overflowList;
        IReadOnlyList<HuffmanListNode> _prefixList;
        int _prefixBitLength;
        int _maxBits;

        public void Init(IPacket packet, IHuffman huffman)
        {
            // save off the book number
            BookNum = number;

            // first, check the sync pattern
            ulong chkVal = packet.ReadBits(24);
            if (chkVal != 0x564342UL)
                throw new InvalidDataException("Book header had invalid signature.");

            // get the counts
            Dimensions = (int)packet.ReadBits(16);
            Entries = (int)packet.ReadBits(24);

            // init the storage
            _lengths = new int[Entries];

            InitTree(packet, huffman);
            InitLookupTable(packet);
        }

        private void InitTree(IPacket packet, IHuffman huffman)
        {
            bool sparse;
            int total = 0;

            int maxLen;
            if (packet.ReadBit())
            {
                // ordered
                int length = (int)packet.ReadBits(5) + 1;
                for (int i = 0; i < Entries;)
                {
                    int count = (int)packet.ReadBits(Utils.ILog(Entries - i));
                    while (--count >= 0)
                        Lengths[i++] = length;

                    length++;
                }
                total = 0;
                sparse = false;
                maxLen = len;
            }
            else
            {
                // unordered
                maxLen = -1;
                sparse = packet.ReadBit();
                for (var i = 0; i < Entries; i++)
                {
                    if (!sparse || packet.ReadBit())
                    {
                        _lengths[i] = (int)packet.ReadBits(5) + 1;
                        total++;
                    }
                    else
                    {
                        // mark the entry as unused
                        _lengths[i] = -1;
                    }

                    if (_lengths[i] > maxLen)
                        maxLen = _lengths[i];
                }
            }

            // figure out the maximum bit size; if all are unused, don't do anything else
            if ((_maxBits = maxLen) > -1)
            {
                int[]? codewordLengths = null;
                if (sparse && total >= Entries >> 2)
                {
                    codewordLengths = new int[Entries];
                    Array.Copy(_lengths, codewordLengths, Entries);

                    sparse = false;
                }

                int sortedCount;
                // compute size of sorted tables
                int sortedCount = sparse ? total : 0;
                int sortedEntries = sortedCount;

                int[]? values = null;
                int[]? codewords = null;
                if (!sparse)
                {
                    codewords = new int[Entries];
                }
                else if (sortedCount != 0)
                {
                    codewordLengths = new int[sortedCount];
                    codewords = new int[sortedCount];
                    values = new int[sortedCount];
                }

                if (!ComputeCodewords(
                    sparse, codewords, codewordLengths, Lengths, n: Entries, values))
                    throw new InvalidDataException();

                var lengths = codewordLengths ?? Lengths;

                if (values != null)
                {
                    (InitialPrefixList, PrefixList) = Huffman.BuildPrefixedLinkedList(
                        values, lengths, codewords, out PrefixBitLength, out PrefixOverflowTreeIndex);
                }
                else
                {
                    (InitialPrefixList, PrefixList) = Huffman.BuildPrefixedLinkedList(
                        new Int32Range(0, codewords!.Length),
                        lengths, codewords, out PrefixBitLength, out PrefixOverflowTreeIndex);
                }

                _prefixList = huffman.PrefixTree;
                _prefixBitLength = huffman.TableBits;
                _overflowList = huffman.OverflowList;
            }
        }

        private static bool ComputeCodewords(
            bool sparse, int[] codewords, int[] codewordLengths, int[] lengths, int n, int[] values)
        {
            int k;
            for (k = 0; k < n; ++k)
                if (lengths[k] > 0)
                    break;

            if (k == n)
                return true;

            int m = 0;
            AddEntry(sparse, codewords, codewordLengths, 0, k, m++, lengths[k], values);

            Span<uint> available = stackalloc uint[32];
            int i;
            for (i = 1; i <= lengths[k]; ++i)
                available[i] = 1u << (32 - i);

            for (i = k + 1; i < n; ++i)
            {
                int z = lengths[i];
                if (z <= 0)
                    continue;

                while (z > 0 && available[z] == 0)
                    --z;
                if (z == 0)
                    return false;

                uint res = available[z];
                available[z] = 0;

                int code = (int)Utils.BitReverse(res);
                AddEntry(sparse, codewords, codewordLengths, code, i, m++, lengths[i], values);

                if (z != lengths[i])
                {
                    for (int y = lengths[i]; y > z; --y)
                        available[y] = res + (1u << (32 - y));
                }
            }

            return true;
        }

        private static void AddEntry(
            bool sparse, int[] codewords, int[] codewordLengths,
            int huffCode, int symbol, int count, int len, int[] values)
        {
            if (sparse)
            {
                codewords[count] = huffCode;
                codewordLengths[count] = len;
                values[count] = symbol;
            }
            else
            {
                codewords[symbol] = huffCode;
            }
        }

        private void InitLookupTable(IPacket packet)
        {
            MapType = (int)packet.ReadBits(4);
            if (MapType == 0)
                return;

            float minValue = Utils.ConvertFromVorbisFloat32(packet.ReadUInt32());
            float deltaValue = Utils.ConvertFromVorbisFloat32(packet.ReadUInt32());
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
                    for (var i = 0; i < Dimensions; i++)
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

        int Lookup1_values()
        {
            int r = (int)Math.Floor(Math.Exp(Math.Log(Entries) / Dimensions));

            if (Math.Floor(Math.Pow(r + 1, Dimensions)) <= Entries)
                r++;

            return r;
        }

        public int DecodeScalar(IPacket packet)
        {
            ulong bits = packet.TryPeekBits(PrefixBitLength, out int bitCnt);
            if (bitCnt == 0)
                return -1;

            // try to get the value from the prefix list...
            ref readonly var node = ref PrefixList[bits];
            if (node.HasValue)
            {
                packet.SkipBits(node.Length);
                return node.Value;
            }

            if (!PrefixOverflowTreeIndex.HasValue)
                throw new InvalidDataException("Missing prefix overflow tree index.");

            // nope, not possible... run the tree
            int bits32 = (int)packet.TryPeekBits(MaxBits, out _);

            for (var i = 0; i < _overflowList.Count; i++)
            {
                node = ref _overflowList[i];
                if (node.Bits == (data & node.Mask))
                {
                    packet.SkipBits(node.Length);
                    return node.Value;
                }
            }
            return -1;
        }

        public float this[int entry, int dim] => _lookupTable[entry * Dimensions + dim];

        public int Dimensions { get; private set; }
        public int Entries { get; private set; }
        public int MapType { get; private set; }
    }
}
