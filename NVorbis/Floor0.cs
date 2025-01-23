using System;
using System.IO;
using NVorbis.Contracts;

namespace NVorbis
{
    // Packed LSP values on dB amplittude and Bark frequency scale.
    // Virtually unused (libvorbis did not use past beta 4). Probably untested.
    internal sealed class Floor0 : IFloor
    {
        private sealed class Data : FloorData
        {
            internal readonly float[] Coeff;
            internal float Amp;

            public Data(float[] coeff)
            {
                Coeff = coeff;
            }

            public override bool ExecuteChannel => Amp != 0; // (ForceEnergy || Amp > 0f) && !ForceNoEnergy;

            public override void Reset()
            {
                Array.Clear(Coeff);
                Amp = 0;
            }
        }

        private BlockSizes _blockSizes;
        private byte _order;
        private ushort _rate;
        private ushort _bark_map_size;
        private byte _ampBits;
        private byte _ampOfs;
        private byte[] _books;
        private Pair<float[]> _wMap;
        private Pair<int[]> _barkMaps;

        public Floor0(ref VorbisPacket packet, BlockSizes blockSizes, Codebook[] codebooks)
        {
            _blockSizes = blockSizes;

            // this is pretty well stolen directly from libvorbis...  BSD license
            _order = (byte)packet.ReadBits(8);
            _rate = (ushort)packet.ReadBits(16);
            _bark_map_size = (ushort)packet.ReadBits(16);
            _ampBits = (byte)packet.ReadBits(6);
            _ampOfs = (byte)packet.ReadBits(8);
            _books = new byte[(int)packet.ReadBits(4) + 1];

            if (_order < 1 || _rate < 1 || _bark_map_size < 1 || _books.Length == 0)
                throw new InvalidDataException();

            for (int i = 0; i < _books.Length; i++)
            {
                byte num = (byte)packet.ReadBits(8);
                if (num < 0 || num >= codebooks.Length)
                    throw new InvalidDataException();

                Codebook book = codebooks[num];
                if (book.MapType == 0 || book.Dimensions < 1)
                    throw new InvalidDataException();

                _books[i] = num;
            }

            _barkMaps = new(
                SynthesizeBarkCurve(blockSizes.Size0 / 2),
                SynthesizeBarkCurve(blockSizes.Size1 / 2));

            _wMap = new(
                SynthesizeWDelMap(blockSizes.Size0 / 2),
                SynthesizeWDelMap(blockSizes.Size1 / 2));
        }

        public FloorData CreateFloorData()
        {
            return new Data(new float[_order + 1]);
        }

        private int[] SynthesizeBarkCurve(int n)
        {
            float scale = _bark_map_size / ToBARK(_rate / 2.0);

            int[] map = new int[n + 1];

            for (int i = 0; i < map.Length - 2; i++)
            {
                map[i] = Math.Min(_bark_map_size - 1, (int)Math.Floor(ToBARK((_rate / 2.0) / n * i) * scale));
            }
            map[n] = -1;
            return map;
        }

        private static float ToBARK(double lsp)
        {
            return (float)(13.1 * Math.Atan(0.00074 * lsp) + 2.24 * Math.Atan(0.0000000185 * lsp * lsp) + .0001 * lsp);
        }

        private float[] SynthesizeWDelMap(int n)
        {
            float wdel = (float)(Math.PI / _bark_map_size);

            float[] map = new float[n];
            for (int i = 0; i < map.Length; i++)
            {
                map[i] = 2f * MathF.Cos(wdel * i);
            }
            return map;
        }

        public void Unpack(ref VorbisPacket packet, FloorData floorData, int channel, Codebook[] books)
        {
            Data data = (Data)floorData;

            // this is pretty well stolen directly from libvorbis...  BSD license
            data.Coeff.AsSpan().Clear();

            ulong amp = packet.ReadBits(_ampBits);
            double ampDiv = (1 << _ampBits) - 1;
            data.Amp = (float)(amp * _ampOfs / ampDiv);

            uint bookNum = (uint)packet.ReadBits(Utils.ilog(_books.Length));
            if (bookNum >= (uint)_books.Length)
            {
                // we ran out of data or the packet is corrupt...  0 the floor and return
                data.Amp = 0;
                return;
            }
            Codebook book = books[_books[bookNum]];

            // first, the book decode...
            for (int i = 0; i < _order;)
            {
                int entry = book.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    // we ran out of data or the packet is corrupt...  0 the floor and return
                    data.Amp = 0;
                    return;
                }

                ReadOnlySpan<float> lookup = book.GetLookup(entry);
                for (int j = 0; i < _order && j < lookup.Length; j++, i++)
                {
                    data.Coeff[i] = lookup[j];
                }
            }

            // then, the "averaging"
            int dim = book.Dimensions;
            float last = 0f;
            for (int j = 0; j < _order;)
            {
                for (int k = 0; j < _order && k < dim; j++, k++)
                {
                    data.Coeff[j] += last;
                }
                last = data.Coeff[j - 1];
            }
        }

        public void Apply(FloorData floorData, int blockSize, Span<float> residue)
        {
            Data data = (Data)floorData;
            int n = blockSize / 2;

            if (data.Amp <= 0f)
            {
                residue.Slice(0, n).Clear();
                return;
            }

            // this is pretty well stolen directly from libvorbis...  BSD license
            int blockSizeIdx = _blockSizes.IndexOf(blockSize);
            int[] barkMap = _barkMaps[blockSizeIdx];
            float[] wMap = _wMap[blockSizeIdx];

            Span<float> coeff = data.Coeff.AsSpan(0, _order);
            for (int j = 0; j < coeff.Length; j++)
            {
                coeff[j] = 2f * MathF.Cos(coeff[j]);
            }

            float ampOfs = _ampOfs;
            int i = 0;
            while (i < n)
            {
                int j;
                int k = barkMap[i];
                float p = .5f;
                float q = .5f;
                float w = wMap[k];
                for (j = 1; j < _order; j += 2)
                {
                    q *= w - data.Coeff[j - 1];
                    p *= w - data.Coeff[j];
                }
                if (j == _order)
                {
                    // odd order filter; slightly assymetric
                    q *= w - data.Coeff[j - 1];
                    p *= p * (4f - w * w);
                    q *= q;
                }
                else
                {
                    // even order filter; still symetric
                    p *= p * (2f - w);
                    q *= q * (2f + w);
                }

                // calc the dB of this bark section
                q = data.Amp / MathF.Sqrt(p + q) - ampOfs;

                // now convert to a linear sample multiplier
                q = MathF.Exp(q * 0.11512925f);

                residue[i] *= q;

                while (barkMap[++i] == k)
                    residue[i] *= q;
            }
        }
    }
}
