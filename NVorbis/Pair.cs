using System;

namespace NVorbis;

internal readonly record struct Pair<T>(T A, T B)
{
    public T this[int i]
    {
        get => i switch
        {
            0 => A,
            1 => B,
            _ => throw new IndexOutOfRangeException(),
        };
    }

    public T this[bool flag]
    {
        get => flag ? B : A;
    }
}
