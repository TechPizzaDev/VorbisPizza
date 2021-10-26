namespace NVorbis.Contracts
{
    interface IMode
    {
        int BlockSize { get; }
        float[][] Windows { get; }

        void Init(DataPacket packet, int channels, int block0Size, int block1Size, IMapping[] mappings);

        bool Decode(DataPacket packet, float[][] buffer, out int packetStartindex, out int packetValidLength, out int packetTotalLength);

        int GetPacketSampleCount(DataPacket packet, bool isLastInPage);
    }
}
