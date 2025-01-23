namespace NVorbis;

internal readonly struct BlockSizes
{
    private readonly byte _value;

    public BlockSizes(int size0, int size1)
    {
        _value = (byte)((size0 & 0xf) | ((size1 & 0xf) << 4));
    }

    public int Size0 => 1 << (_value & 0xf);
    public int Size1 => 1 << ((_value >> 4) & 0xf);

    public int this[bool flag] => flag ? Size1 : Size0;

    public int IndexOf(int value)
    {
        if (value == Size0)
        {
            return 0;
        }
        if (value == Size1)
        {
            return 1;
        }
        return -1;
    }
}
