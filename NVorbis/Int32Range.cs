/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Collections;
using System.Collections.Generic;

namespace NVorbis
{
    readonly struct Int32Range : IReadOnlyList<int>
    {
        public int Start { get; }
        public int Count { get; }

        public Int32Range(int start, int count)
        {
            Start = start;
            Count = count;
        }

        public int this[int index]
        {
            get
            {
                if (index > Count)
                    throw new ArgumentOutOfRangeException();
                return Start + index;
            }
        }

        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
