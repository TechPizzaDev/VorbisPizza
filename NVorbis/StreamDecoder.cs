using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NVorbis.Contracts;
using NVorbis.Ogg;

namespace NVorbis
{
    /// <summary>
    /// Implements a stream decoder for Vorbis data.
    /// </summary>
    public sealed class StreamDecoder : IStreamDecoder, IPacketGranuleCountProvider
    {
        private IPacketProvider _packetProvider;
        private StreamStats _stats;

        private byte _channels;
        private int _sampleRate;
        private int _block0Size;
        private int _block1Size;
        private Mode[] _modes;
        private int _modeFieldBits;

        private byte[] _utf8Vendor;
        private byte[][] _utf8Comments;
        private ITagData? _tags;

        private long _currentPosition;
        private bool _hasClipped;
        private bool _hasPosition;
        private bool _eosFound;

        private float[][]? _nextPacketBuf;
        private float[][]? _prevPacketBuf;
        private int _prevPacketStart;
        private int _prevPacketEnd;
        private int _prevPacketStop;

        /// <summary>
        /// Creates a new instance of <see cref="StreamDecoder"/>.
        /// </summary>
        /// <param name="packetProvider">A <see cref="IPacketProvider"/> instance for the decoder to read from.</param>
        public StreamDecoder(IPacketProvider packetProvider)
        {
            _packetProvider = packetProvider ?? throw new ArgumentNullException(nameof(packetProvider));

            _stats = new StreamStats();

            _utf8Vendor = Array.Empty<byte>();
            _utf8Comments = Array.Empty<byte[]>();
            _modes = Array.Empty<Mode>();

            _currentPosition = 0L;
            ClipSamples = true;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            VorbisPacket packet = _packetProvider.GetNextPacket();
            if (!packet.IsValid)
                throw new InvalidDataException();

            if (!ProcessHeaderPackets(ref packet))
            {
                packet.Reset();
                Dispose();

                throw GetInvalidStreamException(ref packet);
            }
        }

        private static Exception GetInvalidStreamException(ref VorbisPacket packet)
        {
            try
            {
                // let's give our caller some helpful hints about what they've encountered...
                ulong header = packet.ReadBits(64);
                if (header == 0x646165487375704ful)
                {
                    return new ArgumentException("Found OPUS bitstream.");
                }
                else if ((header & 0xFF) == 0x7F)
                {
                    return new ArgumentException("Found FLAC bitstream.");
                }
                else if (header == 0x2020207865657053ul)
                {
                    return new ArgumentException("Found Speex bitstream.");
                }
                else if (header == 0x0064616568736966ul)
                {
                    // ugh...  we need to add support for this in the container reader
                    return new ArgumentException("Found Skeleton metadata bitstream.");
                }
                else if ((header & 0xFFFFFFFFFFFF00ul) == 0x61726f65687400ul)
                {
                    return new ArgumentException("Found Theora bitsream.");
                }
                return new ArgumentException("Could not find Vorbis data to decode.");
            }
            finally
            {
                packet.Finish();
            }
        }

        #region Init

        private bool ProcessHeaderPackets(ref VorbisPacket headerPacket)
        {
            try
            {
                if (!LoadStreamHeader(ref headerPacket))
                    return false;
            }
            finally
            {
                headerPacket.Finish();
            }

            VorbisPacket commentPacket = _packetProvider.GetNextPacket();
            if (!commentPacket.IsValid)
                return false;
            try
            {
                if (!LoadComments(ref commentPacket))
                    return false;
            }
            finally
            {
                commentPacket.Finish();
            }

            VorbisPacket bookPacket = _packetProvider.GetNextPacket();
            if (!bookPacket.IsValid)
                return false;
            try
            {
                if (!LoadBooks(ref bookPacket))
                    return false;
            }
            finally
            {
                bookPacket.Finish();
            }

            _currentPosition = 0;
            ResetDecoder();
            _hasPosition = true;

            return true;
        }

