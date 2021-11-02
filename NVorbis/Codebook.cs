using NVorbis.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis
{
    class Codebook
    {
        struct FastRange : IReadOnlyList<int>
        {
            int _start;
            int _count;

            public FastRange(int start, int count)
            {
                _start = start;
                _count = count;
            }

            public int this[int index]
            {
                get
                {
                    Debug.Assert((uint)index < (uint)_count);
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
        HuffmanListNode[] _overflowList;
        HuffmanListNode[] _prefixList;
        int _prefixBitLength;
        int _maxBits;

        public void Init(DataPacket packet, Huffman huffman)
        {
            // first, check the sync pattern
            var chkVal = packet.ReadBits(24);
            if (chkVal != 0x564342UL) throw new InvalidDataException("Book header had invalid signature!");

            // get the counts
            Dimensions = (int)packet.ReadBits(16);
            Entries = (int)packet.ReadBits(24);

            // init the storage
            _lengths = new int[Entries];

            InitTree(packet, huffman);
            InitLookupTable(packet);
        }

        private void InitTree(DataPacket packet, Huffman huffman)
        {
            bool sparse;
            int total = 0;

            int maxLen;
            if (packet.ReadBit())
            {
                // ordered
                var len = (int)packet.ReadBits(5) + 1;
                for (var i = 0; i < Entries;)
                {
                    var cnt = (int)packet.ReadBits(Utils.ilog(Entries - i));

                    while (--cnt >= 0)
                    {
                        _lengths[i++] = len;
                    }

                    ++len;
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
                        ++total;
                    }
                    else
                    {
                        // mark the entry as unused
                        _lengths[i] = -1;
                    }
                    if (_lengths[i] > maxLen)
                    {
                        maxLen = _lengths[i];
                    }
                }
            }

            // figure out the maximum bit size; if all are unused, don't do anything else
            if ((_maxBits = maxLen) > -1)
            {
                int[] codewordLengths = null;
                if (sparse && total >= Entries >> 2)
                {
                    codewordLengths = new int[Entries];
                    Array.Copy(_lengths, codewordLengths, Entries);

                    sparse = false;
                }

                int sortedCount;
                // compute size of sorted tables
                if (sparse)
                {
                    sortedCount = total;
                }
                else
                {
                    sortedCount = 0;
                }

                int[] values = null;
                int[] codewords = null;
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

                if (!ComputeCodewords(sparse, codewords, codewordLengths, _lengths.AsSpan(0, Entries), values))
                    throw new InvalidDataException();

                var lengthList = codewordLengths ?? _lengths;
                if (values != null)
                    huffman.GenerateTable(values, lengthList, codewords);
                else
                    huffman.GenerateTable(new FastRange(0, codewords.Length), lengthList, codewords);

                _prefixList = huffman.PrefixTree;
                _prefixBitLength = huffman.TableBits;
                _overflowList = huffman.OverflowList;
            }
        }

        [SkipLocalsInit]
        bool ComputeCodewords(bool sparse, int[] codewords, int[] codewordLengths, ReadOnlySpan<int> len, int[] values)
        {
            int i, k, m = 0;
            Span<uint> available = stackalloc uint[32];
            available.Clear();

            for (k = 0; k < len.Length; ++k) if (len[k] > 0) break;
            if (k == len.Length) return true;

            AddEntry(0, k, m++, len[k]);

            for (i = 1; i <= len[k]; ++i) available[i] = 1U << (32 - i);

            for (i = k + 1; i < len.Length; ++i)
            {
                uint res;
                int z = len[i], y;
                if (z <= 0) continue;

                while (z > 0 && available[z] == 0) --z;
                if (z == 0) return false;
                res = available[z];
                available[z] = 0;
                AddEntry(Utils.BitReverse(res), i, m++, len[i]);

                if (z != len[i])
                {
                    for (y = len[i]; y > z; --y)
                    {
                        available[y] = res + (1U << (32 - y));
                    }
                }
            }

            return true;

            void AddEntry(uint huffCode, int symbol, int count, int len)
            {
                if (sparse)
                {
                    codewords[count] = (int)huffCode;
                    codewordLengths[count] = len;
                    values[count] = symbol;
                }
                else
                {
                    codewords[symbol] = (int)huffCode;
                }
            }
        }

        private void InitLookupTable(DataPacket packet)
        {
            MapType = (int)packet.ReadBits(4);
            if (MapType == 0) return;

            var minValue = Utils.ConvertFromVorbisFloat32((uint)packet.ReadBits(32));
            var deltaValue = Utils.ConvertFromVorbisFloat32((uint)packet.ReadBits(32));
            var valueBits = (int)packet.ReadBits(4) + 1;
            var sequence_p = packet.ReadBit();

            int entries = Entries;
            int dimensions = Dimensions;
            var lookupValueCount = entries * dimensions;
            var lookupTable = new float[lookupValueCount];
            ref float lookup = ref lookupTable[0];

            if (MapType == 1)
            {
                lookupValueCount = lookup1_values();
            }

            var multiplicands = new ushort[lookupValueCount];
            for (var i = 0; i < multiplicands.Length; i++)
            {
                multiplicands[i] = (ushort)packet.ReadBits(valueBits);
            }
            ref ushort muls = ref multiplicands[0];

            // now that we have the initial data read in, calculate the entry tree
            if (MapType == 1)
            {
                for (var idx = 0; idx < entries; idx++)
                {
                    var last = 0f;
                    var idxDiv = 1;
                    ref float dimLookup = ref Unsafe.Add(ref lookup, idx * dimensions);

                    for (var i = 0; i < dimensions; i++)
                    {
                        var moff = (idx / idxDiv) % lookupValueCount;
                        var value = Unsafe.Add(ref muls, moff) * deltaValue + minValue + last;
                        Unsafe.Add(ref dimLookup, i) = value;

                        if (sequence_p) last = value;

                        idxDiv *= lookupValueCount;
                    }
                }
            }
            else
            {
                for (var idx = 0; idx < entries; idx++)
                {
                    var last = 0f;
                    nint moff = idx * dimensions;
                    ref float dimLookup = ref Unsafe.Add(ref lookup, idx * dimensions);

                    for (var i = 0; i < dimensions; i++)
                    {
                        var value = Unsafe.Add(ref muls, moff) * deltaValue + minValue + last;
                        Unsafe.Add(ref dimLookup, i) = value;

                        if (sequence_p) last = value;

                        ++moff;
                    }
                }
            }

            _lookupTable = lookupTable;
        }

        int lookup1_values()
        {
            var r = (int)Math.Floor(Math.Exp(Math.Log(Entries) / Dimensions));

            if (Math.Floor(Math.Pow(r + 1, Dimensions)) <= Entries) ++r;

            return r;
        }

        public int DecodeScalar(DataPacket packet)
        {
            var data = (int)packet.TryPeekBits(_prefixBitLength, out var bitsRead);
            if (bitsRead == 0) return -1;

            // try to get the value from the prefix list...
            var node = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_prefixList), data);
            if (node.Length != 0)
            {
                packet.SkipBits(node.Length);
                return node.Value;
            }

            // nope, not possible... run through the overflow nodes
            return DecodeOverflowScalar(packet);
        }

        private int DecodeOverflowScalar(DataPacket packet)
        {
            var data = (int)packet.TryPeekBits(_maxBits, out _);

            var overflowList = _overflowList;
            for (var i = 0; i < overflowList.Length; i++)
            {
                ref var node = ref overflowList[i];
                if (node.Bits == (data & node.Mask))
                {
                    packet.SkipBits(node.Length);
                    return node.Value;
                }
            }
            return -1;
        }

        public ReadOnlySpan<float> GetLookup(int entry)
        {
            return _lookupTable.AsSpan(entry * Dimensions, Dimensions);
        }

        public int Dimensions { get; private set; }

        public int Entries { get; private set; }

        public int MapType { get; private set; }
    }
}
