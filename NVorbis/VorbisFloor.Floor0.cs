/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis
{
    internal abstract partial class VorbisFloor
    {
        private sealed class Floor0 : VorbisFloor
        {
            private int _order, _rate, _barkMapSize, _ampBits, _ampOfs, _ampDiv;
            private VorbisCodebook[] _books;
            private int _bookBits;
            private Dictionary<int, float[]> _wMap;
            private Dictionary<int, int[]> _barkMaps;

            internal Floor0(VorbisStreamDecoder vorbis) : base(vorbis)
            {
            }

            protected override void Init(VorbisDataPacket packet)
            {
                // this is pretty well stolen directly from libvorbis...  BSD license
                _order = (int)packet.ReadBits(8);
                _rate = (int)packet.ReadBits(16);
                _barkMapSize = (int)packet.ReadBits(16);
                _ampBits = (int)packet.ReadBits(6);
                _ampOfs = (int)packet.ReadBits(8);
                _books = new VorbisCodebook[(int)packet.ReadBits(4) + 1];

                if (_order < 1 || _rate < 1 || _barkMapSize < 1 || _books.Length == 0)
                    throw new InvalidDataException();

                _ampDiv = (1 << _ampBits) - 1;

                for (int i = 0; i < _books.Length; i++)
                {
                    int num = (int)packet.ReadBits(8);
                    if (num < 0 || num >= _vorbis.Books.Length)
                        throw new InvalidDataException();
                    var book = _vorbis.Books[num];

                    if (book.MapType == 0 || book.Dimensions < 1)
                        throw new InvalidDataException();

                    _books[i] = book;
                }
                _bookBits = Utils.ILog(_books.Length);

                _barkMaps = new Dictionary<int, int[]>();
                _barkMaps[_vorbis.Block0Size] = SynthesizeBarkCurve(_vorbis.Block0Size / 2);
                _barkMaps[_vorbis.Block1Size] = SynthesizeBarkCurve(_vorbis.Block1Size / 2);

                _wMap = new Dictionary<int, float[]>();
                _wMap[_vorbis.Block0Size] = SynthesizeWDelMap(_vorbis.Block0Size / 2);
                _wMap[_vorbis.Block1Size] = SynthesizeWDelMap(_vorbis.Block1Size / 2);

                _reusablePacketData = new PacketData0[_vorbis._channels];
                for (int i = 0; i < _reusablePacketData.Length; i++)
                    _reusablePacketData[i] = new PacketData0(_order + 1);
            }

            private int[] SynthesizeBarkCurve(int n)
            {
                float scale = _barkMapSize / ToBARK(_rate / 2);
                var map = new int[n + 1];

                for (int i = 0; i < n - 1; i++)
                    map[i] = Math.Min(_barkMapSize - 1, (int)Math.Floor(ToBARK(_rate / 2f / n * i) * scale));

                map[n] = -1;
                return map;
            }

            private static float ToBARK(double lsp)
            {
                return (float)(
                    13.1 * Math.Atan(0.00074 * lsp) +
                    2.24 * Math.Atan(0.0000000185 * lsp * lsp) +
                    0.0001 * lsp);
            }

            private float[] SynthesizeWDelMap(int n)
            {
                float wdel = (float)(Math.PI / _barkMapSize);
                var map = new float[n];

                for (int i = 0; i < n; i++)
                    map[i] = 2f * MathF.Cos(wdel * i);

                return map;
            }

            private sealed class PacketData0 : PacketData
            {
                protected override bool HasEnergy => Amp > 0f;

                internal float[] Coeff;
                internal float Amp;

                public PacketData0(int coeffLength)
                {
                    Coeff = new float[coeffLength];
                }
            }

            private PacketData0[] _reusablePacketData;

            internal override PacketData UnpackPacket(
                VorbisDataPacket packet, int blockSize, int channel)
            {
                var data = _reusablePacketData[channel];
                data.BlockSize = blockSize;
                data.ForceEnergy = false;
                data.ForceNoEnergy = false;

                data.Amp = packet.ReadBits(_ampBits);
                if (data.Amp > 0f)
                {
                    // this is pretty well stolen directly from libvorbis...  BSD license
                    Array.Clear(data.Coeff, 0, data.Coeff.Length);

                    data.Amp = data.Amp / _ampDiv * _ampOfs;

                    var bookNum = (uint)packet.ReadBits(_bookBits);
                    if (bookNum >= _books.Length)
                    {
                        // we ran out of data or the packet is corrupt...  0 the floor and return
                        data.Amp = 0;
                        return data;
                    }
                    var book = _books[bookNum];

                    // first, the book decode...
                    for (int i = 0; i < _order;)
                    {
                        var entry = book.DecodeScalar(packet);
                        if (entry == -1)
                        {
                            // we ran out of data or the packet is corrupt...  0 the floor and return
                            data.Amp = 0;
                            return data;
                        }

                        for (int j = 0; i < _order && j < book.Dimensions; j++, i++)
                            data.Coeff[i] = book[entry, j];
                    }

                    // then, the "averaging"
                    var last = 0f;
                    for (int j = 0; j < _order;)
                    {
                        for (int k = 0; j < _order && k < book.Dimensions; j++, k++)
                            data.Coeff[j] += last;

                        last = data.Coeff[j - 1];
                    }
                }
                return data;
            }

            internal override void Apply(PacketData packetData, float[] residue)
            {
                if (!(packetData is PacketData0 data))
                    throw new ArgumentException("Incorrect packet data!", nameof(packetData));

                int n = data.BlockSize / 2;

                if (data.Amp > 0f)
                {
                    // this is pretty well stolen directly from libvorbis...  BSD license
                    var barkMap = _barkMaps[data.BlockSize];
                    var wMap = _wMap[data.BlockSize];

                    int i;
                    for (i = 0; i < _order; i++)
                        data.Coeff[i] = 2f * MathF.Cos(data.Coeff[i]);

                    i = 0;
                    while (i < n)
                    {
                        int j;
                        int k = barkMap[i];
                        float p = .5f;
                        float q = .5f;
                        float w = wMap[k];

                        for (j = 1; j < _order; j += 2)
                        {
                            q *= w - data.Coeff[j - 1];
                            p *= w - data.Coeff[j];
                        }

                        if (j == _order)
                        {
                            // odd order filter; slightly assymetric
                            q *= w - data.Coeff[j - 1];
                            p *= p * (4f - w * w);
                            q *= q;
                        }
                        else
                        {
                            // even order filter; still symetric
                            p *= p * (2f - w);
                            q *= q * (2f + w);
                        }

                        // calc the dB of this bark section
                        q = data.Amp / MathF.Sqrt(p + q) - _ampOfs;

                        // now convert to a linear sample multiplier
                        q = MathF.Exp(q * 0.11512925f);

                        residue[i] *= q;

                        while (barkMap[++i] == k)
                            residue[i] *= q;
                    }
                }
                else
                {
                    Array.Clear(residue, 0, n);
                }
            }
        }
    }
}