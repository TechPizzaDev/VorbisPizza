using System;
using System.Diagnostics;

namespace NVorbis.Ogg
{
    /// <summary>
    /// Represents a slice of page data.
    /// </summary>
    public readonly struct PageSlice
    {
        internal PageData Page { get; }

        /// <summary>
        /// Gets the data offset within the page.
        /// </summary>
        public int Start { get; }

        /// <summary>
        /// Gets the length of the page slice.
        /// </summary>
        public int Length { get; }

        internal PageSlice(PageData page, int start, int length)
        {
            Debug.Assert((uint)start <= (uint)page.Length);
            Debug.Assert((uint)length <= (uint)(page.Length - start));

            Page = page;
            Start = start;
            Length = length;
        }

        /// <summary>
        /// Gets a span view over the page data slice.
        /// </summary>
        public Span<byte> AsSpan()
        {
            return Page.AsSpan().Slice(Start, Length);
        }

        /// <summary>
        /// Gets an array segment view over the page data slice.
        /// </summary>
        public ArraySegment<byte> AsSegment()
        {
            return Page.AsSegment().Slice(Start, Length);
        }
    }
}
