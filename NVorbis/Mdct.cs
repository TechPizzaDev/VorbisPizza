using System;
using System.Collections.Concurrent;

namespace NVorbis
{
    internal static unsafe class Mdct
    {
        private static ConcurrentDictionary<int, MdctImpl> _setupCache = new();

        public static void Reverse(float[] samples, float[] buf2, int sampleCount)
        {
            MdctImpl impl = _setupCache.GetOrAdd(sampleCount, static (c) => new MdctImpl(c));
            impl.CalcReverse(samples, buf2);
        }

        private class MdctImpl
        {
            private readonly int _n, _n2, _n4, _n8, _ld;

            private readonly float[] _a, _b, _c;
            private readonly ushort[] _bitrev;

            public MdctImpl(int n)
            {
                _n = n;
                _n2 = n >> 1;
                _n4 = _n2 >> 1;
                _n8 = _n4 >> 1;

                _ld = Utils.ilog(n) - 1;

                // first, calc the "twiddle factors"
                _a = new float[_n2];
                _b = new float[_n2];
                _c = new float[_n4];
                int k, k2;
                for (k = k2 = 0; k < _n4; ++k, k2 += 2)
                {
                    _a[k2] = MathF.Cos(4 * k * MathF.PI / n);
                    _a[k2 + 1] = -MathF.Sin(4 * k * MathF.PI / n);
                    _b[k2] = MathF.Cos((k2 + 1) * MathF.PI / n / 2) * .5f;
                    _b[k2 + 1] = MathF.Sin((k2 + 1) * MathF.PI / n / 2) * .5f;
                }
                for (k = k2 = 0; k < _n8; ++k, k2 += 2)
                {
                    _c[k2] = MathF.Cos(2 * (k2 + 1) * MathF.PI / n);
                    _c[k2 + 1] = -MathF.Sin(2 * (k2 + 1) * MathF.PI / n);
                }

                // now, calc the bit reverse table
                _bitrev = new ushort[_n8];
                for (int i = 0; i < _n8; ++i)
                {
                    _bitrev[i] = (ushort)(Utils.BitReverse((uint)i, _ld - 3) << 2);
                }
            }

            public void CalcReverse(float[] buffer, float[] buf2)
            {
                fixed (float* bufferPtr = buffer)
                fixed (float* buf2Ptr = buf2)
                fixed (float* aa = _a)
                {
                    CalcReverse(bufferPtr, buf2Ptr, aa);
                }
            }

            private void CalcReverse(float* buffer, float* buf2, float* aa)
            {
                // copy and reflect spectral data
                // step 0

                {
                    nint d = _n2 - 2;  // buf2
                    nint AA = 0;       // A
                    nint e = 0;        // buffer
                    nint e_stop = _n2; // buffer

                    while (e != e_stop)
                    {
                        buf2[d + 1] = buffer[e] * aa[AA + 0] - buffer[e + 2] * aa[AA + 1];
                        buf2[d + 0] = buffer[e] * aa[AA + 1] + buffer[e + 2] * aa[AA + 0];
                        d -= 2;
                        AA += 2;
                        e += 4;
                    }

                    e = _n2 - 3;
                    while (d >= 0)
                    {
                        buf2[d + 1] = -buffer[e + 2] * aa[AA + 0] - -buffer[e] * aa[AA + 1];
                        buf2[d + 0] = -buffer[e + 2] * aa[AA + 1] + -buffer[e] * aa[AA + 0];
                        d -= 2;
                        AA += 2;
                        e -= 4;
                    }
                }

                // apply "symbolic" names
                float* u = buffer;
                float* v = buf2;

                // step 2

                {
                    nint AA = _n2 - 8;    // A

                    nint e0 = _n4;        // v
                    nint e1 = 0;         // v

                    nint d0 = _n4;        // u
                    nint d1 = 0;         // u

                    while (AA >= 0)
                    {
                        float v40_20, v41_21;

                        v41_21 = v[e0 + 1] - v[e1 + 1];
                        v40_20 = v[e0 + 0] - v[e1 + 0];
                        u[d0 + 1] = v[e0 + 1] + v[e1 + 1];
                        u[d0 + 0] = v[e0 + 0] + v[e1 + 0];
                        u[d1 + 1] = v41_21 * aa[AA + 4] - v40_20 * aa[AA + 5];
                        u[d1 + 0] = v40_20 * aa[AA + 4] + v41_21 * aa[AA + 5];

                        v41_21 = v[e0 + 3] - v[e1 + 3];
                        v40_20 = v[e0 + 2] - v[e1 + 2];
                        u[d0 + 3] = v[e0 + 3] + v[e1 + 3];
                        u[d0 + 2] = v[e0 + 2] + v[e1 + 2];
                        u[d1 + 3] = v41_21 * aa[AA + 0] - v40_20 * aa[AA + 1];
                        u[d1 + 2] = v40_20 * aa[AA + 0] + v41_21 * aa[AA + 1];

                        AA -= 8;

                        d0 += 4;
                        d1 += 4;
                        e0 += 4;
                        e1 += 4;
                    }
                }

                // step 3

                // iteration 0
                step3_iter0_loop(_n >> 4, u, _n2 - 1 - _n4 * 0, -_n8, aa);
                step3_iter0_loop(_n >> 4, u, _n2 - 1 - _n4 * 1, -_n8, aa);

                // iteration 1
                step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 0, -(_n >> 4), 16, aa);
                step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 1, -(_n >> 4), 16, aa);
                step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 2, -(_n >> 4), 16, aa);
                step3_inner_r_loop(_n >> 5, u, _n2 - 1 - _n8 * 3, -(_n >> 4), 16, aa);

