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
    [Flags]
    enum OggPageFlags
    {
        None = 0,
        ContinuesPacket = 1,
        BeginningOfStream = 2,
        EndOfStream = 4,
    }
}
