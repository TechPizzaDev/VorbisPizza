/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Linq;
using System.IO;

namespace NVorbis
{
    abstract partial class VorbisResidue
    {
        // residue type 0... samples are grouped by channel, then stored with non-interleaved dimensions;
        // (d0, d0, d0, d0, ..., d1, d1, d1, d1, ..., d2, d2, d2, d2, etc...)
        class Residue0 : VorbisResidue
        {
            int _begin;
            int _end;
            int _partitionSize;
            int _classifications;
            int _maxStages;

            VorbisCodebook[][] _books;
            VorbisCodebook _classBook;

            int[] _cascade, _entryCache;
            int[][] _decodeMap;
            int[][][] _partWordCache;

            internal Residue0(VorbisStreamDecoder vorbis) : base(vorbis) { }

            protected override void Init(VorbisDataPacket packet)
            {
                // this is pretty well stolen directly from libvorbis...  BSD license
                _begin = (int)packet.ReadBits(24);
                _end = (int)packet.ReadBits(24);
                _partitionSize = (int)packet.ReadBits(24) + 1;
                _classifications = (int)packet.ReadBits(6) + 1;
                _classBook = _vorbis.Books[(int)packet.ReadBits(8)];

                _cascade = new int[_classifications];
                int acc = 0;
                for (int i = 0; i < _classifications; i++)
                {
                    var low_bits = (int)packet.ReadBits(3);
                    if (packet.ReadBit())
                    {
                        _cascade[i] = (int)packet.ReadBits(5) << 3 | low_bits;
                    }
                    else
                    {
                        _cascade[i] = low_bits;
                    }
                    acc += ICount(_cascade[i]);
                }

                var bookNums = new int[acc];
                for (int i = 0; i < acc; i++)
                {
                    bookNums[i] = (int)packet.ReadBits(8);
                    if (_vorbis.Books[bookNums[i]].MapType == 0)
                        throw new InvalidDataException();
                }

                int entries = _classBook.Entries;
                int dim = _classBook.Dimensions;
                int partvals = 1;
                while (dim > 0)
                {
                    partvals *= _classifications;
                    if (partvals > entries)
                        throw new InvalidDataException();
                    --dim;
                }

                _books = new VorbisCodebook[_classifications][];

                acc = 0;
                int maxstage = 0;
                int stages;
                for (int j = 0; j < _classifications; j++)
                {
                    stages = Utils.ILog(_cascade[j]);
                    _books[j] = new VorbisCodebook[stages];
                    if (stages > 0)
                    {
                        maxstage = Math.Max(maxstage, stages);
                        for (int k = 0; k < stages; k++)
                        {
                            if ((_cascade[j] & (1 << k)) > 0)
                            {
                                _books[j][k] = _vorbis.Books[bookNums[acc++]];
                            }
                        }
                    }
                }
                _maxStages = maxstage;

                _decodeMap = new int[partvals][];
                for (int j = 0; j < partvals; j++)
                {
                    int val = j;
                    int mult = partvals / _classifications;
                    _decodeMap[j] = new int[_classBook.Dimensions];
                    for (int k = 0; k < _classBook.Dimensions; k++)
                    {
                        int deco = val / mult;
                        val -= deco * mult;
                        mult /= _classifications;
                        _decodeMap[j][k] = deco;
                    }
                }

                _entryCache = new int[_partitionSize];

                _partWordCache = new int[_vorbis._channels][][];
                var maxPartWords = ((_end - _begin) / _partitionSize + _classBook.Dimensions - 1) / _classBook.Dimensions;
                for (int ch = 0; ch < _vorbis._channels; ch++)
                    _partWordCache[ch] = new int[maxPartWords][];
            }

            internal override float[][] Decode(
                VorbisDataPacket packet, bool[] doNotDecode, int channels, int blockSize)
            {
                var residue = GetResidueBuffer(doNotDecode.Length);

                // this is pretty well stolen directly from libvorbis...  BSD license
                int end = _end < blockSize / 2 ? _end : blockSize / 2;
                int n = end - _begin;

                if (n > 0 && doNotDecode.Contains(false))
                {
                    int partVals = n / _partitionSize;
                    int partWords = (partVals + _classBook.Dimensions - 1) / _classBook.Dimensions;

                    for (int j = 0; j < channels; j++)
                        Array.Clear(_partWordCache[j], 0, partWords);

                    for (int s = 0; s < _maxStages; s++)
                    {
                        for (int i = 0, l = 0; i < partVals; l++)
                        {
                            if (s == 0)
                            {
                                for (int j = 0; j < channels; j++)
                                {
                                    int idx = _classBook.DecodeScalar(packet);
                                    if (idx >= 0 && idx < _decodeMap.Length)
                                    {
                                        _partWordCache[j][l] = _decodeMap[idx];
                                    }
                                    else
                                    {
                                        i = partVals;
                                        s = _maxStages;
                                        break;
                                    }
                                }
                            }
                            for (int k = 0; i < partVals && k < _classBook.Dimensions; k++, i++)
                            {
                                int offset = _begin + i * _partitionSize;
                                for (int j = 0; j < channels; j++)
                                {
                                    int idx = _partWordCache[j][l][k];
                                    if ((_cascade[idx] & (1 << s)) != 0)
                                    {
                                        var book = _books[idx][s];
                                        if (book != null)
                                        {
                                            if (WriteVectors(book, packet, residue, j, offset, _partitionSize))
                                            {
                                                // bad packet... exit now and try to use what we already have
                                                i = partVals;
                                                s = _maxStages;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return residue;
            }

            virtual protected bool WriteVectors(
                VorbisCodebook codebook, VorbisDataPacket packet, float[][] residue,
                int channel, int offset, int partitionSize)
            {
                var res = residue[channel];
                var step = partitionSize / codebook.Dimensions;

                for (int i = 0; i < step; i++)
                {
                    if ((_entryCache[i] = codebook.DecodeScalar(packet)) == -1)
                        return true;
                }
                for (int i = 0; i < codebook.Dimensions; i++)
                {
                    for (int j = 0; j < step; j++, offset++)
                        res[offset] += codebook[_entryCache[j], i];
                }
                return false;
            }
        }
    }
}
