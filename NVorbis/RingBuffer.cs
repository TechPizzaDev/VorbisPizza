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
    class RingBuffer
    {
        float[] _buffer;
        int _start;
        int _end;
        int _bufLen;

        internal RingBuffer(int size)
        {
            _buffer = new float[size];
            _start = _end = 0;
            _bufLen = size;
        }

        internal void EnsureSize(int size)
        {
            // because _end == _start signifies no data, and _end is always 1 more than the data we have, 
            // we must make the buffer {channels} entries bigger than requested
            size += Channels;

            if (_bufLen < size)
            {
                var newBuffer = new float[size];
                Array.Copy(_buffer, _start, newBuffer, 0, _bufLen - _start);
                if (_end < _start)
                    Array.Copy(_buffer, 0, newBuffer, _bufLen - _start, _end);

                var end = Length;
                _start = 0;
                _end = end;
                _buffer = newBuffer;

                _bufLen = size;
            }
        }

        internal int Channels;

        internal void CopyTo(Span<float> buffer)
        {
            var start = _start;
            RemoveItems(buffer.Length);

            // this is used to pull data out of the buffer, so we'll update the start position too
            int length = (_end - start + _bufLen) % _bufLen;
            if (buffer.Length > length)
                throw new ArgumentOutOfRangeException(nameof(buffer), "Destination buffer requested too much.");

            var cnt = Math.Min(buffer.Length, _bufLen - start);
            _buffer.AsSpan(start, cnt).CopyTo(buffer);

            if (cnt < buffer.Length)
                _buffer.AsSpan(0, buffer.Length - cnt).CopyTo(buffer.Slice(cnt));
        }

        internal void RemoveItems(int count)
        {
            var cnt = (count + _start) % _bufLen;
            if (_end > _start)
            {
                if (cnt > _end || cnt < _start)
                    throw new ArgumentOutOfRangeException(nameof(count));
            }
            else
            {
                // wrap-around
                if (cnt < _start && cnt > _end)
                    throw new ArgumentOutOfRangeException(nameof(count));
            }

            _start = cnt;
        }

        internal void Clear()
        {
            _start = _end = 0;
        }

        internal int Length
        {
            get
            {
                var tmp = _end - _start;
                if (tmp < 0)
                    tmp += _bufLen;
                return tmp;
            }
        }

        internal void Write(
            int channel, int index, int start, int switchPoint, int end, float[] pcm, float[] window)
        {
            // this is the index of the first sample to merge
            int idx = (index + start) * Channels + channel + _start;
            while (idx >= _bufLen)
                idx -= _bufLen;

            // blech...  gotta fix the first packet's pointers
            if (idx < 0)
            {
                start -= index;
                idx = channel;
            }

            // go through and do the overlap
            for (; idx < _bufLen && start < switchPoint; idx += Channels, ++start)
                _buffer[idx] += pcm[start] * window[start];

            if (idx >= _bufLen)
            {
                idx -= _bufLen;
                for (; start < switchPoint; idx += Channels, ++start)
                    _buffer[idx] += pcm[start] * window[start];
            }

            // go through and write the rest
            for (; idx < _bufLen && start < end; idx += Channels, ++start)
                _buffer[idx] = pcm[start] * window[start];

            if (idx >= _bufLen)
            {
                idx -= _bufLen;
                for (; start < end; idx += Channels, ++start)
                    _buffer[idx] = pcm[start] * window[start];
            }

            // finally, make sure the buffer end is set correctly
            _end = idx;
        }
    }
}