                // iterations 2 ... x
                int l = 2;
                for (; l < (_ld - 3) >> 1; ++l)
                {
                    int k0 = _n >> (l + 2);
                    nint k0_2 = -(k0 >> 1);
                    int lim = 1 << (l + 1);

                    for (int i = 0; i < lim; ++i)
                    {
                        step3_inner_r_loop(_n >> (l + 4), u, _n2 - 1 - k0 * i, k0_2, 1 << (l + 3), aa);
                    }
                }

                // iterations x ... end
                for (; l < _ld - 6; ++l)
                {
                    nint k0 = _n >> (l + 2);
                    nint k1 = 1 << (l + 3);
                    nint k0_2 = -(k0 >> 1);
                    int rlim = _n >> (l + 6);
                    int lim = 1 << l + 1;
                    nint i_off = _n2 - 1;
                    nint A0 = 0;

                    for (int r = rlim; r > 0; --r)
                    {
                        step3_inner_s_loop(lim, u, i_off, k0_2, A0, k1, k0, aa);
                        A0 += k1 * 4;
                        i_off -= 8;
                    }
                }

                // combine some iteration steps...
                step3_inner_s_loop_ld654(_n >> 5, u, _n2 - 1, _n, aa);

                // steps 4, 5, and 6
                fixed (ushort* bitrev = _bitrev)
                {
                    nint bit = 0;
                    nint d0 = _n4 - 4; // v
                    nint d1 = _n2 - 4; // v

                    while (d0 >= 0)
                    {
                        int k4;

                        k4 = bitrev[bit + 0];
                        v[d1 + 3] = u[k4 + 0];
                        v[d1 + 2] = u[k4 + 1];
                        v[d0 + 3] = u[k4 + 2];
                        v[d0 + 2] = u[k4 + 3];

                        k4 = bitrev[bit + 1];
                        v[d1 + 1] = u[k4 + 0];
                        v[d1 + 0] = u[k4 + 1];
                        v[d0 + 1] = u[k4 + 2];
                        v[d0 + 0] = u[k4 + 3];

                        d0 -= 4;
                        d1 -= 4;
                        bit += 2;
                    }
                }

