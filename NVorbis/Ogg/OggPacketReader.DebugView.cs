/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Collections.Generic;

namespace NVorbis.Ogg
{
    partial class OggPacketReader
    {
        class DebugView
        {
            OggPacketReader _reader;
            OggPacket _last, _first;
            
            public OggContainerReader Container => _reader._container;
            public int StreamSerial => _reader._streamSerial;
            public bool EndOfStreamFound => _reader._eosFound;

            public int CurrentPacketIndex
            {
                get
                {
                    if (_reader._current == null)
                        return -1;
                    return Packets.IndexOf(_reader._current);
                }
            }

            public DebugView(OggPacketReader reader)
            {
                _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            }

            public List<OggPacket> Packets
            {
                get
                {
                    var packets = new List<OggPacket>();
                    if (_reader._last == _last && 
                        _reader._first == _first)
                        return packets;
                    
                    _last = _reader._last;
                    _first = _reader._first;

                    var node = _first;
                    while (node != null)
                    {
                        packets.Add(node);
                        node = node.Next;
                    }
                    return packets;
                }
            }
        }
    }
}
