using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class Packet : DataPacket
    {
        // this is the list of pages & packets in packed 24:8 format
        // in theory, this is good for up to 1016 GiB of Ogg file
        // in practice, probably closer to 300 days @ 160k bps
        private PacketDataPart[]? _dataParts;
        private PacketDataPart _firstDataPart;

        public IPacketReader _packetReader;
        private int _dataCount;
        private byte[] _data;
        private int _dataPartIndex;
        private int _dataOfs;
        private int _dataEnd;

        internal Packet(PacketDataPart firstDataPart, PacketDataPart[]? dataParts, IPacketReader packetReader)
        {
            _firstDataPart = firstDataPart;
            _dataParts = dataParts;
            _packetReader = packetReader;
            _data = Array.Empty<byte>();
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
            _data = data.Array ?? Array.Empty<byte>();
            _dataOfs = data.Offset;
            _dataEnd = data.Offset + data.Count;
        }

        protected override int ReadBytes(Span<byte> destination)
        {
            int length = destination.Length;
            do
            {
                int left = _dataEnd - _dataOfs;
                int toRead = Math.Min(left, destination.Length);
                _data.AsSpan(_dataOfs, toRead).CopyTo(destination);
                destination = destination.Slice(toRead);

                _dataOfs += toRead;
                if (_dataOfs == _dataEnd)
                {
                    if (!GetNextPacketData())
                        break;
                }
            }
            while (destination.Length > 0);

            return length - destination.Length;
        }

        private bool GetNextPacketData()
        {
            if (++_dataPartIndex < GetDataPartCount())
            {
                ArraySegment<byte> data = _packetReader.GetPacketData(GetDataPart(_dataPartIndex));
                SetData(data);
                _dataCount += data.Count * 8;
                return true;
            }

            byte[] oldData = _data;
            SetData(Array.Empty<byte>());

            // Restore to previous index to not overflow ever
            _dataPartIndex--;

            // If data was already the special one-byte array,
            // there was an attempt to read past the end of the packet so invalidate the read.
            return oldData != Array.Empty<byte>();
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
            //_packetReader?.InvalidatePacketCache(this);

            base.Done();
        }
    }
}