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
        // residue type 1... samples are grouped by channel, 
        // then stored with interleaved dimensions (d0, d1, d2, d0, d1, d2, etc...)
        private sealed class Residue1 : Residue0
        {
            internal Residue1(VorbisStreamDecoder vorbis) : base(vorbis) { }

            protected override bool WriteVectors(
                VorbisCodebook codebook, VorbisDataPacket packet, float[][] residue,
                int channel, int offset, int partitionSize)
            {
                var res = residue[channel];

                for (int i = 0; i < partitionSize;)
                {
                    var entry = codebook.DecodeScalar(packet);
                    if (entry == -1)
                        return true;

                    for (int j = 0; j < codebook.Dimensions; i++, j++)
                        res[offset + i] += codebook[entry, j];
                }

                return false;
            }
        }
    }
}
