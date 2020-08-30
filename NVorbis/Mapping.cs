using System;
using NVorbis.Contracts;

namespace NVorbis
{
    internal class Mapping : IMapping
    {
        private IMdct _mdct;
        private int[] _couplingAngle;
        private int[] _couplingMangitude;
        private IFloor[] _submapFloor;
        private IResidue[] _submapResidue;

        private IFloorData[] _floorData;
        private bool[] _noExecuteChannel;
        private IFloor[] _channelFloor;
        private IResidue[] _channelResidue;

        public void Init(IPacket packet, int channels, IFloor[] floors, IResidue[] residues, IMdct mdct)
        {
            int submapCount = 1;
            if (packet.ReadBit())
                submapCount += (int)packet.ReadBits(4);

            // square polar mapping
            int couplingSteps = 0;
            if (packet.ReadBit())
                couplingSteps = (int)packet.ReadBits(8) + 1;

            int couplingBits = Utils.ILog(channels - 1);
            _couplingAngle = new int[couplingSteps];
            _couplingMangitude = new int[couplingSteps];
            for (int j = 0; j < couplingSteps; j++)
            {
                int magnitude = (int)packet.ReadBits(couplingBits);
                int angle = (int)packet.ReadBits(couplingBits);
                if (magnitude == angle || magnitude > channels - 1 || angle > channels - 1)
                    throw new System.IO.InvalidDataException("Invalid magnitude or angle in mapping header.");

                _couplingAngle[j] = angle;
                _couplingMangitude[j] = magnitude;
            }

            if (0 != packet.ReadBits(2))
                throw new System.IO.InvalidDataException("Reserved bits not 0 in mapping header.");

            var mux = new int[channels];
            if (submapCount > 1)
            {
                for (int c = 0; c < channels; c++)
                {
                    mux[c] = (int)packet.ReadBits(4);
                    if (mux[c] > submapCount)
                        throw new System.IO.InvalidDataException("Invalid channel mux submap index in mapping header.");
                }
            }

            _submapFloor = new IFloor[submapCount];
            _submapResidue = new IResidue[submapCount];
            for (int j = 0; j < submapCount; j++)
            {
                packet.SkipBits(8); // unused placeholder
                int floorNum = (int)packet.ReadBits(8);
                if (floorNum >= floors.Length)
                    throw new System.IO.InvalidDataException("Invalid floor number in mapping header.");

                int residueNum = (int)packet.ReadBits(8);
                if (residueNum >= residues.Length)
                    throw new System.IO.InvalidDataException("Invalid residue number in mapping header.");

                _submapFloor[j] = floors[floorNum];
                _submapResidue[j] = residues[residueNum];
            }

            _floorData = new IFloorData[channels];
            _noExecuteChannel = new bool[channels];
            _channelFloor = new IFloor[channels];
            _channelResidue = new IResidue[channels];
            for (int c = 0; c < channels; c++)
            {
                _channelFloor[c] = _submapFloor[mux[c]];
                _channelResidue[c] = _submapResidue[mux[c]];
            }

            _mdct = mdct;
        }

        public void DecodePacket(IPacket packet, int blockSize, int channels, float[][] buffer)
        {
            int halfBlockSize = blockSize / 2;

            // read the noise floor data
            for (int i = 0; i < _channelFloor.Length; i++)
            {
                _floorData[i] = _channelFloor[i].Unpack(packet, blockSize, i);
                _noExecuteChannel[i] = !_floorData[i].ExecuteChannel;

                // pre-clear the residue buffers
                Array.Clear(buffer[i], 0, halfBlockSize);
            }

            // make sure we handle no-energy channels correctly given the couplings..
            for (int i = 0; i < _couplingAngle.Length; i++)
            {
                if (_floorData[_couplingAngle[i]].ExecuteChannel || _floorData[_couplingMangitude[i]].ExecuteChannel)
                {
                    _floorData[_couplingAngle[i]].ForceEnergy = true;
                    _floorData[_couplingMangitude[i]].ForceEnergy = true;
                }
            }

            // decode the submaps into the residue buffer
            for (int i = 0; i < _submapFloor.Length; i++)
            {
                for (int j = 0; j < _channelFloor.Length; j++)
                {
                    if (_submapFloor[i] != _channelFloor[j] || _submapResidue[i] != _channelResidue[j])
                    {
                        // the submap doesn't match, so this floor doesn't contribute
                        _floorData[j].ForceNoEnergy = true;
                    }
                }

                _submapResidue[i].Decode(packet, _noExecuteChannel, blockSize, buffer);
            }

            // inverse coupling
            for (int i = _couplingAngle.Length - 1; i >= 0; i--)
            {
                if (_floorData[_couplingAngle[i]].ExecuteChannel || _floorData[_couplingMangitude[i]].ExecuteChannel)
                {
                    float[] magnitude = buffer[_couplingMangitude[i]];
                    float[] angle = buffer[_couplingAngle[i]];

                    // we only have to do the first half; MDCT ignores the last half
                    for (int j = 0; j < halfBlockSize; j++)
                    {
                        float newM, newA;

                        var oldM = magnitude[j];
                        var oldA = angle[j];
                        if (oldM > 0)
                        {
                            if (oldA > 0)
                            {
                                newM = oldM;
                                newA = oldM - oldA;
                            }
                            else
                            {
                                newA = oldM;
                                newM = oldM + oldA;
                            }
                        }
                        else
                        {
                            if (oldA > 0)
                            {
                                newM = oldM;
                                newA = oldM + oldA;
                            }
                            else
                            {
                                newA = oldM;
                                newM = oldM - oldA;
                            }
                        }

                        magnitude[j] = newM;
                        angle[j] = newA;
                    }
                }
            }

            // apply floor / dot product / MDCT (only run if we have sound energy in that channel)
            for (int c = 0; c < _channelFloor.Length; c++)
            {
                if (_floorData[c].ExecuteChannel)
                {
                    _channelFloor[c].Apply(_floorData[c], blockSize, buffer[c]);
                    _mdct.Reverse(buffer[c], blockSize);
                }
                else
                {
                    // since we aren't doing the IMDCT, we have to explicitly clear the back half of the block
                    Array.Clear(buffer[c], halfBlockSize, halfBlockSize);
                }
            }
        }
    }
}
