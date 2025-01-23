using System;

namespace NVorbis.Contracts
{
    internal interface IFloor
    {
        FloorData CreateFloorData();

        void Unpack(ref VorbisPacket packet, FloorData floorData, int channel, Codebook[] books);

        void Apply(FloorData floorData, int blockSize, Span<float> residue);
    }
}
