using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis;

internal ref struct BitStackArray
{
    private const int Bits = sizeof(byte) * 8;

    private readonly ref byte _data;
    private readonly int _capacity;
    private int _count;

    public readonly int Count => _count;

    public readonly bool this[int index]
    {
        get
        {
            byte slot = GetSlot(index, out int bitIndex);
            int mask = 1 << bitIndex;
            return (slot & mask) != 0;
        }
        set
        {
            ref byte slot = ref GetSlot(index, out int bitIndex);
            int mask = 1 << bitIndex;
            slot = (byte)((value ? mask : 0) | (slot & ~mask));
        }
    }

    public BitStackArray(Span<byte> storage)
    {
        _data = ref MemoryMarshal.GetReference(storage);
        _capacity = storage.Length * Bits;
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly ref byte GetSlot(int index, out int bitIndex)
    {
        if ((uint)index >= (uint)_count)
        {
            ThrowIndexOutOfRange();
        }

        (uint slotIndex, uint uBitIndex) = Math.DivRem((uint)index, Bits);
        bitIndex = (int)uBitIndex;
        return ref Unsafe.Add(ref _data, slotIndex);
    }

    public void Add(bool value)
    {
        int count = _count;
        int newCount = count + 1;
        if (newCount > _capacity)
        {
            ThrowIndexOutOfRange();
        }
        _count = newCount;

        ref byte slot = ref GetSlot(count, out int bitIndex);
        int mask = 1 << bitIndex;
        slot = (byte)((value ? mask : 0) | (slot & ~mask));
    }

    public readonly bool Contains(bool value)
    {
        for (int i = 0; i < _count; i++)
        {
            if (this[i] == value)
            {
                return true;
            }
        }
        return false;
    }

    public void Clear()
    {
        _count = 0;
    }

    [DoesNotReturn]
    private static void ThrowIndexOutOfRange()
    {
        throw new IndexOutOfRangeException();
    }
}
