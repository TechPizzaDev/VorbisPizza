using System;
using System.Diagnostics;

namespace NVorbis;

internal struct ChannelBuffer
{
    private float[] _array;
    private int _channels;
    private int _stride;

    public ChannelBuffer(float[] array, int channels, int stride)
    {
        _array = array;
        _channels = channels;
        _stride = stride;
    }

    public Span<float> GetSpan(int channel)
    {
        Debug.Assert(channel < _channels);
        return _array.AsSpan(channel * _stride, _stride);
    }

    public Span<float> GetFullSpan()
    {
        return _array;
    }
}