        private static ReadOnlySpan<byte> PacketSignatureStream => new byte[] { 0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73, 0x00, 0x00, 0x00, 0x00 };
        private static ReadOnlySpan<byte> PacketSignatureComments => new byte[] { 0x03, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };
        private static ReadOnlySpan<byte> PacketSignatureBooks => new byte[] { 0x05, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };

        private static bool ValidateHeader(ref VorbisPacket packet, ReadOnlySpan<byte> expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                ulong v = packet.ReadBits(8);
                if (expected[i] != v)
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] ReadString(ref VorbisPacket packet, bool skip)
        {
            int byteLength = (int)packet.ReadBits(32);
            if (byteLength != 0)
            {
                if (!skip)
                {
                    return packet.ReadBytes(byteLength);
                }
                packet.SkipBytes(byteLength);
            }
            return Array.Empty<byte>();
        }

        private bool LoadStreamHeader(ref VorbisPacket packet)
        {
            if (!ValidateHeader(ref packet, PacketSignatureStream))
            {
                return false;
            }

            _channels = (byte)packet.ReadBits(8);
            _sampleRate = (int)packet.ReadBits(32);
            UpperBitrate = (int)packet.ReadBits(32);
            NominalBitrate = (int)packet.ReadBits(32);
            LowerBitrate = (int)packet.ReadBits(32);

            _block0Size = 1 << (int)packet.ReadBits(4);
            _block1Size = 1 << (int)packet.ReadBits(4);

            if (NominalBitrate == 0 && UpperBitrate > 0 && LowerBitrate > 0)
            {
                NominalBitrate = (UpperBitrate + LowerBitrate) / 2;
            }

            _stats.SetSampleRate(_sampleRate);
            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private bool LoadComments(ref VorbisPacket packet)
        {
            if (!ValidateHeader(ref packet, PacketSignatureComments))
            {
                return false;
            }

            _utf8Vendor = ReadString(ref packet, SkipTags);

            _utf8Comments = new byte[packet.ReadBits(32)][];
            for (int i = 0; i < _utf8Comments.Length; i++)
            {
                _utf8Comments[i] = ReadString(ref packet, SkipTags);
            }

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private bool LoadBooks(ref VorbisPacket packet)
        {
            if (!ValidateHeader(ref packet, PacketSignatureBooks))
            {
                return false;
            }

            // read the books
            Codebook[] books = new Codebook[packet.ReadBits(8) + 1];
            for (int i = 0; i < books.Length; i++)
            {
                books[i] = new Codebook(ref packet);
            }

            // Vorbis never used this feature, so we just skip the appropriate number of bits
            int times = (int)packet.ReadBits(6) + 1;
            packet.SkipBits(16 * times);

            // read the floors
            IFloor[] floors = new IFloor[packet.ReadBits(6) + 1];
            for (int i = 0; i < floors.Length; i++)
            {
                floors[i] = CreateFloor(ref packet, _block0Size, _block1Size, books);
            }

            // read the residues
            Residue0[] residues = new Residue0[packet.ReadBits(6) + 1];
            for (int i = 0; i < residues.Length; i++)
            {
                residues[i] = CreateResidue(ref packet, _channels, books);
            }

            // read the mappings
            Mapping[] mappings = new Mapping[packet.ReadBits(6) + 1];
            for (int i = 0; i < mappings.Length; i++)
            {
                mappings[i] = CreateMapping(ref packet, _channels, floors, residues);
            }

            // read the modes
            _modes = new Mode[packet.ReadBits(6) + 1];
            for (int i = 0; i < _modes.Length; i++)
            {
                _modes[i] = new Mode(ref packet, _channels, _block0Size, _block1Size, mappings);
            }

            // verify the closing bit
            if (!packet.ReadBit())
                throw new InvalidDataException("Book packet did not end on correct bit!");

            // save off the number of bits to read to determine packet mode
            _modeFieldBits = Utils.ilog(_modes.Length - 1);

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private static IFloor CreateFloor(ref VorbisPacket packet, int block0Size, int block1Size, Codebook[] codebooks)
        {
            int type = (int)packet.ReadBits(16);
            return type switch
            {
                0 => new Floor0(ref packet, block0Size, block1Size, codebooks),
                1 => new Floor1(ref packet, codebooks),
                _ => throw new InvalidDataException("Invalid floor type!"),
            };
        }

        private static Mapping CreateMapping(ref VorbisPacket packet, int channels, IFloor[] floors, Residue0[] residues)
        {
            if (packet.ReadBits(16) != 0)
            {
                throw new InvalidDataException("Invalid mapping type!");
            }
            return new Mapping(ref packet, channels, floors, residues);
        }

        private static Residue0 CreateResidue(ref VorbisPacket packet, int channels, Codebook[] codebooks)
        {
            int type = (int)packet.ReadBits(16);
            return type switch
            {
                0 => new Residue0(ref packet, channels, codebooks),
                1 => new Residue1(ref packet, channels, codebooks),
                2 => new Residue2(ref packet, channels, codebooks),
                _ => throw new InvalidDataException("Invalid residue type!"),
            };
        }

        #endregion

        private void ResetDecoder()
        {
            _prevPacketBuf = null;
            _prevPacketStart = 0;
            _prevPacketEnd = 0;
            _prevPacketStop = 0;
            _nextPacketBuf = null;
            _eosFound = false;
            _hasClipped = false;
            _hasPosition = false;
        }

        #region Decoding

        /// <inheritdoc/>
        public int Read(Span<float> buffer)
        {
            return Read(buffer, buffer.Length / _channels, 0, interleave: true);
        }

        /// <inheritdoc/>
        public int Read(Span<float> buffer, int samplesToRead, int channelStride)
        {
            return Read(buffer, samplesToRead, channelStride, interleave: false);
        }

        private unsafe int Read(Span<float> buffer, int samplesToRead, int channelStride, bool interleave)
        {
            // if the caller didn't ask for any data, bail early
            if (buffer.Length == 0)
            {
                return 0;
            }

            int channels = _channels;
            if (buffer.Length % channels != 0)
            {
                throw new ArgumentException("Length must be a multiple of Channels.", nameof(buffer));
            }
            if (buffer.Length < samplesToRead * channels)
            {
                throw new ArgumentException("The buffer is too small for the requested amount.");
            }
            if (_packetProvider == null)
            {
                throw new ObjectDisposedException(nameof(StreamDecoder));
            }

            // save off value to track when we're done with the request
            int idx = 0;
            int tgt = samplesToRead;

            // try to fill the buffer; drain the last buffer if EOS, resync, bad packet, or parameter change
            while (idx < tgt)
            {
                // if we don't have any more valid data in the current packet, read in the next packet
                if (_prevPacketStart == _prevPacketEnd)
                {
                    if (_eosFound)
                    {
                        // no more samples, so just return
                        break;
                    }

                    if (!ReadNextPacket(idx, out long samplePosition))
                    {
                        // drain the current packet (the windowing will fade it out)
                        _prevPacketEnd = _prevPacketStop;
                    }

                    // if we need to pick up a position, and the packet had one, apply the position now
                    if (samplePosition != -1 && !_hasPosition)
                    {
                        _hasPosition = true;
                        _currentPosition = samplePosition - (_prevPacketEnd - _prevPacketStart) - idx;
                    }
                }

                // we read out the valid samples from the previous packet
                int copyLen = Math.Min(tgt - idx, _prevPacketEnd - _prevPacketStart);
                if (copyLen > 0)
                {
                    if (interleave)
                    {
                        fixed (float* target = buffer.Slice(idx * channels, copyLen * channels))
                        {
                            if (ClipSamples)
                            {
                                ClippingCopyBuffer(target, copyLen);
                            }
                            else
                            {
                                CopyBuffer(target, copyLen);
                            }
                        }
                    }
                    else
                    {
                        CopyBufferContiguous(buffer.Slice(idx), copyLen, channelStride, ClipSamples);
                    }

                    idx += copyLen;
                    _prevPacketStart += copyLen;
                }
            }

            // update the position
            _currentPosition += idx;

            // return count of floats written
            return idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private unsafe void ClippingCopyBuffer(float* target, int count)
        {
            float[][]? prevPacketBuf = _prevPacketBuf;
            Debug.Assert(prevPacketBuf != null);

            int channels = _channels;
            int j = 0;

            if (Sse.IsSupported)
            {
                Vector128<float> clipped = Vector128<float>.Zero;

                if (_channels == 2)
                {
                    fixed (float* prev0Ptr = prevPacketBuf[0])
                    fixed (float* prev1Ptr = prevPacketBuf[1])
                    {
                        float* prev0 = prev0Ptr + _prevPacketStart;
                        float* prev1 = prev1Ptr + _prevPacketStart;

                        for (; j + Vector128<float>.Count <= count; j += Vector128<float>.Count)
                        {
                            Vector128<float> p0 = Unsafe.ReadUnaligned<Vector128<float>>(prev0 + j);
                            Vector128<float> p1 = Unsafe.ReadUnaligned<Vector128<float>>(prev1 + j);

                            Vector128<float> t0 = Sse.Shuffle(p0, p1, 0b01_00_01_00);
                            Vector128<float> ts0 = Sse.Shuffle(t0, t0, 0b11_01_10_00);

                            Vector128<float> t1 = Sse.Shuffle(p0, p1, 0b11_10_11_10);
                            Vector128<float> ts1 = Sse.Shuffle(t1, t1, 0b11_01_10_00);

                            ts0 = Utils.ClipValue(ts0, ref clipped);
                            ts1 = Utils.ClipValue(ts1, ref clipped);

                            Unsafe.WriteUnaligned(target + j * 2, ts0);
                            Unsafe.WriteUnaligned(target + j * 2 + Vector128<float>.Count, ts1);
                        }
                    }
                }
                else if (_channels == 1)
                {
                    fixed (float* prev0Ptr = prevPacketBuf[0])
                    {
                        float* prev0 = prev0Ptr + _prevPacketStart;

                        for (; j + Vector128<float>.Count <= count; j += Vector128<float>.Count)
                        {
                            Vector128<float> p0 = Unsafe.ReadUnaligned<Vector128<float>>(prev0 + j);
                            p0 = Utils.ClipValue(p0, ref clipped);
                            Unsafe.WriteUnaligned(target + j, p0);
                        }
                    }
                }

                Vector128<float> mask = Sse.CompareEqual(clipped, Vector128<float>.Zero);
                _hasClipped |= Sse.MoveMask(mask) != 0xf;
            }

            for (int ch = 0; ch < channels; ch++)
            {
                fixed (float* prevPtr = prevPacketBuf[ch])
                {
                    float* prev = prevPtr + _prevPacketStart;
                    float* tar = target + ch;

                    for (int i = j; i < count; i++)
                    {
                        tar[i * channels] = Utils.ClipValue(prev[i], ref _hasClipped);
                    }
                }
            }
        }

        private unsafe void CopyBuffer(float* target, int count)
        {
            float[][]? prevPacketBuf = _prevPacketBuf;
            Debug.Assert(prevPacketBuf != null);

            int channels = _channels;

            for (int ch = 0; ch < channels; ch++)
            {
                fixed (float* prevPtr = prevPacketBuf[ch])
                {
                    float* prev = prevPtr + _prevPacketStart;
                    float* tar = target + ch;

                    for (int i = 0; i < count; i++)
                    {
                        tar[i * channels] = prev[i];
                    }
                }
            }
        }

        private unsafe void CopyBufferContiguous(Span<float> buffer, int count, int channelStride, bool clip)
        {
            float[][]? prevPacketBuf = _prevPacketBuf;
            Debug.Assert(prevPacketBuf != null);

            for (int i = 0; i < _channels; i++)
            {
                Span<float> destination = buffer.Slice(i * channelStride, count);
                Span<float> source = prevPacketBuf[i].AsSpan(_prevPacketStart, count);

                if (clip)
                {
                    fixed (float* dst = destination)
                    fixed (float* src = source)
                    {
                        int j = 0;

                        if (Vector.IsHardwareAccelerated)
                        {
                            Vector<int> clipped = Vector<int>.Zero;

                            for (; j + Vector<float>.Count <= count; j += Vector<float>.Count)
                            {
                                Vector<float> p0 = Unsafe.ReadUnaligned<Vector<float>>(src + j);
                                p0 = Utils.ClipValue(p0, ref clipped);
                                Unsafe.WriteUnaligned(dst + j, p0);
                            }

                            _hasClipped |= !Vector.EqualsAll(clipped, Vector<int>.Zero);
                        }

                        for (; j < count; j++)
                        {
                            dst[j] = Utils.ClipValue(src[j], ref _hasClipped);
                        }
                    }
                }
                else
                {
                    source.CopyTo(destination);
                }
            }
        }

        private bool ReadNextPacket(nint bufferedSamples, out long samplePosition)
        {
            // decode the next packet now so we can start overlapping with it
            float[][]? curPacket = DecodeNextPacket(
                out int startIndex, out int validLen, out int totalLen, out bool isEndOfStream,
                out samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits);

            _eosFound |= isEndOfStream;
            if (curPacket == null)
            {
                _stats.AddPacket(0, bitsRead, bitsRemaining, containerOverheadBits);
                return false;
            }

            // if we get a max sample position, back off our valid length to match
            if (samplePosition != -1 && isEndOfStream)
            {
                long actualEnd = _currentPosition + bufferedSamples + validLen - startIndex;
                int diff = (int)(samplePosition - actualEnd);
                if (diff < 0)
                {
                    validLen += diff;
                }
            }

            // start overlapping (if we don't have an previous packet data,
            // just loop and the previous packet logic will handle things appropriately)
            if (_prevPacketEnd > 0)
            {
                Debug.Assert(_prevPacketBuf != null);

                // overlap the first samples in the packet with the previous packet, then loop
                OverlapBuffers(_prevPacketBuf, curPacket, _prevPacketStart, _prevPacketStop, startIndex, _channels);
                _prevPacketStart = startIndex;
            }
            else if (_prevPacketBuf == null)
            {
                // first packet, so it doesn't have any good data before the valid length
                _prevPacketStart = validLen;
            }

            // update stats
            _stats.AddPacket(validLen - _prevPacketStart, bitsRead, bitsRemaining, containerOverheadBits);

            // keep the old buffer so the GC doesn't have to reallocate every packet
            _nextPacketBuf = _prevPacketBuf;

            // save off our current packet's data for the next pass
            _prevPacketEnd = validLen;
            _prevPacketStop = totalLen;
            _prevPacketBuf = curPacket;
            return true;
        }

        private float[][]? DecodeNextPacket(
            out int packetStartIndex, out int packetValidLength, out int packetTotalLength, out bool isEndOfStream,
            out long samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits)
        {
            VorbisPacket packet = _packetProvider.GetNextPacket();
            if (!packet.IsValid)
            {
                // no packet? we're at the end of the stream
                isEndOfStream = true;
                bitsRead = 0;
                bitsRemaining = 0;
                containerOverheadBits = 0;
            }
            else
            {
                try
                {
                    // if the packet is flagged as the end of the stream, we can safely mark _eosFound
                    isEndOfStream = packet.IsEndOfStream;

                    // resync... that means we've probably lost some data; pick up a new position
                    if (packet.IsResync)
                    {
                        _hasPosition = false;
                    }

                    // grab the container overhead now, since the read won't affect it
                    containerOverheadBits = packet.ContainerOverheadBits;

                    // make sure the packet starts with a 0 bit as per the spec
                    if (packet.ReadBits(1) == 0)
                    {
                        // if we get here, we should have a good packet; decode it and add it to the buffer
                        ref Mode mode = ref _modes[(int)packet.ReadBits(_modeFieldBits)];
                        if (_nextPacketBuf == null)
                        {
                            _nextPacketBuf = new float[_channels][];
                            for (int i = 0; i < _channels; i++)
                            {
                                _nextPacketBuf[i] = new float[_block1Size];
                            }
                        }
                        if (mode.Decode(ref packet, _nextPacketBuf, out packetStartIndex, out packetValidLength, out packetTotalLength))
                        {
                            // per the spec, do not decode more samples than the last granulePosition
                            samplePosition = packet.GranulePosition;
                            bitsRead = packet.BitsRead;
                            bitsRemaining = packet.BitsRemaining;
                            return _nextPacketBuf;
                        }
                    }
                    bitsRead = packet.BitsRead;
                    bitsRemaining = packet.BitsRead + packet.BitsRemaining;
                }
                finally
                {
                    packet.Finish();
                }
            }

            packetStartIndex = 0;
            packetValidLength = 0;
            packetTotalLength = 0;
            samplePosition = -1;
            return null;
        }

        private static unsafe void OverlapBuffers(
            float[][] previous, float[][] next, int prevStart, int prevLen, int nextStart, int channels)
        {
            nint length = prevLen - prevStart;
            for (int c = 0; c < channels; c++)
            {
                Span<float> prevSpan = previous[c].AsSpan(prevStart, (int)length);
                Span<float> nextSpan = next[c].AsSpan(nextStart, (int)length);

                fixed (float* p = prevSpan)
                fixed (float* n = nextSpan)
                {
                    nint i = 0;
                    if (Vector.IsHardwareAccelerated)
                    {
                        for (; i + Vector<float>.Count <= length; i += Vector<float>.Count)
                        {
                            Vector<float> ni = Unsafe.ReadUnaligned<Vector<float>>(n + i);
                            Vector<float> pi = Unsafe.ReadUnaligned<Vector<float>>(p + i);

                            Vector<float> result = ni + pi;
                            Unsafe.WriteUnaligned(n + i, result);
                        }
                    }
                    for (; i < length; i++)
                    {
                        n[i] += p[i];
                    }
                }
            }
        }

        #endregion

        #region Seeking

        /// <summary>
        /// Seeks the stream by the specified duration.
        /// </summary>
        /// <param name="timePosition">The relative time to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        /// <inheritdoc cref="SeekTo(long, SeekOrigin)"/>
        public void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            SeekTo((long)(SampleRate * timePosition.TotalSeconds), seekOrigin);
        }

        /// <summary>
        /// Seeks the stream by the specified sample count.
        /// </summary>
        /// <param name="samplePosition">The relative sample position to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        /// <exception cref="PreRollPacketException">
        /// Could not read pre-roll packet. Try seeking again prior to reading more samples.
        /// </exception>
        /// <exception cref="SeekOutOfRangeException">The requested seek position extends beyond the stream.</exception>
        public void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            if (_packetProvider == null)
                throw new ObjectDisposedException(nameof(StreamDecoder));

            if (!_packetProvider.CanSeek)
                throw new InvalidOperationException("Seek is not supported by the underlying packet provider.");

            switch (seekOrigin)
            {
                case SeekOrigin.Begin:
                    // no-op
                    break;

                case SeekOrigin.Current:
                    samplePosition = SamplePosition - samplePosition;
                    break;

                case SeekOrigin.End:
                    samplePosition = TotalSamples - samplePosition;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(seekOrigin));
            }

            if (samplePosition < 0)
                throw new ArgumentOutOfRangeException(nameof(samplePosition));

            // seek the stream to the correct position
            long pos = _packetProvider.SeekTo(samplePosition, 1, this);
            int rollForward = (int)(samplePosition - pos);

            // clear out old data
            ResetDecoder();
            _hasPosition = true;

            // read the pre-roll packet
            if (!ReadNextPacket(0, out _))
            {
                // we'll use this to force ReadSamples to fail to read
                _eosFound = true;
                long maxGranuleCount = _packetProvider.GetGranuleCount(this);
                if (samplePosition > maxGranuleCount)
                {
                    throw new SeekOutOfRangeException();
                }
                _prevPacketStart = _prevPacketStop;
                _currentPosition = samplePosition;
                return;
            }

            // read the actual packet
            if (!ReadNextPacket(0, out _))
            {
                ResetDecoder();
                // we'll use this to force ReadSamples to fail to read
                _eosFound = true;
                throw new PreRollPacketException();
            }

            // adjust our indexes to match what we want
            _prevPacketStart += rollForward;
            _currentPosition = samplePosition;
        }

        int IPacketGranuleCountProvider.GetPacketGranuleCount(ref VorbisPacket curPacket)
        {
            try
            {
                // if it's a resync, there's not any audio data to return
                if (curPacket.IsResync)
                    return 0;

                // if it's not an audio packet, there's no audio data (seems obvious, though...)
                if (curPacket.ReadBit())
                    return 0;

                // OK, let's ask the appropriate mode how long this packet actually is

                // first we need to know which mode...
                uint modeIdx = (uint)curPacket.ReadBits(_modeFieldBits);

                // if we got an invalid mode value, we can't decode any audio data anyway...
                if (modeIdx >= (uint)_modes.Length)
                    return 0;

                return _modes[modeIdx].GetPacketSampleCount(ref curPacket);
            }
            finally
            {
                curPacket.Finish();
            }
        }

        #endregion

        /// <summary>
        /// Cleans up this instance.
        /// </summary>
        public void Dispose()
        {
            (_packetProvider as IDisposable)?.Dispose();
            _packetProvider = null!;

            _nextPacketBuf = null;
            _prevPacketBuf = null;
        }

        #region Properties

        /// <inheritdoc />
        public int Channels => _channels;

        /// <inheritdoc />
        public int SampleRate => _sampleRate;

        /// <inheritdoc />
        public int UpperBitrate { get; private set; }

        /// <summary>
        /// Gets the nominal bitrate of the stream, if specified. 
        /// May be calculated from <see cref="LowerBitrate"/> and <see cref="UpperBitrate"/>.
        /// </summary>
        public int NominalBitrate { get; private set; }

        /// <inheritdoc />
        public int LowerBitrate { get; private set; }

        /// <inheritdoc />
        public ITagData Tags => _tags ??= new TagData(_utf8Vendor, _utf8Comments);

        /// <inheritdoc />
        public TimeSpan TotalTime => TimeSpan.FromSeconds((double)TotalSamples / _sampleRate);

        /// <inheritdoc />
        public long TotalSamples
        {
            get
            {
                if (_packetProvider == null)
                    throw new ObjectDisposedException(nameof(StreamDecoder));
                return _packetProvider.GetGranuleCount(this);
            }
        }

        /// <inheritdoc />
        public TimeSpan TimePosition
        {
            get => TimeSpan.FromSeconds((double)_currentPosition / _sampleRate);
            set => SeekTo(value);
        }

        /// <inheritdoc />
        public long SamplePosition
        {
            get => _currentPosition;
            set => SeekTo(value);
        }

        /// <summary>
        /// Gets or sets whether to clip samples returned by <see cref="Read(Span{float})"/>.
        /// </summary>
        public bool ClipSamples { get; set; }

        /// <inheritdoc />
        public bool SkipTags { get; set; }

        /// <summary>
        /// Gets whether <see cref="Read(Span{float})"/> has returned any clipped samples.
        /// </summary>
        public bool HasClipped => _hasClipped;

        /// <inheritdoc />
        public bool IsEndOfStream => _eosFound && _prevPacketBuf == null;

        /// <inheritdoc />
        public IStreamStats Stats => _stats;

        #endregion
    }
}