                // step 7
                fixed (float* cc = _c)
                {
                    nint c = 0;      // C
                    nint d = 0;      // v
                    nint e = _n2 - 4; // v

                    while (d < e)
                    {
                        float a02, a11, b0, b1, b2, b3;

                        a02 = v[d + 0] - v[e + 2];
                        a11 = v[d + 1] + v[e + 3];

                        b0 = cc[c + 1] * a02 + cc[c] * a11;
                        b1 = cc[c + 1] * a11 - cc[c] * a02;

                        b2 = v[d + 0] + v[e + 2];
                        b3 = v[d + 1] - v[e + 3];

                        v[d + 0] = b2 + b0;
                        v[d + 1] = b3 + b1;
                        v[e + 2] = b2 - b0;
                        v[e + 3] = b1 - b3;

                        a02 = v[d + 2] - v[e + 0];
                        a11 = v[d + 3] + v[e + 1];

                        b0 = cc[c + 3] * a02 + cc[c + 2] * a11;
                        b1 = cc[c + 3] * a11 - cc[c + 2] * a02;

                        b2 = v[d + 2] + v[e + 0];
                        b3 = v[d + 3] - v[e + 1];

                        v[d + 2] = b2 + b0;
                        v[d + 3] = b3 + b1;
                        v[e + 0] = b2 - b0;
                        v[e + 1] = b1 - b3;

                        c += 4;
                        d += 4;
                        e -= 4;
                    }
                }

