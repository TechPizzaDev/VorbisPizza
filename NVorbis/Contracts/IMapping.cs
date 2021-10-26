namespace NVorbis.Contracts
{
    interface IMapping
    {
        void Init(DataPacket packet, int channels, IFloor[] floors, IResidue[] residues, IMdct mdct);

        void DecodePacket(DataPacket packet, int blockSize, int channels, float[][] buffer);
    }
}
