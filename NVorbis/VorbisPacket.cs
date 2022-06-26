using System;
using NVorbis.Contracts.Ogg;
using NVorbis.Ogg;

namespace NVorbis
{
    public struct VorbisPacket : IEquatable<VorbisPacket>
    {
        private DataPacket _packet;

        internal VorbisPacket(ForwardOnlyPacketProvider packetProvider)
        {
            _packet = packetProvider;
        }

        internal VorbisPacket(PacketDataPart firstDataPart, PacketDataPart[]? dataParts, IPacketReader packetReader)
        {
            _packet = new Packet(firstDataPart, dataParts, packetReader);
        }

        public bool IsValid => _packet != null;

        public int ContainerOverheadBits
        {
            get => _packet.ContainerOverheadBits;
            set => _packet.ContainerOverheadBits = value;
        }

        public long? GranulePosition
        {
            get => _packet.GranulePosition;
            set => _packet.GranulePosition = value;
        }

        public bool IsResync
        {
            get => _packet.IsResync;
            set => _packet.IsResync = value;
        }

        public bool IsShort
        {
            get => _packet.IsShort;
        }

        public bool IsEndOfStream
        {
            get => _packet.IsEndOfStream;
            set => _packet.IsEndOfStream = value;
        }

        public int BitsRead => _packet.BitsRead;

        public int BitsRemaining => _packet.BitsRemaining;

        public void Done()
        {
            if (_packet is Packet packet)
            {
                packet._packetReader?.InvalidatePacketCache(this);
            }
            else if (_packet is ForwardOnlyPacketProvider fpacket)
            {
                fpacket.Done();
            }
            _packet = null;
        }

        public void Reset()
        {
            _packet.Reset();
        }

        public ulong ReadBits(int count)
        {
            return _packet.ReadBits(count);
        }

        public ulong TryPeekBits(int count, out int bitsRead)
        {
            return _packet.TryPeekBits(count, out bitsRead);
        }

        public int SkipBits(int count)
        {
            return _packet.SkipBits(count);
        }

        public readonly bool Equals(VorbisPacket other)
        {
            return ReferenceEquals(_packet, other._packet);
        }
    }
}
