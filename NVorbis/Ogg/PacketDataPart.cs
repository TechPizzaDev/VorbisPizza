namespace NVorbis.Contracts.Ogg
{
    internal readonly struct PacketDataPart
    {
        public readonly uint PageIndex;
        public readonly byte PacketIndex;

        public PacketDataPart(uint pageIndex, byte packetIndex)
        {
            PageIndex = pageIndex;
            PacketIndex = packetIndex;
        }
    }
}
