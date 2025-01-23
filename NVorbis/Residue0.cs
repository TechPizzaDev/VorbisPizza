using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NVorbis
{
    // each channel gets its own pass, one dimension at a time
    internal class Residue0
    {
        private int _begin;
        private int _end;
        private int _partitionSize;
        private byte _classifications;
        private byte _maxStages;

        private byte[]?[] _books;
        private byte _classBook;

        private byte[] _cascade;
        private int[] _decodeMap;
        private int[]? _partWordCache;

        [SkipLocalsInit]
        public Residue0(ref VorbisPacket packet, Codebook[] codebooks)
        {
            Span<byte> bookNums = stackalloc byte[1024];

            // this is pretty well stolen directly from libvorbis...  BSD license
            _begin = (int)packet.ReadBits(24);
            _end = (int)packet.ReadBits(24);
            _partitionSize = (int)packet.ReadBits(24) + 1;
            _classifications = (byte)(packet.ReadBits(6) + 1);
            _classBook = (byte)packet.ReadBits(8);

            byte[] cascade = new byte[_classifications];
            int acc = 0;
            for (int i = 0; i < cascade.Length; i++)
            {
                uint low_bits = (uint)packet.ReadBits(4);
                uint bits = low_bits & 0b111;
                if ((low_bits & 0b1000) != 0)
                {
                    bits |= (uint)packet.ReadBits(5) << 3;
                }
                cascade[i] = (byte)bits;
                acc += BitOperations.PopCount(bits);
            }
            _cascade = cascade;

            if (acc > bookNums.Length)
                bookNums = new byte[acc];
            else
                bookNums = bookNums.Slice(0, acc);

            for (int i = 0; i < bookNums.Length; i++)
            {
                bookNums[i] = (byte)packet.ReadBits(8);

                if (codebooks[bookNums[i]].MapType == 0)
                    throw new InvalidDataException();
            }

            var classBook = codebooks[_classBook];
            int entries = classBook.Entries;
            int dim = classBook.Dimensions;
            int partvals = 1;
            for (int i = 0; i < dim; i++)
            {
                partvals *= _classifications;
                if (partvals > entries)
                    throw new InvalidDataException();
            }

            // now the lookups
            _books = new byte[_classifications][];

            acc = 0;
            int maxstage = 0;
            int stages;
            for (int j = 0; j < cascade.Length; j++)
            {
                stages = Utils.ilog(cascade[j]);
                if (stages <= 0)
                {
                    continue;
                }

                var bookList = new byte[stages];
                maxstage = Math.Max(maxstage, stages);
                for (int k = 0; k < stages; k++)
                {
                    if ((cascade[j] & (1 << k)) > 0)
                    {
                        bookList[k] = bookNums[acc++];
                    }
                }
                _books[j] = bookList;
            }
            _maxStages = (byte)maxstage;

            _decodeMap = new int[partvals * classBook.Dimensions];
            for (int j = 0; j < partvals; j++)
            {
                int val = j;
                int mult = partvals / _classifications;
                for (int k = 0; k < classBook.Dimensions; k++)
                {
                    int deco = val / mult;
                    val -= deco * mult;
                    mult /= _classifications;
                    _decodeMap[j * classBook.Dimensions + k] = deco;
                }
            }
        }

        public virtual void Decode(
            ref VorbisPacket packet,
            BitStackArray doNotDecodeFlag, int blockSize, ChannelBuffer buffer, Codebook[] books)
        {
            // this is pretty well stolen directly from libvorbis...  BSD license
            int halfSize = blockSize / 2;
            int begin = Math.Min(_begin, halfSize);
            int end = Math.Min(_end, halfSize);
            int n = end - begin;
            if (n <= 0)
            {
                return;
            }

            int[] decodeMap = _decodeMap;
            byte[] cascade = _cascade;
            int partitionCount = n / _partitionSize;

            var classBook = books[_classBook];
            int dim = classBook.Dimensions;
            int partitionWords = (partitionCount + dim - 1) / dim;
            int cacheLength = doNotDecodeFlag.Count * partitionWords;

            if (_partWordCache == null || _partWordCache.Length < cacheLength)
                Array.Resize(ref _partWordCache, cacheLength);
            Span<int> partWordCache = _partWordCache.AsSpan(0, cacheLength);

            for (int stage = 0; stage < _maxStages; stage++)
            {
                for (int partitionIdx = 0, entryIdx = 0; partitionIdx < partitionCount; entryIdx++)
                {
                    if (stage == 0)
                    {
                        for (int ch = 0; ch < doNotDecodeFlag.Count; ch++)
                        {
                            if (doNotDecodeFlag[ch])
                            {
                                continue;
                            }

                            int idx = classBook.DecodeScalar(ref packet);
                            if (idx >= 0 && idx < decodeMap.Length)
                            {
                                partWordCache[ch * partitionWords + entryIdx] = idx;
                            }
                            else
                            {
                                partitionIdx = partitionCount;
                                stage = _maxStages;
                                break;
                            }
                        }
                    }

                    for (int dimIdx = 0; partitionIdx < partitionCount && dimIdx < dim; dimIdx++, partitionIdx++)
                    {
                        int offset = begin + partitionIdx * _partitionSize;
                        for (int ch = 0; ch < doNotDecodeFlag.Count; ch++)
                        {
                            if (doNotDecodeFlag[ch])
                            {
                                continue;
                            }

                            int mapIndex = partWordCache[ch * partitionWords + entryIdx] * dim;
                            int idx = decodeMap[mapIndex + dimIdx];
                            if ((cascade[idx] & (1 << stage)) == 0)
                            {
                                continue;
                            }

                            byte[]? bookList = _books[idx];
                            if (bookList == null)
                            {
                                continue;
                            }
                            Codebook book = books[bookList[stage]];

                            if (WriteVectors(book, ref packet, buffer.GetSpan(ch), offset, _partitionSize))
                            {
                                // bad packet...  exit now and try to use what we already have
                                partitionIdx = partitionCount;
                                stage = _maxStages;
                                break;
                            }
                        }
                    }
                }
            }
        }

        protected virtual bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, Span<float> channelBuf, int offset, int partitionSize)
        {
            int steps = partitionSize / codebook.Dimensions;
            Span<float> res = channelBuf.Slice(offset, steps);

            for (int step = 0; step < steps; step++)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                float r = 0;
                ReadOnlySpan<float> lookup = codebook.GetLookup(entry);
                for (int dim = 0; dim < lookup.Length; dim++)
                {
                    r += lookup[dim];
                }
                res[step] += r;
            }
            return false;
        }
    }
}
