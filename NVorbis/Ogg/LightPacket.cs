using System.Collections.Generic;
using System.Linq;

namespace NVorbis.Ogg
{
    internal class LightPacket : DataPacket
    {
        LightPageReader _reader;
        LightPacketProvider _packetProvider;
        IList<(long, int)> _dataSrc;
        int _dataIndex;
        int _dataOfs;
        byte[] _dataBuf;

        public int Index { get; }

        public LightPacket(
            LightPageReader reader, 
            LightPacketProvider packetProvider, 
            int index, 
            IList<(long, int)> data)
            : base(GetSizeSum(data))
        {
            _reader = reader;
            _packetProvider = packetProvider;
            Index = index;
            _dataSrc = data;
        }

        private static int GetSizeSum(IList<(long, int)> data)
        {
            int sum = 0;
            for (int i = 0; i < data.Count; i++)
                sum += data[i].Item2;
            return sum;
        }

        public override void Done()
        {
            if (GranuleCount.HasValue)
                _packetProvider.SetPacketGranuleInfo(Index, GranuleCount.Value, GranulePosition);
            _dataBuf = null;
        }

        protected override int ReadNextByte()
        {
            if (_dataIndex == _dataSrc.Count)
                return -1;
            
            if (_dataOfs == 0)
            {
                var ofs = _dataSrc[_dataIndex].Item1;
                _dataBuf = new byte[_dataSrc[_dataIndex].Item2];

                var idx = 0;
                int cnt;
                while (
                    idx < _dataBuf.Length &&
                    (cnt = _reader.Read(ofs + idx, _dataBuf, idx, _dataBuf.Length - idx)) > 0)
                {
                    idx += cnt;
                }

                if (idx < _dataBuf.Length)
                {
                    // uh-oh...  bad packet
                    _dataBuf = null;
                    _dataIndex = _dataSrc.Count;
                    return -1;
                }
            }

            var b = _dataBuf[_dataOfs];
            
            if (++_dataOfs == _dataSrc[_dataIndex].Item2)
            {
                _dataOfs = 0;
                if (++_dataIndex == _dataSrc.Count)
                    _dataBuf = null;
            }
            
            return b;
        }
    }
}