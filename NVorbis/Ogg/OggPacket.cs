﻿/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace NVorbis.Ogg
{
    class Packet : DataPacket
    {
        long _offset;                       // 8
        int _length;                        // 4
        int _curOfs;                        // 4
        Packet _mergedPacket;               // IntPtr.Size
        Packet _next;                       // IntPtr.Size
        Packet _prev;                       // IntPtr.Size
        ContainerReader _containerReader;   // IntPtr.Size
        byte[] _data;                       // IntPtr.Size + data

        internal Packet Next
        {
            get => _next;
            set => _next = value;
        }
        internal Packet Prev
        {
            get => _prev;
            set => _prev = value;
        }
        internal bool IsContinued
        {
            get => GetFlag(PacketFlags.User1);
            set => SetFlag(PacketFlags.User1, value);
        }
        internal bool IsContinuation
        {
            get => GetFlag(PacketFlags.User2);
            set => SetFlag(PacketFlags.User2, value);
        }

        internal Packet(ContainerReader containerReader, long streamOffset, int length)
            : base(length)
        {
            _containerReader = containerReader;

            _offset = streamOffset;
            _length = length;
            _curOfs = 0;
        }

        internal void MergeWith(DataPacket continuation)
        {
            var op = continuation as Packet;

            if (op == null) throw new ArgumentException("Incorrect packet type!");

            Length += continuation.Length;

            if (_mergedPacket == null)
            {
                _mergedPacket = op;
            }
            else
            {
                _mergedPacket.MergeWith(continuation);
            }

            // per the spec, a partial packet goes with the next page's granulepos.  we'll go ahead and assign it to the next page as well
            PageGranulePosition = continuation.PageGranulePosition;
            PageSequenceNumber = continuation.PageSequenceNumber;
        }

        internal void Reset()
        {
            _curOfs = 0;
            ResetBitReader();

            if (_mergedPacket != null)
            {
                _mergedPacket.Reset();
            }
        }

        protected override int ReadNextByte()
        {
            if (_curOfs == _length)
            {
                if (_mergedPacket == null) return -1;

                return _mergedPacket.ReadNextByte();
            }

            if (_data == null)
            {
                _data = _containerReader.ReadPacketData(_offset, _length);
            }

            if (_curOfs < _data.Length)
            {
                return _data[_curOfs++];
            }
            return -1;
        }
    }
}
