namespace NVorbis.Contracts.Ogg
{
    internal readonly struct PacketDataPart
    {
        public uint PageIndex { get; }
        public byte PacketIndex { get; }

        public PacketDataPart(uint pageIndex, byte packetIndex)
        {
            PageIndex = pageIndex;
            PacketIndex = packetIndex;
        }
    }
}
