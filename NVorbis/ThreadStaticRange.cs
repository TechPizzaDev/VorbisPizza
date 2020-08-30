using System;
using System.Collections;
using System.Collections.Generic;

namespace NVorbis
{
    internal class ThreadStaticRange : IReadOnlyList<int>
    {
        [ThreadStatic]
        private static ThreadStaticRange? _cachedRange;

        private int _start;

        public int Count { get; private set; }

        public int this[int index]
        {
            get
            {
                if (index > Count)
                    throw new IndexOutOfRangeException();
                return _start + index;
            }
        }

        private ThreadStaticRange()
        {
        }

        internal static ThreadStaticRange Get(int start, int count)
        {
            var fr = _cachedRange ??= new ThreadStaticRange();
            fr._start = start;
            fr.Count = count;
            return fr;
        }

        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
