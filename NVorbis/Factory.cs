using NVorbis.Contracts;

namespace NVorbis
{
    internal class Factory : IFactory
    {
        public IHuffman CreateHuffman()
        {
            return new Huffman();
        }

        public IMdct CreateMdct()
        {
            return new Mdct();
        }

        public ICodebook CreateCodebook()
        {
            return new Codebook();
        }

        public IFloor CreateFloor(IPacket packet)
        {
            int type = (int)packet.ReadBits(16);
            return type switch
            {
                0 => new Floor0(),
                1 => new Floor1(),
                _ => throw new System.IO.InvalidDataException("Invalid floor type."),
            };
        }

        public IMapping CreateMapping(IPacket packet)
        {
            if (packet.ReadBits(16) != 0)
                throw new System.IO.InvalidDataException("Invalid mapping type.");

            return new Mapping();
        }

        public IMode CreateMode()
        {
            return new Mode();
        }

        public IResidue CreateResidue(IPacket packet)
        {
            var type = (int)packet.ReadBits(16);
            return type switch
            {
                0 => new Residue0(),
                1 => new Residue1(),
                2 => new Residue2(),
                _ => throw new System.IO.InvalidDataException("Invalid residue type."),
            };
        }
    }
}
