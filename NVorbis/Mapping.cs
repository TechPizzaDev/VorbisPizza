using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using NVorbis.Contracts;

namespace NVorbis
{
    internal sealed class Mapping
    {
        private byte[] _couplingAngle;
        private byte[] _couplingMagnitude;
        private byte[] _mux;
        private byte[] _submapFloor;
        private byte[] _submapResidue;
        private FloorData[] _channelFloorData;
        private float[] _buf2;

        public Mapping(ref VorbisPacket packet, int channels, IFloor[] floors, Residue0[] residues)
        {
            int submapCount = 1;
            if (packet.ReadBit())
            {
                submapCount += (int)packet.ReadBits(4);
            }

            // square polar mapping
            int couplingSteps = 0;
            if (packet.ReadBit())
            {
                couplingSteps = (int)packet.ReadBits(8) + 1;
            }

            int couplingBits = Utils.ilog(channels - 1);
            _couplingAngle = new byte[couplingSteps];
            _couplingMagnitude = new byte[couplingSteps];
            for (int j = 0; j < couplingSteps; j++)
            {
                byte magnitude = (byte)packet.ReadBits(couplingBits);
                byte angle = (byte)packet.ReadBits(couplingBits);
                if (magnitude == angle || magnitude > channels - 1 || angle > channels - 1)
                {
                    throw new System.IO.InvalidDataException("Invalid magnitude or angle in mapping header!");
                }
                _couplingAngle[j] = angle;
                _couplingMagnitude[j] = magnitude;
            }

            if (0 != packet.ReadBits(2))
            {
                throw new System.IO.InvalidDataException("Reserved bits not 0 in mapping header.");
            }

            byte[] mux = new byte[channels];
            if (submapCount > 1)
            {
                for (int c = 0; c < channels; c++)
                {
                    mux[c] = (byte)packet.ReadBits(4);
                    if (mux[c] > submapCount)
                    {
                        throw new System.IO.InvalidDataException("Invalid channel mux submap index in mapping header!");
                    }
                }
            }
            _mux = mux;

            _submapFloor = new byte[submapCount];
            _submapResidue = new byte[submapCount];
            for (int j = 0; j < submapCount; j++)
            {
                packet.SkipBits(8); // unused placeholder
                byte floorNum = (byte)packet.ReadBits(8);
                if (floorNum >= floors.Length)
                {
                    throw new System.IO.InvalidDataException("Invalid floor number in mapping header!");
                }
                byte residueNum = (byte)packet.ReadBits(8);
                if (residueNum >= residues.Length)
                {
                    throw new System.IO.InvalidDataException("Invalid residue number in mapping header!");
                }

                _submapFloor[j] = floorNum;
                _submapResidue[j] = residueNum;
            }

            _channelFloorData = new FloorData[channels];
            for (int c = 0; c < channels; c++)
            {
                _channelFloorData[c] = floors[_submapFloor[mux[c]]].CreateFloorData();
            }

            _buf2 = Array.Empty<float>();
        }

