/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/

namespace NVorbis
{
    internal abstract partial class VorbisResidue
    {
        // residue type 2... basically type 0,
        // but samples are interleaved between channels (ch0, ch1, ch0, ch1, etc...)
        private sealed class Residue2 : Residue0
        {
            private int _channels;

            internal Residue2(VorbisStreamDecoder vorbis) : base(vorbis) { }

            // We can use the type 0 logic by saying we're doing a 
            // single channel buffer big enough to hold the samples for all channels
            // This works because WriteVectors(...) "knows" the
            // correct channel count and processes the data accordingly.
            internal override float[][] Decode(
                VorbisDataPacket packet, bool[] doNotDecode, int channels, int blockSize)
            {
                _channels = channels;

                return base.Decode(packet, doNotDecode, 1, blockSize * channels);
            }

            protected override bool WriteVectors(
                VorbisCodebook codebook, VorbisDataPacket packet, float[][] residue,
                int channel, int offset, int partitionSize)
            {
                int chPtr = 0;

                offset /= _channels;
                for (int c = 0; c < partitionSize;)
                {
                    int entry = codebook.DecodeScalar(packet);
                    if (entry == -1)
                        return true;

                    for (int d = 0; d < codebook.Dimensions; d++, c++)
                    {
                        residue[chPtr][offset] += codebook[entry, d];
                        if (++chPtr == _channels)
                        {
                            chPtr = 0;
                            offset++;
                        }
                    }
                }

                return false;
            }
        }
    }
}
