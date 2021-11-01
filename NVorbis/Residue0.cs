﻿using NVorbis.Contracts;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace NVorbis
{
    // each channel gets its own pass, one dimension at a time
    class Residue0 : IResidue
    {
        static int icount(int v)
        {
            var ret = 0;
            while (v != 0)
            {
                ret += (v & 1);
                v >>= 1;
            }
            return ret;
        }

        int _channels;
        int _begin;
        int _end;
        int _partitionSize;
        int _classifications;
        int _maxStages;

        Codebook[][] _books;
        Codebook _classBook;

        byte[] _cascade;
        int[] _decodeMap; 
        int[] _partWordCache;

        [SkipLocalsInit]
        virtual public void Init(DataPacket packet, int channels, Codebook[] codebooks)
        {
            // this is pretty well stolen directly from libvorbis...  BSD license
            _begin = (int)packet.ReadBits(24);
            _end = (int)packet.ReadBits(24);
            _partitionSize = (int)packet.ReadBits(24) + 1;
            _classifications = (int)packet.ReadBits(6) + 1;
            _classBook = codebooks[(int)packet.ReadBits(8)];

            var cascade = new byte[_classifications];
            _cascade = cascade;

            var acc = 0;
            for (int i = 0; i < cascade.Length; i++)
            {
                var low_bits = (byte)packet.ReadBits(3);
                if (packet.ReadBit())
                {
                    cascade[i] = (byte)(packet.ReadBits(5) << 3 | low_bits);
                }
                else
                {
                    cascade[i] = low_bits;
                }
                acc += icount(cascade[i]);
            }

            var bookNums = acc < 1024 ? stackalloc byte[acc] : new byte[acc];
            for (var i = 0; i < bookNums.Length; i++)
            {
                bookNums[i] = (byte)packet.ReadBits(8);
                if (codebooks[bookNums[i]].MapType == 0) throw new InvalidDataException();
            }

            var entries = _classBook.Entries;
            var dim = _classBook.Dimensions;
            var partvals = 1;
            while (dim > 0)
            {
                partvals *= _classifications;
                if (partvals > entries) throw new InvalidDataException();
                --dim;
            }

            // now the lookups
            _books = new Codebook[_classifications][];

            acc = 0;
            var maxstage = 0;
            int stages;
            for (int j = 0; j < cascade.Length; j++)
            {
                stages = Utils.ilog(cascade[j]);
                _books[j] = new Codebook[stages];
                if (stages > 0)
                {
                    maxstage = Math.Max(maxstage, stages);
                    for (int k = 0; k < stages; k++)
                    {
                        if ((cascade[j] & (1 << k)) > 0)
                        {
                            _books[j][k] = codebooks[bookNums[acc++]];
                        }
                    }
                }
            }
            _maxStages = maxstage;

            _decodeMap = new int[partvals * _classBook.Dimensions];
            for (int j = 0; j < partvals; j++)
            {
                var val = j;
                var mult = partvals / _classifications;
                for (int k = 0; k < _classBook.Dimensions; k++)
                {
                    var deco = val / mult;
                    val -= deco * mult;
                    mult /= _classifications;
                    _decodeMap[j * _classBook.Dimensions + k] = deco;
                }
            }

            _channels = channels;
        }

        virtual public void Decode(
            DataPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, float[][] buffer)
        {
            // this is pretty well stolen directly from libvorbis...  BSD license
            var end = _end < blockSize / 2 ? _end : blockSize / 2;
            var n = end - _begin;
            
            if (n > 0 && doNotDecodeChannel.IndexOf(false) != -1)
            {
                var channels = _channels;
                var decodeMap = _decodeMap;
                var cascade = _cascade;
                var partitionCount = n / _partitionSize;

                var partitionWords = (partitionCount + _classBook.Dimensions - 1) / _classBook.Dimensions;
                int cacheLength = channels * partitionWords;

                if (_partWordCache == null || _partWordCache.Length < cacheLength)
                    Array.Resize(ref _partWordCache, cacheLength);
                Span<int> partWordCache = _partWordCache.AsSpan(0, cacheLength);

                for (var stage = 0; stage < _maxStages; stage++)
                {
                    for (int partitionIdx = 0, entryIdx = 0; partitionIdx < partitionCount; entryIdx++)
                    {
                        if (stage == 0)
                        {
                            for (var ch = 0; ch < channels; ch++)
                            {
                                var idx = _classBook.DecodeScalar(packet);
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

                        for (var dimensionIdx = 0; partitionIdx < partitionCount && dimensionIdx < _classBook.Dimensions; dimensionIdx++, partitionIdx++)
                        {
                            var offset = _begin + partitionIdx * _partitionSize;
                            for (var ch = 0; ch < channels; ch++)
                            {
                                var mapIndex = partWordCache[ch * partitionWords + entryIdx] * _classBook.Dimensions;
                                var idx = decodeMap[mapIndex + dimensionIdx];
                                if ((cascade[idx] & (1 << stage)) != 0)
                                {
                                    var book = _books[idx][stage];
                                    if (book != null)
                                    {
                                        if (WriteVectors(book, packet, buffer, ch, offset, _partitionSize))
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
                }
            }
        }

        virtual protected bool WriteVectors(Codebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            var res = residue[channel];
            var steps = partitionSize / codebook.Dimensions;

            for (var step = 0; step < steps; step++, offset++)
            {
                int entry = codebook.DecodeScalar(packet);
                if (entry == -1)
                {
                    return true;
                }

                float r = 0;
                var lookup = codebook.GetLookup(entry);
                for (var dim = 0; dim < lookup.Length; dim++)
                {
                    r += lookup[dim];
                }
                res[offset] += r;
            }
            return false;
        }
    }
}
