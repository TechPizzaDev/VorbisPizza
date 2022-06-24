using NVorbis.Contracts.Ogg;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis.Ogg
{
    internal sealed class Packet : DataPacket
    {
        private static byte[] _oneByte = new byte[1];

        // this is the list of pages & packets in packed 24:8 format
        // in theory, this is good for up to 1016 GiB of Ogg file
        // in practice, probably closer to 300 days @ 160k bps
        private PacketDataPart[] _dataParts;
        private PacketDataPart _firstDataPart;

        private IPacketReader _packetReader;
        private int _dataCount;
        private byte[] _data;
        private int _dataPartIndex;
        private int _dataOfs;
        private int _dataEnd;

        internal Packet(PacketDataPart firstDataPart, PacketDataPart[] dataParts, IPacketReader packetReader)
        {
            _firstDataPart = firstDataPart;
            _dataParts = dataParts;
            _packetReader = packetReader;
            Reset();
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
                return _dataParts[index - 1];
            return _firstDataPart;
        }

        protected override int TotalBits => _dataCount;

        private void SetData(ArraySegment<byte> data)
        {
            _data = data.Array;
            _dataOfs = data.Offset;
            _dataEnd = data.Offset + data.Count;
        }

        protected override int ReadNextByte()
        {
            int b = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_data), _dataOfs);

            if (++_dataOfs == _dataEnd)
            {
                GetNextPacketData(ref b);
            }

            return b;
        }

        private void GetNextPacketData(ref int b)
        {
            if (++_dataPartIndex < GetDataPartCount())
            {
                ArraySegment<byte> data = _packetReader.GetPacketData(GetDataPart(_dataPartIndex));
                SetData(data);
                _dataCount += data.Count * 8;
            }
            else
            {
                // Data is already the special array;
                // there was an attempt to read past the end of the packet so invalidate the read.
                if (_data == _oneByte)
                    b = -1;

                SetData(_oneByte);

                // Restore to previous index to not overflow ever
                _dataPartIndex--;
            }
        }

        public override void Reset()
        {
            _dataPartIndex = 0;
            ArraySegment<byte> data = _packetReader.GetPacketData(_firstDataPart);
            SetData(data);
            _dataCount = data.Count * 8;

            base.Reset();
        }

        public override void Done()
        {
            _packetReader?.InvalidatePacketCache(this);

            base.Done();
        }
    }
}