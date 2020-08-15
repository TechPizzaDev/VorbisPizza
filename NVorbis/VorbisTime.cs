﻿/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System.IO;

namespace NVorbis
{
    internal abstract class VorbisTime
    {
        internal static VorbisTime Init(VorbisStreamDecoder vorbis, VorbisDataPacket packet)
        {
            ulong type = packet.ReadBits(16);

            VorbisTime? time = null;
            switch (type)
            {
                case 0:
                    time = new Time0(vorbis);
                    break;
            }

            if (time == null)
                throw new InvalidDataException();

            time.Init(packet);
            return time;
        }

        private VorbisStreamDecoder _vorbis;

        protected VorbisTime(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        protected abstract void Init(VorbisDataPacket packet);

        private class Time0 : VorbisTime
        {
            internal Time0(VorbisStreamDecoder vorbis) : base(vorbis)
            {
            }

            protected override void Init(VorbisDataPacket packet)
            {
            }
        }
    }
}
