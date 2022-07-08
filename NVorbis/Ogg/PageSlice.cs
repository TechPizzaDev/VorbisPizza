using System;
using System.Diagnostics;

namespace NVorbis.Ogg
{
    internal readonly struct PageSlice
    {
        public PageData Page { get; }
        public int Start { get; }
        public int Length { get; }

        public PageSlice(PageData page, int start, int length)
        {
            Debug.Assert((uint)start <= (uint)page.Length);
            Debug.Assert((uint)length <= (uint)(page.Length - start));
            
            Page = page;
            Start = start;
            Length = length;
        }

        public Span<byte> AsSpan()
        {
            return Page.AsSpan().Slice(Start, Length);
        }

        public ArraySegment<byte> AsSegment()
        {
            return Page.AsSegment().Slice(Start, Length);
        }
    }
}