        [SkipLocalsInit]
        public void DecodePacket(
            ref VorbisPacket packet, int blockSize, ChannelBuffer buffer,
            IFloor[] floors, Residue0[] residues, Codebook[] books)
        {
            int halfBlockSize = blockSize / 2;

            int channels = _mux.Length;
            BitStackArray noExecuteChannel = new(stackalloc byte[256 / 8]);

            // read the noise floor data
            FloorData[] floorData = _channelFloorData;
            for (int ch = 0; ch < channels; ch++)
            {
                floorData[ch].Reset(); // TODO: remove

                floors[_submapFloor[_mux[ch]]].Unpack(ref packet, floorData[ch], ch, books);
                noExecuteChannel.Add(!floorData[ch].ExecuteChannel);

                // pre-clear the residue buffers
                buffer.GetSpan(ch).Clear();
            }

            // make sure we handle no-energy channels correctly given the couplings..
            for (int i = 0; i < _couplingAngle.Length; i++)
            {
                byte mag = _couplingMagnitude[i];
                byte angle = _couplingAngle[i];
                if (!(noExecuteChannel[mag] && noExecuteChannel[angle]))
                {
                    noExecuteChannel[mag] = false;
                    noExecuteChannel[angle] = false;
                }
            }

            BitStackArray doNotDecodeFlag = new(stackalloc byte[256 / 8]);
            float[] decodeBuffer = new float[buffer.GetFullSpan().Length];

            // decode the submaps into the residue buffer
            for (int i = 0; i < _submapResidue.Length; i++)
            {
                for (int j = 0; j < channels; j++)
                {
                    if (_mux[j] == i)
                    {
                        doNotDecodeFlag.Add(noExecuteChannel[j]);
                    }
                }

                var chBuffer = new ChannelBuffer(decodeBuffer, doNotDecodeFlag.Count, blockSize);

                var residue = residues[_submapResidue[i]];
                residue.Decode(ref packet, doNotDecodeFlag, blockSize, chBuffer, books);

                int ch = 0;
                for (int j = 0; j < channels; j++)
                {
                    if (_mux[j] == i)
                    {
                        var src = chBuffer.GetSpan(ch).Slice(0, halfBlockSize);
                        src.CopyTo(buffer.GetSpan(j).Slice(0, halfBlockSize));
                        ch += 1;
                    }
                }

                doNotDecodeFlag.Clear();
            }

            // inverse coupling
            for (int i = _couplingAngle.Length - 1; i >= 0; i--)
            {
                // we only have to do the first half; MDCT ignores the last half
                Span<float> magnitudeSpan = buffer.GetSpan(_couplingMagnitude[i]).Slice(0, halfBlockSize);
                Span<float> angleSpan = buffer.GetSpan(_couplingAngle[i]).Slice(0, halfBlockSize);
                ApplyCoupling(magnitudeSpan, angleSpan);
            }

            if (halfBlockSize > _buf2.Length)
            {
                Array.Resize(ref _buf2, halfBlockSize);
            }

            // apply floor / dot product / MDCT (only run if we have sound energy in that channel)
            for (int ch = 0; ch < channels; ch++)
            {
                var span = buffer.GetSpan(ch).Slice(0, blockSize);
                var halfSpan = span.Slice(0, halfBlockSize);

                if (floorData[ch].ExecuteChannel)
                {
                    floors[_submapFloor[_mux[ch]]].Apply(floorData[ch], blockSize, halfSpan);
                    Mdct.Reverse(span, _buf2, blockSize);
                }
                else
                {
                    // since we aren't doing the IMDCT, we have to explicitly clear the back half of the block
                    halfSpan.Clear();
                }
            }
        }

        private static void ApplyCoupling(Span<float> mag, Span<float> ang)
        {
            if (mag.Length != ang.Length)
            {
                throw new InvalidOperationException();
            }

            if (Vector.IsHardwareAccelerated)
            {
                while (mag.Length >= Vector<float>.Count)
                {
                    Vector<float> oldM = VectorHelper.Create<float>(mag);
                    Vector<float> oldA = VectorHelper.Create<float>(ang);

                    Vector<float> posM = Vector.GreaterThan<float>(oldM, Vector<float>.Zero);
                    Vector<float> posA = Vector.GreaterThan<float>(oldA, Vector<float>.Zero);

                    /*             newM; newA;
                         m &  a ==    0    -1
                         m & !a ==    1     0
                        !m &  a ==    0     1
                        !m & !a ==   -1     0
                    */

                    Vector<float> signMask = new Vector<uint>(1u << 31).As<uint, float>() & posM;
                    Vector<float> signedA = oldA ^ signMask;
                    Vector<float> newM = oldM - Vector.AndNot(signedA, posA);
                    Vector<float> newA = oldM + (signedA & posA);

                    newM.CopyTo(mag);
                    newA.CopyTo(ang);

                    mag = mag.Slice(Vector<float>.Count);
                    ang = ang.Slice(Vector<float>.Count);
                }
            }

            for (int j = 0; j < mag.Length; j++)
            {
                float oldM = mag[j];
                float oldA = ang[j];

                float newM = oldM;
                float newA = oldM;

                if (oldM > 0)
                {
                    if (oldA > 0)
                    {
                        newA = oldM - oldA;
                    }
                    else
                    {
                        newM = oldM + oldA;
                    }
                }
                else
                {
                    if (oldA > 0)
                    {
                        newA = oldM + oldA;
                    }
                    else
                    {
                        newM = oldM - oldA;
                    }
                }

                mag[j] = newM;
                ang[j] = newA;
            }
        }
    }
}
