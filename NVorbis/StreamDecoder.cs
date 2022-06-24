using NVorbis.Contracts;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using static System.Runtime.CompilerServices.Unsafe;

namespace NVorbis
{
    /// <summary>
    /// Implements a stream decoder for Vorbis data.
    /// </summary>
    public sealed class StreamDecoder : IStreamDecoder
    {
        private IPacketProvider _packetProvider;
        private StreamStats _stats;

        private byte _channels;
        private int _sampleRate;
        private int _block0Size;
        private int _block1Size;
        private Mode[] _modes;
        private int _modeFieldBits;

        private string _vendor;
        private string[] _comments;
        private ITagData _tags;

        private long _currentPosition;
        private bool _hasClipped;
        private bool _hasPosition;
        private bool _eosFound;

        private float[][] _nextPacketBuf;
        private float[][] _prevPacketBuf;
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

            _currentPosition = 0L;
            ClipSamples = true;

            DataPacket packet = _packetProvider.PeekNextPacket();
            if (!ProcessHeaderPackets(packet))
            {
                _packetProvider = null;
                packet.Reset();

                throw GetInvalidStreamException(packet);
            }
        }

        private static Exception GetInvalidStreamException(DataPacket packet)
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
                packet.Reset();
            }
        }

        #region Init

        private bool ProcessHeaderPackets(DataPacket packet)
        {
            if (!ProcessHeaderPacket(
                packet,
                static (p, s) => s.LoadStreamHeader(p),
                static (_, s) => s._packetProvider.GetNextPacket().Done()))
            {
                return false;
            }

            if (!ProcessHeaderPacket(
                _packetProvider.GetNextPacket(),
                static (p, s) => s.LoadComments(p),
                static (p, _) => p.Done()))
            {
                return false;
            }

            if (!ProcessHeaderPacket(
                _packetProvider.GetNextPacket(),
                static (p, s) => s.LoadBooks(p),
                static (p, _) => p.Done()))
            {
                return false;
            }

            _currentPosition = 0;
            ResetDecoder();
            return true;
        }

        private bool ProcessHeaderPacket(
            DataPacket packet,
            Func<DataPacket, StreamDecoder, bool> processAction,
            Action<DataPacket, StreamDecoder> doneAction)
        {
            if (packet != null)
            {
                try
                {
                    return processAction(packet, this);
                }
                finally
                {
                    doneAction(packet, this);
                }
            }
            return false;
        }

        static private ReadOnlySpan<byte> PacketSignatureStream => new byte[] { 0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73, 0x00, 0x00, 0x00, 0x00 };
        static private ReadOnlySpan<byte> PacketSignatureComments => new byte[] { 0x03, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };
        static private ReadOnlySpan<byte> PacketSignatureBooks => new byte[] { 0x05, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };

        static private bool ValidateHeader(DataPacket packet, ReadOnlySpan<byte> expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != packet.ReadBits(8))
                {
                    return false;
                }
            }
            return true;
        }

        static private string ReadString(DataPacket packet)
        {
            int len = (int)packet.ReadBits(32);

            if (len == 0)
            {
                return string.Empty;
            }

            byte[] buf = new byte[len];
            int cnt = packet.Read(buf.AsSpan(0, len));
            if (cnt < len)
            {
                throw new InvalidDataException("Could not read full string!");
            }
            return Encoding.UTF8.GetString(buf);
        }

        private bool LoadStreamHeader(DataPacket packet)
        {
            if (!ValidateHeader(packet, PacketSignatureStream))
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

        private bool LoadComments(DataPacket packet)
        {
            if (!ValidateHeader(packet, PacketSignatureComments))
            {
                return false;
            }

            _vendor = ReadString(packet);

            _comments = new string[packet.ReadBits(32)];
            for (int i = 0; i < _comments.Length; i++)
            {
                _comments[i] = ReadString(packet);
            }

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private bool LoadBooks(DataPacket packet)
        {
            if (!ValidateHeader(packet, PacketSignatureBooks))
            {
                return false;
            }

            // read the books
            Codebook[] books = new Codebook[packet.ReadBits(8) + 1];
            for (int i = 0; i < books.Length; i++)
            {
                books[i] = new Codebook(packet);
            }

            // Vorbis never used this feature, so we just skip the appropriate number of bits
            int times = (int)packet.ReadBits(6) + 1;
            packet.SkipBits(16 * times);

            // read the floors
            IFloor[] floors = new IFloor[packet.ReadBits(6) + 1];
            for (int i = 0; i < floors.Length; i++)
            {
                floors[i] = CreateFloor(packet, _block0Size, _block1Size, books);
            }

            // read the residues
            Residue0[] residues = new Residue0[packet.ReadBits(6) + 1];
            for (int i = 0; i < residues.Length; i++)
            {
                residues[i] = CreateResidue(packet, _channels, books);
            }

            // read the mappings
            Mapping[] mappings = new Mapping[packet.ReadBits(6) + 1];
            for (int i = 0; i < mappings.Length; i++)
            {
                mappings[i] = CreateMapping(packet, _channels, floors, residues);
            }

            // read the modes
            _modes = new Mode[packet.ReadBits(6) + 1];
            for (int i = 0; i < _modes.Length; i++)
            {
                _modes[i] = new Mode(packet, _channels, _block0Size, _block1Size, mappings);
            }

            // verify the closing bit
            if (!packet.ReadBit()) throw new InvalidDataException("Book packet did not end on correct bit!");

            // save off the number of bits to read to determine packet mode
            _modeFieldBits = Utils.ilog(_modes.Length - 1);

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private static IFloor CreateFloor(DataPacket packet, int block0Size, int block1Size, Codebook[] codebooks)
        {
            int type = (int)packet.ReadBits(16);
            switch (type)
            {
                case 0: return new Floor0(packet, block0Size, block1Size, codebooks);
                case 1: return new Floor1(packet, codebooks);
                default: throw new InvalidDataException("Invalid floor type!");
            }
        }

        private static Mapping CreateMapping(DataPacket packet, int channels, IFloor[] floors, Residue0[] residues)
        {
            if (packet.ReadBits(16) != 0)
            {
                throw new InvalidDataException("Invalid mapping type!");
            }
            return new Mapping(packet, channels, floors, residues);
        }

        private static Residue0 CreateResidue(DataPacket packet, int channels, Codebook[] codebooks)
        {
            int type = (int)packet.ReadBits(16);
            switch (type)
            {
                case 0: return new Residue0(packet, channels, codebooks);
                case 1: return new Residue1(packet, channels, codebooks);
                case 2: return new Residue2(packet, channels, codebooks);
                default: throw new InvalidDataException("Invalid residue type!");
            }
        }

        #endregion

        #region State Change

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

        #endregion

        #region Decoding

        /// <inheritdoc/>
        public int Read(Span<float> buffer)
        {
            if (buffer.Length % _channels != 0) throw new ArgumentException("Length must be a multiple of Channels.", nameof(buffer));
            if (_packetProvider == null) throw new ObjectDisposedException(nameof(StreamDecoder));

            // if the caller didn't ask for any data, bail early
            if (buffer.Length == 0)
            {
                return 0;
            }

            // save off value to track when we're done with the request
            nint idx = 0;
            int tgt = buffer.Length;
            ref float target = ref MemoryMarshal.GetReference(buffer);

            // try to fill the buffer; drain the last buffer if EOS, resync, bad packet, or parameter change
            while (idx < tgt)
            {
                // if we don't have any more valid data in the current packet, read in the next packet
                if (_prevPacketStart == _prevPacketEnd)
                {
                    if (_eosFound)
                    {
                        _nextPacketBuf = null;
                        _prevPacketBuf = null;

                        // no more samples, so just return
                        break;
                    }

                    if (!ReadNextPacket(idx / _channels, out long? samplePosition))
                    {
                        // drain the current packet (the windowing will fade it out)
                        _prevPacketEnd = _prevPacketStop;
                    }

                    // if we need to pick up a position, and the packet had one, apply the position now
                    if (samplePosition.HasValue && !_hasPosition)
                    {
                        _hasPosition = true;
                        _currentPosition = samplePosition.Value - (_prevPacketEnd - _prevPacketStart) - idx / _channels;
                    }
                }

                // we read out the valid samples from the previous packet
                nint copyLen = Math.Min((tgt - idx) / _channels, _prevPacketEnd - _prevPacketStart);
                if (copyLen > 0)
                {
                    if (ClipSamples)
                    {
                        idx += ClippingCopyBuffer(ref Add(ref target, idx), copyLen);
                    }
                    else
                    {
                        idx += CopyBuffer(ref Add(ref target, idx), copyLen);
                    }
                }
            }

            // update the position
            _currentPosition += idx / _channels;

            // return count of floats written
            return (int)idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private nint ClippingCopyBuffer(ref float target, nint count)
        {
            float[][] prevPacketBuf = _prevPacketBuf;
            nint channels = _channels;
            nint j = 0;

            if (Sse.IsSupported)
            {
                Vector128<float> clipped = Vector128<float>.Zero;

                if (_channels == 2)
                {
                    ref float prev0 = ref prevPacketBuf[0][_prevPacketStart];
                    ref float prev1 = ref prevPacketBuf[1][_prevPacketStart];

                    for (; j + 4 <= count; j += 4)
                    {
                        Vector128<float> p0 = As<float, Vector128<float>>(ref Add(ref prev0, j));
                        Vector128<float> p1 = As<float, Vector128<float>>(ref Add(ref prev1, j));

                        Vector128<float> t0 = Sse.Shuffle(p0, p1, 0b01_00_01_00);
                        Vector128<float> ts0 = Sse.Shuffle(t0, t0, 0b11_01_10_00);

                        Vector128<float> t1 = Sse.Shuffle(p0, p1, 0b11_10_11_10);
                        Vector128<float> ts1 = Sse.Shuffle(t1, t1, 0b11_01_10_00);

                        ts0 = Utils.ClipValue(ts0, ref clipped);
                        ts1 = Utils.ClipValue(ts1, ref clipped);

                        As<float, Vector128<float>>(ref Add(ref target, j * 2 + 0)) = ts0;
                        As<float, Vector128<float>>(ref Add(ref target, j * 2 + 4)) = ts1;
                    }
                }
                else if (_channels == 1)
                {
                    ref float prev0 = ref prevPacketBuf[0][_prevPacketStart];

                    for (; j + 4 <= count; j += 4)
                    {
                        Vector128<float> p0 = As<float, Vector128<float>>(ref Add(ref prev0, j));
                        p0 = Utils.ClipValue(p0, ref clipped);
                        As<float, Vector128<float>>(ref Add(ref target, j)) = p0;
                    }
                }

                Vector128<float> mask = Sse.CompareEqual(clipped, Vector128<float>.Zero);
                _hasClipped |= Sse.MoveMask(mask) != 0xf;
            }

            for (nint ch = 0; ch < channels; ch++)
            {
                ref float tar = ref Add(ref target, ch);
                ref float prev = ref prevPacketBuf[ch][_prevPacketStart];

                for (nint i = j; i < count; i++)
                {
                    Add(ref tar, i * channels) = Utils.ClipValue(Add(ref prev, i), ref _hasClipped);
                }
            }

            _prevPacketStart += (int)count;

            return count * channels;
        }

        private nint CopyBuffer(ref float target, nint count)
        {
            float[][] prevPacketBuf = _prevPacketBuf;
            nint channels = _channels;
            nint j = 0;

            for (nint ch = 0; ch < channels; ch++)
            {
                ref float tar = ref Add(ref target, ch);
                ref float prev = ref prevPacketBuf[ch][_prevPacketStart];

                for (nint i = j; i < count; i++)
                {
                    Add(ref tar, i * channels) = Add(ref prev, i);
                }
            }

            _prevPacketStart += (int)count;

            return count * channels;
        }

        private bool ReadNextPacket(nint bufferedSamples, out long? samplePosition)
        {
            // decode the next packet now so we can start overlapping with it
            float[][] curPacket = DecodeNextPacket(out int startIndex, out int validLen, out int totalLen, out bool isEndOfStream, out samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits);
            _eosFound |= isEndOfStream;
            if (curPacket == null)
            {
                _stats.AddPacket(0, bitsRead, bitsRemaining, containerOverheadBits);
                return false;
            }

            // if we get a max sample position, back off our valid length to match
            if (samplePosition.HasValue && isEndOfStream)
            {
                long actualEnd = _currentPosition + bufferedSamples + validLen - startIndex;
                int diff = (int)(samplePosition.Value - actualEnd);
                if (diff < 0)
                {
                    validLen += diff;
                }
            }

            // start overlapping (if we don't have an previous packet data, just loop and the previous packet logic will handle things appropriately)
            if (_prevPacketEnd > 0)
            {
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

        private float[][] DecodeNextPacket(out int packetStartindex, out int packetValidLength, out int packetTotalLength, out bool isEndOfStream, out long? samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits)
        {
            DataPacket packet = null;
            try
            {
                if ((packet = _packetProvider.GetNextPacket()) == null)
                {
                    // no packet? we're at the end of the stream
                    isEndOfStream = true;
                }
                else
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
                    if (packet.ReadBit())
                    {
                        bitsRemaining = packet.BitsRemaining + 1;
                    }
                    else
                    {
                        // if we get here, we should have a good packet; decode it and add it to the buffer
                        Mode mode = _modes[(int)packet.ReadBits(_modeFieldBits)];
                        if (_nextPacketBuf == null)
                        {
                            _nextPacketBuf = new float[_channels][];
                            for (int i = 0; i < _channels; i++)
                            {
                                _nextPacketBuf[i] = new float[_block1Size];
                            }
                        }
                        if (mode.Decode(packet, _nextPacketBuf, out packetStartindex, out packetValidLength, out packetTotalLength))
                        {
                            // per the spec, do not decode more samples than the last granulePosition
                            samplePosition = packet.GranulePosition;
                            bitsRead = packet.BitsRead;
                            bitsRemaining = packet.BitsRemaining;
                            return _nextPacketBuf;
                        }
                        bitsRemaining = packet.BitsRead + packet.BitsRemaining;
                    }
                }
                packetStartindex = 0;
                packetValidLength = 0;
                packetTotalLength = 0;
                samplePosition = null;
                bitsRead = 0;
                bitsRemaining = 0;
                containerOverheadBits = 0;
                return null;
            }
            finally
            {
                packet?.Done();
            }
        }

        private static void OverlapBuffers(float[][] previous, float[][] next, int prevStart, int prevLen, int nextStart, int channels)
        {
            nint length = prevLen - prevStart;
            for (int c = 0; c < channels; c++)
            {
                Span<float> prevSpan = previous[c].AsSpan(prevStart, (int)length);
                Span<float> nextSpan = next[c].AsSpan(nextStart, (int)length);

                ref float p = ref MemoryMarshal.GetReference(prevSpan);
                ref float n = ref MemoryMarshal.GetReference(nextSpan);

                nint i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    for (; i + Vector<float>.Count <= length; i += Vector<float>.Count)
                    {
                        ref float ni = ref Add(ref n, i);
                        ref float pi = ref Add(ref p, i);
                        As<float, Vector<float>>(ref ni) += As<float, Vector<float>>(ref pi);
                    }
                }
                for (; i < length; i++)
                {
                    Add(ref n, i) += Add(ref p, i);
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
        public void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            SeekTo((long)(SampleRate * timePosition.TotalSeconds), seekOrigin);
        }

        /// <summary>
        /// Seeks the stream by the specified sample count.
        /// </summary>
        /// <param name="samplePosition">The relative sample position to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        public void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            if (_packetProvider == null) throw new ObjectDisposedException(nameof(StreamDecoder));
            if (!_packetProvider.CanSeek) throw new InvalidOperationException("Seek is not supported by the Contracts.IPacketProvider instance.");

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

            if (samplePosition < 0) throw new ArgumentOutOfRangeException(nameof(samplePosition));

            int rollForward;
            if (samplePosition == 0)
            {
                // short circuit for the looping case...
                _packetProvider.SeekTo(0, 0, GetPacketGranules);
                rollForward = 0;
            }
            else
            {
                // seek the stream to the correct position
                long pos = _packetProvider.SeekTo(samplePosition, 1, GetPacketGranules);
                rollForward = (int)(samplePosition - pos);
            }

            // clear out old data
            ResetDecoder();
            _hasPosition = true;

            // read the pre-roll packet
            if (!ReadNextPacket(0, out _))
            {
                // we'll use this to force ReadSamples to fail to read
                _eosFound = true;
                if (_packetProvider.GetGranuleCount() != samplePosition)
                {
                    throw new InvalidOperationException("Could not read pre-roll packet!  Try seeking again prior to reading more samples.");
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
                throw new InvalidOperationException("Could not read pre-roll packet!  Try seeking again prior to reading more samples.");
            }

            // adjust our indexes to match what we want
            _prevPacketStart += rollForward;
            _currentPosition = samplePosition;
        }

        private int GetPacketGranules(DataPacket curPacket, bool isLastInPage)
        {
            // if it's a resync, there's not any audio data to return
            if (curPacket.IsResync) return 0;

            // if it's not an audio packet, there's no audio data (seems obvious, though...)
            if (curPacket.ReadBit()) return 0;

            // OK, let's ask the appropriate mode how long this packet actually is

            // first we need to know which mode...
            int modeIdx = (int)curPacket.ReadBits(_modeFieldBits);

            // if we got an invalid mode value, we can't decode any audio data anyway...
            if (modeIdx < 0 || modeIdx >= _modes.Length) return 0;

            return _modes[modeIdx].GetPacketSampleCount(curPacket, isLastInPage);
        }

        #endregion

        /// <summary>
        /// Cleans up this instance.
        /// </summary>
        public void Dispose()
        {
            (_packetProvider as IDisposable)?.Dispose();
            _packetProvider = null;
        }

        #region Properties

        /// <summary>
        /// Gets the number of channels in the stream.
        /// </summary>
        public int Channels => _channels;

        /// <summary>
        /// Gets the sample rate of the stream.
        /// </summary>
        public int SampleRate => _sampleRate;

        /// <summary>
        /// Gets the upper bitrate limit for the stream, if specified.
        /// </summary>
        public int UpperBitrate { get; private set; }

        /// <summary>
        /// Gets the nominal bitrate of the stream, if specified.  May be calculated from <see cref="LowerBitrate"/> and <see cref="UpperBitrate"/>.
        /// </summary>
        public int NominalBitrate { get; private set; }

        /// <summary>
        /// Gets the lower bitrate limit for the stream, if specified.
        /// </summary>
        public int LowerBitrate { get; private set; }

        /// <summary>
        /// Gets the tag data from the stream's header.
        /// </summary>
        public ITagData Tags => _tags ?? (_tags = new TagData(_vendor, _comments));

        /// <summary>
        /// Gets the total duration of the decoded stream.
        /// </summary>
        public TimeSpan TotalTime => TimeSpan.FromSeconds((double)TotalSamples / _sampleRate);

        /// <summary>
        /// Gets the total number of samples in the decoded stream.
        /// </summary>
        public long TotalSamples => _packetProvider?.GetGranuleCount() ?? throw new ObjectDisposedException(nameof(StreamDecoder));

        /// <summary>
        /// Gets or sets the current time position of the stream.
        /// </summary>
        public TimeSpan TimePosition
        {
            get => TimeSpan.FromSeconds((double)_currentPosition / _sampleRate);
            set => SeekTo(value);
        }

        /// <summary>
        /// Gets or sets the current sample position of the stream.
        /// </summary>
        public long SamplePosition
        {
            get => _currentPosition;
            set => SeekTo(value);
        }

        /// <summary>
        /// Gets or sets whether to clip samples returned by <see cref="Read(Span{float})"/>.
        /// </summary>
        public bool ClipSamples { get; set; }

        /// <summary>
        /// Gets whether <see cref="Read(Span{float})"/> has returned any clipped samples.
        /// </summary>
        public bool HasClipped => _hasClipped;

        /// <summary>
        /// Gets whether the decoder has reached the end of the stream.
        /// </summary>
        public bool IsEndOfStream => _eosFound && _prevPacketBuf == null;

        /// <summary>
        /// Gets the <see cref="IStreamStats"/> instance for this stream.
        /// </summary>
        public IStreamStats Stats => _stats;

        #endregion
    }
}
