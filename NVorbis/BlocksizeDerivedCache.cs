using System;

namespace NVorbis;

internal readonly struct BlocksizeDerivedCache
{
    public readonly float[] WindowSlope;

    public BlocksizeDerivedCache(int blockSize)
    {
        WindowSlope = GenerateWindow(blockSize / 2);
    }

    private static float[] GenerateWindow(int n)
    {
        var window = new float[n];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = CalcWindowSlope(i, n);
        }
        return window;
    }

    private static float CalcWindowSlope(int x, int n)
    {
        // please note that there might be a MISTAKE
        // in how the spec specifies the right window slope
        // function. See "4.3.1. packet type, mode and window decode"
        // step 7 where it adds an "extra" pi/2.
        // The left slope doesn't have it, only the right one.
        // as stb_vorbis shares the window slope generation function,
        // The *other* possible reason is that we don't need the right
        // window for anything. TODO investigate this more.
        float v = MathF.Sin(0.5f * MathF.PI * (x + 0.5f) / n);
        return MathF.Sin(0.5f * MathF.PI * v * v);
    }
}