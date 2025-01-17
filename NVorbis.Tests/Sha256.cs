using System.Runtime.CompilerServices;

namespace NVorbis.Tests;

[InlineArray(4)]
public struct Sha256 : IEquatable<Sha256>
{
    private ulong _e0;

    public Sha256(ulong e0, ulong e1, ulong e2, ulong e3)
    {
        this[0] = e0;
        this[1] = e1;
        this[2] = e2;
        this[3] = e3;
    }
    
    public bool Equals(Sha256 other)
    {
        return ((ReadOnlySpan<ulong>)this).SequenceEqual(other);
    }

    public override bool Equals(object? obj)
    {
        return obj is Sha256 other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        for (int i = 0; i < 4; i++)
        {
            ulong value = this[i];
            hash.Add((int)value);
            hash.Add((int)(value >> 32));
        }
        return hash.ToHashCode();
    }
}