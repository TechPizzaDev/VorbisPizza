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
    abstract partial class VorbisFloor
    {
        internal static VorbisFloor Init(
            VorbisStreamDecoder vorbis, VorbisDataPacket packet)
        {
            var type = (int)packet.ReadBits(16);

            VorbisFloor floor = null;
            switch (type)
            {
                case 0: floor = new Floor0(vorbis); break;
                case 1: floor = new Floor1(vorbis); break;
            }

            if (floor == null)
                throw new InvalidDataException();

            floor.Init(packet);
            return floor;
        }

        VorbisStreamDecoder _vorbis;

        protected VorbisFloor(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        abstract protected void Init(VorbisDataPacket packet);

        abstract internal PacketData UnpackPacket(
            VorbisDataPacket packet, int blockSize, int channel);

        abstract internal void Apply(PacketData packetData, float[] residue);

        abstract internal class PacketData
        {
            internal int BlockSize;
            abstract protected bool HasEnergy { get; }
            internal bool ForceEnergy { get; set; }
            internal bool ForceNoEnergy { get; set; }

            internal bool ExecuteChannel => (ForceEnergy | HasEnergy) & !ForceNoEnergy;
        }
    }
}