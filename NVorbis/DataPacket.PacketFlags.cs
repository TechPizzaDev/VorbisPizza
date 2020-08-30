/****************************************************************************
* NVorbis                                                                  *
* Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
*                                                                          *
* See COPYING for license terms (Ms-PL).                                   *
*                                                                          *
***************************************************************************/
using System;

namespace NVorbis
{
    public abstract partial class DataPacket
    {
        /// <summary>
        /// Defines flags to apply to the current packet
        /// </summary>
        [Flags]
        // for now, let's use a byte... if we find we need more space, we can always expand it...
        protected enum PacketFlags : byte
        {
            /// <summary>
            /// Packet is first since reader had to resync with stream.
            /// </summary>
            IsResync = 0x01,
            /// <summary>
            /// Packet is the last in the logical stream.
            /// </summary>
            IsEndOfStream = 0x02,
            /// <summary>
            /// Packet does not have all its data available.
            /// </summary>
            IsShort = 0x04,

            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User0 = 0x08,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User1 = 0x10,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User2 = 0x20,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User3 = 0x40,
            /// <summary>
            /// Flag for use by inheritors.
            /// </summary>
            User4 = 0x80,
        }

    }
}