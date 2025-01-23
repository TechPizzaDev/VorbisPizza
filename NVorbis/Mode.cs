using System;
using NVorbis.Contracts;

namespace NVorbis
{
    internal struct Mode
    {
        private bool _blockFlag;
        private BlockSizes _blockSizes;
        private Mapping _mapping;

        public Mode(ref VorbisPacket packet, BlockSizes blockSizes, Mapping[] mappings)
        {
            _blockFlag = packet.ReadBit();
            if (0 != packet.ReadBits(32))
            {
                throw new System.IO.InvalidDataException("Mode header had invalid window or transform type!");
            }

            int mappingIdx = (int)packet.ReadBits(8);
            if (mappingIdx >= mappings.Length)
            {
                throw new System.IO.InvalidDataException("Mode header had invalid mapping index!");
            }
            _mapping = mappings[mappingIdx];

            _blockSizes = blockSizes;
        }

        public bool GetPacketInfo(ref VorbisPacket packet, out PacketInfo info)
        {
            if (packet.IsShort)
            {
                info = default;
                return false;
            }

            int size = _blockSizes[_blockFlag];
            (bool prev, bool next)? flags = _blockFlag ? (packet.ReadBit(), packet.ReadBit()) : null;

            // Compute windowing info for left window
            var center = size / 2;
            var size0 = _blockSizes.Size0;

            var (leftStart, leftEnd, length, useSize1) = (flags?.prev ?? true) ?
                (0, center, size / 2, _blockFlag) :
                ((size - size0) / 4, (size + size0) / 4, size0 / 2, false);

            // Compute windowing info for right window
            var (rightStart, rightEnd) = (flags?.next ?? true) ?
                 (center, size) :
                 ((size * 3 - size0) / 4, (size * 3 + size0) / 4);

            info = new PacketInfo()
            {
                Length = length,
                LeftUseSize1 = useSize1,

                LeftStart = leftStart,
                LeftEnd = leftEnd,

                RightStart = rightStart,
                RightEnd = rightEnd,
            };
            return true;
        }

        public bool Decode(
            ref VorbisPacket packet,
            ChannelBuffer buffer,
            Codebook[] books,
            IFloor[] floors,
            Residue0[] residues,
            out PacketInfo info)
        {
            if (!GetPacketInfo(ref packet, out info))
            {
                return false;
            }
        
            int blockSize = _blockSizes[_blockFlag];
            _mapping.DecodePacket(ref packet, blockSize, buffer, floors, residues, books);

            return true;
        }
    }
}
