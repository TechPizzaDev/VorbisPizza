/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System.IO;

namespace NVorbis
{
    abstract partial class VorbisMapping
    {
        internal static VorbisMapping Init(
            VorbisStreamDecoder vorbis, VorbisDataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisMapping mapping = null;
            switch (type)
            {
                case 0: mapping = new Mapping0(vorbis); break;
            }

            if (mapping == null)
                throw new InvalidDataException();

            mapping.Init(packet);
            return mapping;
        }

        VorbisStreamDecoder _vorbis;

        protected VorbisMapping(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        protected abstract void Init(VorbisDataPacket packet);

        internal Submap[] Submaps;
        internal Submap[] ChannelSubmap;
        internal CouplingStep[] CouplingSteps;

        internal class Submap
        {
            public VorbisFloor Floor { get; }
            public VorbisResidue Residue { get; }

            public Submap(VorbisFloor floor, VorbisResidue residue)
            {
                Floor = floor;
                Residue = residue;
            }
        }

        internal readonly struct CouplingStep
        {
            public int Magnitude { get; }
            public int Angle { get; }

            public CouplingStep(int magnitude, int angle)
            {
                Magnitude = magnitude;
                Angle = angle;
            }
        }
    }
}
