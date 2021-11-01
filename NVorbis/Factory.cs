﻿using NVorbis.Contracts;

namespace NVorbis
{
    class Factory : IFactory
    {
        internal static Factory Instance { get; } = new();

        public Huffman CreateHuffman()
        {
            return new Huffman();
        }

        public IMdct CreateMdct()
        {
            return new Mdct();
        }

        public Codebook CreateCodebook()
        {
            return new Codebook();
        }

        public IFloor CreateFloor(DataPacket packet)
        {
            var type = (int)packet.ReadBits(16);
            switch (type)
            {
                case 0: return new Floor0();
                case 1: return new Floor1();
                default: throw new System.IO.InvalidDataException("Invalid floor type!");
            }
        }

        public IMapping CreateMapping(DataPacket packet)
        {
            if (packet.ReadBits(16) != 0)
            {
                throw new System.IO.InvalidDataException("Invalid mapping type!");
            }

            return new Mapping();
        }

        public IMode CreateMode()
        {
            return new Mode();
        }

        public IResidue CreateResidue(DataPacket packet)
        {
            var type = (int)packet.ReadBits(16);
            switch (type)
            {
                case 0: return new Residue0();
                case 1: return new Residue1();
                case 2: return new Residue2();
                default: throw new System.IO.InvalidDataException("Invalid residue type!");
            }
        }
    }
}
