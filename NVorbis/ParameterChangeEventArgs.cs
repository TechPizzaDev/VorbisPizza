/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/

namespace NVorbis
{
    /// <summary>
    /// Event data for when a logical stream has a parameter change.
    /// </summary>
    public readonly struct ParameterChangeEventArgs
    {
        /// <summary>
        /// Gets the first packet after the parameter change.  
        /// This would typically be the parameters packet.
        /// </summary>
        public VorbisDataPacket FirstPacket { get; }

        /// <summary>
        /// Creates a new instance of <see cref="ParameterChangeEventArgs"/>.
        /// </summary>
        /// <param name="firstPacket">The first packet after the parameter change.</param>
        public ParameterChangeEventArgs(VorbisDataPacket firstPacket)
        {
            FirstPacket = firstPacket;
        }
    }
}
