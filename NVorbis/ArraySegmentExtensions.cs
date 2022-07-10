using System;

namespace NVorbis
{
    internal static class ArraySegmentExtensions
    {
        public static ref T Get<T>(this ArraySegment<T> segment, int index)
        {
            uint i = (uint)(segment.Offset + index);
            if (i >= (uint)segment.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return ref segment.Array![i];
        }
    }
}
