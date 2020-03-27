﻿/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.IO;

namespace NVorbis
{
    class VorbisMode
    {
        internal static VorbisMode Init(VorbisStreamDecoder vorbis, VorbisDataPacket packet)
        {
            var mode = new VorbisMode(vorbis);
            mode.BlockFlag = packet.ReadBit();
            mode.WindowType = (int)packet.ReadBits(16);
            mode.TransformType = (int)packet.ReadBits(16);
            var mapping = (int)packet.ReadBits(8);

            if (mode.WindowType != 0 ||
                mode.TransformType != 0 ||
                mapping >= vorbis.Maps.Length) 
                throw new InvalidDataException();

            mode.Mapping = vorbis.Maps[mapping];
            mode.BlockSize = mode.BlockFlag ? vorbis.Block1Size : vorbis.Block0Size;

            // now pre-calc the window(s)...
            if (mode.BlockFlag)
            {
                // long block
                mode._windows = new float[4][];
                mode._windows[0] = new float[vorbis.Block1Size];
                mode._windows[1] = new float[vorbis.Block1Size];
                mode._windows[2] = new float[vorbis.Block1Size];
                mode._windows[3] = new float[vorbis.Block1Size];
            }
            else
            {
                // short block
                mode._windows = new float[1][];
                mode._windows[0] = new float[vorbis.Block0Size];
            }
            mode.CalcWindows();

            return mode;
        }

        VorbisStreamDecoder _vorbis;

        float[][] _windows;

        private VorbisMode(VorbisStreamDecoder vorbis)
        {
            _vorbis = vorbis;
        }

        void CalcWindows()
        {
            // 0: prev = s, next = s || BlockFlag = false
            // 1: prev = l, next = s
            // 2: prev = s, next = l
            // 3: prev = l, next = l

            for (int idx = 0; idx < _windows.Length; idx++)
            {
                var array = _windows[idx];

                int left = ((idx & 1) == 0 ? _vorbis.Block0Size : _vorbis.Block1Size) / 2;
                int wnd = BlockSize;
                int right = ((idx & 2) == 0 ? _vorbis.Block0Size : _vorbis.Block1Size) / 2;

                int leftbegin = wnd / 4 - left / 2;
                int rightbegin = wnd - wnd / 4 - right / 2;

                for (int i = 0; i < left; i++)
                {
                    float x = MathF.Sin((i + 0.5f) / left * MathF.PI / 2);
                    x *= x;
                    array[leftbegin + i] = MathF.Sin(x * MathF.PI / 2);
                }

                for (int i = leftbegin + left; i < rightbegin; i++)
                    array[i] = 1.0f;
                
                for (int i = 0; i < right; i++)
                {
                    float x = MathF.Sin((right - i - 0.5f) / right * MathF.PI / 2);
                    x *= x;
                    array[rightbegin + i] = MathF.Sin(x * MathF.PI / 2);
                }
            }
        }

        internal bool BlockFlag;
        internal int WindowType;
        internal int TransformType;
        internal VorbisMapping Mapping;
        internal int BlockSize;

        internal float[] GetWindow(bool prev, bool next)
        {
            if (BlockFlag)
            {
                if (next)
                {
                    if (prev) 
                        return _windows[3];
                    return _windows[2];
                }
                else if (prev)
                {
                    return _windows[1];
                }
            }

            return _windows[0];
        }
    }
}