                // step 8 + decode
                fixed (float* bb = _b)
                {
                    nint b = _n2 - 8; // B
                    nint e = _n2 - 8; // buf2
                    nint d0 = 0;      // buffer
                    nint d1 = _n2 - 4;// buffer
                    nint d2 = _n2;    // buffer
                    nint d3 = _n - 4; // buffer

                    while (e >= 0)
                    {
                        float p0, p1, p2, p3;

                        p3 = +buf2[e + 6] * bb[b + 7] - buf2[e + 7] * bb[b + 6];
                        p2 = -buf2[e + 6] * bb[b + 6] - buf2[e + 7] * bb[b + 7];

                        buffer[d0 + 0] = p3;
                        buffer[d1 + 3] = -p3;
                        buffer[d2 + 0] = p2;
                        buffer[d3 + 3] = p2;

                        p1 = +buf2[e + 4] * bb[b + 5] - buf2[e + 5] * bb[b + 4];
                        p0 = -buf2[e + 4] * bb[b + 4] - buf2[e + 5] * bb[b + 5];

                        buffer[d0 + 1] = p1;
                        buffer[d1 + 2] = -p1;
                        buffer[d2 + 1] = p0;
                        buffer[d3 + 2] = p0;

                        p3 = +buf2[e + 2] * bb[b + 3] - buf2[e + 3] * bb[b + 2];
                        p2 = -buf2[e + 2] * bb[b + 2] - buf2[e + 3] * bb[b + 3];

                        buffer[d0 + 2] = p3;
                        buffer[d1 + 1] = -p3;
                        buffer[d2 + 2] = p2;
                        buffer[d3 + 1] = p2;

                        p1 = +buf2[e] * bb[b + 1] - buf2[e + 1] * bb[b + 0];
                        p0 = -buf2[e] * bb[b + 0] - buf2[e + 1] * bb[b + 1];

                        buffer[d0 + 3] = p1;
                        buffer[d1 + 0] = -p1;
                        buffer[d2 + 3] = p0;
                        buffer[d3 + 0] = p0;

                        b -= 8;
                        e -= 8;
                        d0 += 4;
                        d2 += 4;
                        d1 -= 4;
                        d3 -= 4;
                    }
                }
            }

            private static void step3_iter0_loop(int n, float* e, nint i_off, nint k_off, float* aa)
            {
                nint ee0 = i_off;        // e
                nint ee2 = ee0 + k_off;  // e
                nint a = 0;

                for (int i = n >> 2; i > 0; --i)
                {
                    float k00_20, k01_21;

                    k00_20 = e[ee0 + 0] - e[ee2 + 0];
                    k01_21 = e[ee0 - 1] - e[ee2 - 1];
                    e[ee0 + 0] += e[ee2 + 0];
                    e[ee0 - 1] += e[ee2 - 1];
                    e[ee2 + 0] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[ee2 - 1] = k01_21 * aa[a] + k00_20 * aa[a + 1];
                    a += 8;

                    k00_20 = e[ee0 - 2] - e[ee2 - 2];
                    k01_21 = e[ee0 - 3] - e[ee2 - 3];
                    e[ee0 - 2] += e[ee2 - 2];
                    e[ee0 - 3] += e[ee2 - 3];
                    e[ee2 - 2] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[ee2 - 3] = k01_21 * aa[a] + k00_20 * aa[a + 1];
                    a += 8;

                    k00_20 = e[ee0 - 4] - e[ee2 - 4];
                    k01_21 = e[ee0 - 5] - e[ee2 - 5];
                    e[ee0 - 4] += e[ee2 - 4];
                    e[ee0 - 5] += e[ee2 - 5];
                    e[ee2 - 4] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[ee2 - 5] = k01_21 * aa[a] + k00_20 * aa[a + 1];
                    a += 8;

                    k00_20 = e[ee0 - 6] - e[ee2 - 6];
                    k01_21 = e[ee0 - 7] - e[ee2 - 7];
                    e[ee0 - 6] += e[ee2 - 6];
                    e[ee0 - 7] += e[ee2 - 7];
                    e[ee2 - 6] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[ee2 - 7] = k01_21 * aa[a] + k00_20 * aa[a + 1];
                    a += 8;

                    ee0 -= 8;
                    ee2 -= 8;
                }
            }

            private static void step3_inner_r_loop(int lim, float* e, nint d0, nint k_off, nint k1, float* aa)
            {
                float k00_20, k01_21;

                nint e0 = d0;            // e
                nint e2 = e0 + k_off;    // e
                nint a = 0;

                for (int i = lim >> 2; i > 0; --i)
                {
                    k00_20 = e[e0 + 0] - e[e2 + 0];
                    k01_21 = e[e0 - 1] - e[e2 - 1];
                    e[e0 + 0] += e[e2 + 0];
                    e[e0 - 1] += e[e2 - 1];
                    e[e2 + 0] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[e2 - 1] = k01_21 * aa[a] + k00_20 * aa[a + 1];

                    a += k1;

                    k00_20 = e[e0 - 2] - e[e2 - 2];
                    k01_21 = e[e0 - 3] - e[e2 - 3];
                    e[e0 - 2] += e[e2 - 2];
                    e[e0 - 3] += e[e2 - 3];
                    e[e2 - 2] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[e2 - 3] = k01_21 * aa[a] + k00_20 * aa[a + 1];

                    a += k1;

                    k00_20 = e[e0 - 4] - e[e2 - 4];
                    k01_21 = e[e0 - 5] - e[e2 - 5];
                    e[e0 - 4] += e[e2 - 4];
                    e[e0 - 5] += e[e2 - 5];
                    e[e2 - 4] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[e2 - 5] = k01_21 * aa[a] + k00_20 * aa[a + 1];

                    a += k1;

                    k00_20 = e[e0 - 6] - e[e2 - 6];
                    k01_21 = e[e0 - 7] - e[e2 - 7];
                    e[e0 - 6] += e[e2 - 6];
                    e[e0 - 7] += e[e2 - 7];
                    e[e2 - 6] = k00_20 * aa[a] - k01_21 * aa[a + 1];
                    e[e2 - 7] = k01_21 * aa[a] + k00_20 * aa[a + 1];

                    a += k1;

                    e0 -= 8;
                    e2 -= 8;
                }
            }

            private static void step3_inner_s_loop(
                int n, float* e, nint i_off, nint k_off, nint a, nint a_off, nint k0, float* aa)
            {
                float A0 = aa[a + 0];
                float A1 = aa[a + 1];
                float A2 = aa[a + a_off];
                float A3 = aa[a + a_off + 1 + 0];
                float A4 = aa[a + a_off * 2 + 0];
                float A5 = aa[a + a_off * 2 + 1];
                float A6 = aa[a + a_off * 3 + 0];
                float A7 = aa[a + a_off * 3 + 1];

                float k00, k11;

                nint ee0 = i_off;        // e
                nint ee2 = ee0 + k_off;  // e

                for (int i = n; i > 0; --i)
                {
                    k00 = e[ee0 + 0] - e[ee2 + 0];
                    k11 = e[ee0 - 1] - e[ee2 - 1];
                    e[ee0 + 0] += e[ee2 + 0];
                    e[ee0 - 1] += e[ee2 - 1];
                    e[ee2 + 0] = k00 * A0 - k11 * A1;
                    e[ee2 - 1] = k11 * A0 + k00 * A1;

                    k00 = e[ee0 - 2] - e[ee2 - 2];
                    k11 = e[ee0 - 3] - e[ee2 - 3];
                    e[ee0 - 2] += e[ee2 - 2];
                    e[ee0 - 3] += e[ee2 - 3];
                    e[ee2 - 2] = k00 * A2 - k11 * A3;
                    e[ee2 - 3] = k11 * A2 + k00 * A3;

                    k00 = e[ee0 - 4] - e[ee2 - 4];
                    k11 = e[ee0 - 5] - e[ee2 - 5];
                    e[ee0 - 4] += e[ee2 - 4];
                    e[ee0 - 5] += e[ee2 - 5];
                    e[ee2 - 4] = k00 * A4 - k11 * A5;
                    e[ee2 - 5] = k11 * A4 + k00 * A5;

                    k00 = e[ee0 - 6] - e[ee2 - 6];
                    k11 = e[ee0 - 7] - e[ee2 - 7];
                    e[ee0 - 6] += e[ee2 - 6];
                    e[ee0 - 7] += e[ee2 - 7];
                    e[ee2 - 6] = k00 * A6 - k11 * A7;
                    e[ee2 - 7] = k11 * A6 + k00 * A7;

                    ee0 -= k0;
                    ee2 -= k0;
                }
            }

            private static void step3_inner_s_loop_ld654(int n, float* e, int i_off, int base_n, float* aa)
            {
                int a_off = base_n >> 3;
                float A2 = aa[a_off];
                nint z = i_off;          // e
                nint @base = z - 16 * n; // e

                while (z > @base)
                {
                    float k00, k11;

                    k00 = e[z + 0] - e[z - 8];
                    k11 = e[z - 1] - e[z - 9];
                    e[z + 0] += e[z - 8];
                    e[z - 1] += e[z - 9];
                    e[z - 8] = k00;
                    e[z - 9] = k11;

                    k00 = e[z - 2] - e[z - 10];
                    k11 = e[z - 3] - e[z - 11];
                    e[z - 2] += e[z - 10];
                    e[z - 3] += e[z - 11];
                    e[z - 10] = (k00 + k11) * A2;
                    e[z - 11] = (k11 - k00) * A2;

                    k00 = e[z - 12] - e[z - 4];
                    k11 = e[z - 5] - e[z - 13];
                    e[z - 4] += e[z - 12];
                    e[z - 5] += e[z - 13];
                    e[z - 12] = k11;
                    e[z - 13] = k00;

                    k00 = e[z - 14] - e[z - 6];
                    k11 = e[z - 7] - e[z - 15];
                    e[z - 6] += e[z - 14];
                    e[z - 7] += e[z - 15];
                    e[z - 14] = (k00 + k11) * A2;
                    e[z - 15] = (k00 - k11) * A2;

                    iter_54(e, z);
                    iter_54(e, z - 8);

                    z -= 16;
                }
            }

            private static void iter_54(float* e, nint z)
            {
                float k00, k11, k22, k33;
                float y0, y1, y2, y3;

                k00 = e[z + 0] - e[z - 4];
                y0 = e[z + 0] + e[z - 4];
                y2 = e[z - 2] + e[z - 6];
                k22 = e[z - 2] - e[z - 6];

                e[z + 0] = y0 + y2;
                e[z - 2] = y0 - y2;

                k33 = e[z - 3] - e[z - 7];

                e[z - 4] = k00 + k33;
                e[z - 6] = k00 - k33;

                k11 = e[z - 1] - e[z - 5];
                y1 = e[z - 1] + e[z - 5];
                y3 = e[z - 3] + e[z - 7];

                e[z - 1] = y1 + y3;
                e[z - 3] = y1 - y3;
                e[z - 5] = k11 - k22;
                e[z - 7] = k11 + k22;
            }
        }
    }
}
