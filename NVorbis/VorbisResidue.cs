/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.IO;

namespace NVorbis
{
    abstract partial class VorbisResidue
    {
        internal static VorbisResidue Init(
            VorbisStreamDecoder vorbis, VorbisDataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisResidue residue = null;
            switch (type)
            {
                case 0: residue = new Residue0(vorbis); break;
                case 1: residue = new Residue1(vorbis); break;
                case 2: residue = new Residue2(vorbis); break;
            }
            if (residue == null) throw new InvalidDataException();

            residue.Init(packet);
            return residue;
        }

        static int ICount(int v)
        {
            var ret = 0;
            while (v != 0)
            {
                ret += (v & 1);
                v >>= 1;
            }
            return ret;
        }

        VorbisStreamDecoder _vorbis;
        float[][] _residue;

        protected VorbisResidue(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;

            _residue = new float[_vorbis._channels][];
            for (int i = 0; i < _vorbis._channels; i++)
            {
                _residue[i] = new float[_vorbis.Block1Size];
            }
        }

        protected float[][] GetResidueBuffer(int channels)
        {
            var temp = _residue;
            if (channels < _vorbis._channels)
            {
                temp = new float[channels][];
                Array.Copy(_residue, temp, channels);
            }
            for (int i = 0; i < channels; i++)
            {
                Array.Clear(temp[i], 0, temp[i].Length);
            }
            return temp;
        }

        abstract internal float[][] Decode(
            VorbisDataPacket packet, bool[] doNotDecode, int channels, int blockSize);

        abstract protected void Init(VorbisDataPacket packet);
    }
}
