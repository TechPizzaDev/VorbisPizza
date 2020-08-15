/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;

namespace NVorbis.Ogg
{
    internal class OggPacket : VorbisDataPacket
    {
        private long _offset;                         // 8
        private int _length;                          // 4
        private int _curOfs;                          // 4
        private OggPacket? _mergedPacket;             // IntPtr.Size
        private OggContainerReader _containerReader;  // IntPtr.Size
        private ReadOnlyMemory<byte> _data;           // sizeof(ReadOnlyMemory<byte>)

        internal OggPacket? Next { get; set; } // IntPtr.Size
        internal OggPacket? Prev { get; set; } // IntPtr.Size

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

        internal OggPacket(OggContainerReader containerReader, long streamOffset, int length)
            : base(length)
        {
            _containerReader = containerReader ?? throw new ArgumentNullException(nameof(containerReader));

            _offset = streamOffset;
            _length = length;
            _curOfs = 0;
        }

        internal void MergeWith(VorbisDataPacket continuation)
        {
            if (!(continuation is OggPacket op))
                throw new ArgumentException("Incorrect packet type!", nameof(continuation));

            Length += continuation.Length;

            if (_mergedPacket == null)
                _mergedPacket = op;
            else
                _mergedPacket.MergeWith(continuation);

            // per the spec, a partial packet goes with the next page's granulepos. 
            // we'll go ahead and assign it to the next page as well
            PageGranulePosition = continuation.PageGranulePosition;
            PageSequenceNumber = continuation.PageSequenceNumber;
        }

        internal void Reset()
        {
            _curOfs = 0;
            ResetBitReader();

            if (_mergedPacket != null)
                _mergedPacket.Reset();
        }

        protected override int ReadNextByte()
        {
            if (_curOfs == _length)
            {
                if (_mergedPacket == null)
                    return -1;
                return _mergedPacket.ReadNextByte();
            }

            if (_data.IsEmpty)
                _data = _containerReader.ReadPacketData(_offset, _length);

            if (_curOfs < _data.Length)
                return _data.Span[_curOfs++];

            return -1;
        }
    }
}
