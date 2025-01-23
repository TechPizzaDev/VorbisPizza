namespace NVorbis;

internal struct PacketInfo
{
    public int Length;
    public bool LeftUseSize1;

    public int LeftStart;
    public int LeftEnd;

    public int RightStart;
    public int RightEnd;

    public readonly int SampleCount => RightStart - LeftStart;
}
