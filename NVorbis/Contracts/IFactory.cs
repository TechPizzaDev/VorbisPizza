namespace NVorbis.Contracts
{
    interface IFactory
    {
        Codebook CreateCodebook();
        IFloor CreateFloor(DataPacket packet);
        IResidue CreateResidue(DataPacket packet);
        IMapping CreateMapping(DataPacket packet);
        IMode CreateMode();
        IMdct CreateMdct();
        Huffman CreateHuffman();
    }
}
