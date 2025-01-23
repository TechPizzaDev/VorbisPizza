using System;

namespace NVorbis
{
    // all channels in one pass, interleaved
    internal sealed class Residue2 : Residue1
    {
        public Residue2(ref VorbisPacket packet, Codebook[] codebooks) : base(ref packet, codebooks)
        {
        }

        public override void Decode(
            ref VorbisPacket packet, 
            BitStackArray doNotDecodeChannel, int blockSize, ChannelBuffer buffer, Codebook[] books)
        {
            int channels = doNotDecodeChannel.Count;
            int halfSize = blockSize / 2;

            if (!doNotDecodeChannel.Contains(false))
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer.GetSpan(ch).Slice(0, halfSize).Clear();
                }
                return;
            }

            // since we're doing all channels in a single pass, the block size has to be multiplied.
            // otherwise this is just a pass-through call

            var tmp = new ChannelBuffer(new float[halfSize * channels], 1, halfSize * channels);
            var oneFalse = new BitStackArray([0]);
            oneFalse.Add(false);
            base.Decode(ref packet, oneFalse, blockSize * channels, tmp, books);

            if (channels == 1)
            {
                tmp.GetSpan(0).CopyTo(buffer.GetSpan(0));
            }
            else
            {
                Span<float> src = tmp.GetFullSpan();
                for (int ch = 0; ch < channels; ch++)
                {
                    Span<float> dst = buffer.GetSpan(ch);
                    for (int i = 0; i < halfSize; i++)
                    {
                        dst[i] = src[i * channels + ch];
                    }
                }
            }
        }
    }
}
