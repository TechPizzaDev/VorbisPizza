using NVorbis.Contracts.Ogg;
using System;
using System.Collections.Generic;

namespace NVorbis.Ogg
{
    internal sealed class Packet : DataPacket
    {
        // size with 1-2 packet segments (> 2 packet segments should be very uncommon):
        //   x86:  68 bytes
        //   x64: 104 bytes

        // this is the list of pages & packets in packed 24:8 format
        // in theory, this is good for up to 1016 GiB of Ogg file
        // in practice, probably closer to 300 days @ 160k bps
        private PacketDataPart[] _dataParts;
        private PacketDataPart _firstDataPart;

        private IPacketReader _packetReader;
        private int _dataCount;
        private Memory<byte> _data;
        private int _dataIndex;
        private int _dataOfs;

        internal Packet(PacketDataPart firstDataPart, PacketDataPart[] dataParts, IPacketReader packetReader, Memory<byte> initialData)
        {
            _firstDataPart = firstDataPart;
            _dataParts = dataParts;
            _packetReader = packetReader;
            _data = initialData;
        }

        private int GetDataPartCount()
        {
            int length = 1;
            if (_dataParts != null)
                length += _dataParts.Length;
            return length;
        }

        private PacketDataPart GetDataPart(int index)
        {
            if (index != 0 && _dataParts != null)
                return _dataParts[index];
            return _firstDataPart;
        }

        protected override int TotalBits => (_dataCount + _data.Length) * 8;

        protected override int ReadNextByte()
        {
            int dataPartCount = GetDataPartCount();
            if (_dataIndex == dataPartCount) return -1;

            var b = _data.Span[_dataOfs];

            if (++_dataOfs == _data.Length)
            {
                _dataOfs = 0;
                _dataCount += _data.Length;
                if (++_dataIndex < dataPartCount)
                {
                    _data = _packetReader.GetPacketData(GetDataPart(_dataIndex));
                }
                else
                {
                    _data = Memory<byte>.Empty;
                }
            }

            return b;
        }

        public override void Reset()
        {
            _dataIndex = 0;
            _dataOfs = 0;
            if (GetDataPartCount() > 0)
            {
                _data = _packetReader.GetPacketData(GetDataPart(0));
            }

            base.Reset();
        }

        public override void Done()
        {
            _packetReader?.InvalidatePacketCache(this);

            base.Done();
        }
    }
}