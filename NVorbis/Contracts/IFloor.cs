namespace NVorbis.Contracts
{
    interface IFloor
    {
        void Init(DataPacket packet, int channels, int block0Size, int block1Size, Codebook[] codebooks);

        IFloorData CreateFloorData();

        void Unpack(DataPacket packet, IFloorData floorData, int blockSize, int channel);

        void Apply(IFloorData floorData, int blockSize, float[] residue);
    }
}
