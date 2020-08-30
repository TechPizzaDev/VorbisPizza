using System;
using System.Collections.Generic;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal class Packet : DataPacket
    {
        public readonly struct DataParts
        {
            public int Value { get; }
            public IReadOnlyList<int>? List { get; }

            public int Count => List?.Count ?? 1;

            public int this[int index]
            {
                get
                {
                    if (List == null)
                    {
                        if (index != 0)
                            throw new IndexOutOfRangeException();
                        return Value;
                    }
                    return List[index];
                }
            }

            public DataParts(int value) : this()
            {
                Value = value;
            }

            public DataParts(IReadOnlyList<int>? list) : this()
            {
                List = list;
            }
        }

        // size with 1-2 packet segments (> 2 packet segments should be very uncommon):
        //   x86:  68 bytes
        //   x64: 104 bytes

        // this is the list of pages & packets in packed 24:8 format
        // in theory, this is good for up to 1016 GiB of Ogg file
        // in practice, probably closer to 300 days @ 160kbps
        private DataParts _dataParts;
        private IPacketReader _packetReader;
        private int _dataCount;
        private Memory<byte> _data;
        private int _dataIndex;
        private int _dataOfs;

        internal Packet(DataParts dataParts, IPacketReader packetReader, Memory<byte> initialData)
        {
            _dataParts = dataParts;
            _packetReader = packetReader;
            _data = initialData;
        }

        protected override int TotalBits => (_dataCount + _data.Length) * 8;

        protected override int ReadNextByte()
        {
            if (_dataIndex == _dataParts.Count)
                return -1;

            byte b = _data.Span[_dataOfs];

            if (++_dataOfs == _data.Length)
            {
                _dataOfs = 0;
                _dataCount += _data.Length;
                if (++_dataIndex < _dataParts.Count)
                {
                    _data = _packetReader.GetPacketData(_dataParts[_dataIndex]);
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

            if (_dataParts.Count > 0)
                _data = _packetReader.GetPacketData(_dataParts[0]);

            base.Reset();
        }

        public override void Done()
        {
            _packetReader?.InvalidatePacketCache(this);

            base.Done();
        }
    }
}