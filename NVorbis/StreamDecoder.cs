using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NVorbis.Contracts;

namespace NVorbis
{
    /// <summary>
    /// Implements a stream decoder for Vorbis data.
    /// </summary>
    public sealed class StreamDecoder : IStreamDecoder
    {
        private static readonly byte[] PacketSignatureStream = {
            0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73, 0x00, 0x00, 0x00, 0x00
        };

        private static readonly byte[] PacketSignatureComments = { 0x03, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };
        private static readonly byte[] PacketSignatureBooks = { 0x05, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };

        internal static Func<IFactory> CreateFactory { get; set; } = () => new Factory();

        private IPacketProvider _packetProvider;
        private IFactory _factory;
        private StreamStats _stats;

        private byte _channels;
        private int _sampleRate;
        private int _block0Size;
        private int _block1Size;
        private IMode[] _modes;
        private int _modeFieldBits;

        private string _vendor;
        private string[] _comments;
        private ITagData _tags;

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
        /// <param name="packetProvider">
        /// A <see cref="IPacketProvider"/> instance for the decoder to read from.
        /// </param>
        public StreamDecoder(IPacketProvider packetProvider)
            : this(packetProvider, new Factory())
        {
        }

        internal StreamDecoder(IPacketProvider packetProvider, IFactory factory)
        {
            _packetProvider = packetProvider ?? throw new ArgumentNullException(nameof(packetProvider));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            _stats = new StreamStats();

            _currentPosition = 0L;
            ClipSamples = true;

            var packet = _packetProvider.PeekNextPacket();
            if (!ProcessHeaderPackets(packet))
            {
                _packetProvider = null!;
                packet?.Reset();

                throw GetInvalidStreamException(packet);
            }
        }

        private static Exception GetInvalidStreamException(IPacket packet)
        {
            try
            {
                // let's give our caller some helpful hints about what they've encountered...
                var header = packet.ReadBits(64);
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

        private bool ProcessHeaderPackets(IPacket? packet)
        {
            if (!ProcessHeaderPacket(packet, LoadStreamHeader, _ => _packetProvider.GetNextPacket()?.Done()))
                return false;

            if (!ProcessHeaderPacket(_packetProvider.GetNextPacket(), LoadComments, pkt => pkt.Done()))
                return false;

            if (!ProcessHeaderPacket(_packetProvider.GetNextPacket(), LoadBooks, pkt => pkt.Done()))
                return false;

            _currentPosition = 0;
            ResetDecoder();
            return true;
        }

        private static bool ProcessHeaderPacket(
            IPacket? packet, Func<IPacket, bool> processAction, Action<IPacket> doneAction)
        {
            if (packet != null)
            {
                try
                {
                    return processAction(packet);
                }
                finally
                {
                    doneAction(packet);
                }
            }
            return false;
        }

        private static bool ValidateHeader(IPacket packet, byte[] expected)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                if (expected[i] != packet.ReadBits(8))
                    return false;
            }
            return true;
        }

        private static string ReadString(IPacket packet)
        {
            int length = (int)packet.ReadBits(32);
            var buf = new byte[length];
            int count = packet.Read(buf.AsSpan(0, length));
            if (count < length)
                throw new InvalidDataException("Could not read full string.");

            return Encoding.UTF8.GetString(buf);
        }

        private bool LoadStreamHeader(IPacket packet)
        {
            if (!ValidateHeader(packet, PacketSignatureStream))
                return false;

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

        private bool LoadComments(IPacket packet)
        {
            if (!ValidateHeader(packet, PacketSignatureComments))
            {
                return false;
            }

            _vendor = ReadString(packet);

            _comments = new string[packet.ReadBits(32)];
            for (var i = 0; i < _comments.Length; i++)
            {
                _comments[i] = ReadString(packet);
            }

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private bool LoadBooks(IPacket packet)
        {
            if (!ValidateHeader(packet, PacketSignatureBooks))
            {
                return false;
            }

            var mdct = _factory.CreateMdct();
            var huffman = _factory.CreateHuffman();

            // read the books
            var books = new ICodebook[packet.ReadBits(8) + 1];
            for (var i = 0; i < books.Length; i++)
            {
                books[i] = _factory.CreateCodebook();
                books[i].Init(packet, huffman);
            }

            // Vorbis never used this feature, so we just skip the appropriate number of bits
            var times = (int)packet.ReadBits(6) + 1;
            packet.SkipBits(16 * times);

            // read the floors
            var floors = new IFloor[packet.ReadBits(6) + 1];
            for (var i = 0; i < floors.Length; i++)
            {
                floors[i] = _factory.CreateFloor(packet);
                floors[i].Init(packet, _channels, _block0Size, _block1Size, books);
            }

            // read the residues
            var residues = new IResidue[packet.ReadBits(6) + 1];
            for (var i = 0; i < floors.Length; i++)
            {
                residues[i] = _factory.CreateResidue(packet);
                residues[i].Init(packet, _channels, books);
            }

            // read the mappings
            var mappings = new IMapping[packet.ReadBits(6) + 1];
            for (var i = 0; i < mappings.Length; i++)
            {
                mappings[i] = _factory.CreateMapping(packet);
                mappings[i].Init(packet, _channels, floors, residues, mdct);
            }

            // read the modes
            _modes = new IMode[packet.ReadBits(6) + 1];
            for (var i = 0; i < _modes.Length; i++)
            {
                _modes[i] = _factory.CreateMode();
                _modes[i].Init(packet, _channels, _block0Size, _block1Size, mappings);
            }

            // verify the closing bit
            if (!packet.ReadBit())
                throw new InvalidDataException("Book packet did not end on correct bit.");

            // save off the number of bits to read to determine packet mode
            _modeFieldBits = Utils.ILog(_modes.Length - 1);

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
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
            if (_packetProvider == null)
                throw new ObjectDisposedException(nameof(StreamDecoder));

            int count = buffer.Length;
            if (count % _channels != 0)
                throw new ArgumentException("Length must be a multiple of Channels.", nameof(buffer));

            // if the caller didn't ask for any data, bail early
            if (count == 0)
                return 0;

            int offset = 0;

            // try to fill the buffer; drain the last buffer if EOS, resync, bad packet, or parameter change
            do
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

                    if (!ReadNextPacket(offset / _channels, out long? samplePosition))
                    {
                        // drain the current packet (the windowing will fade it out)
                        _prevPacketEnd = _prevPacketStop;
                    }

                    // if we need to pick up a position, and the packet had one, apply the position now
                    if (samplePosition.HasValue && !_hasPosition)
                    {
                        _hasPosition = true;

                        _currentPosition =
                            samplePosition.GetValueOrDefault() -
                            (_prevPacketEnd - _prevPacketStart) - offset / _channels;
                    }
                }

                // we read out the valid samples from the previous packet
                int copyLen = Math.Min((count - offset) / _channels, _prevPacketEnd - _prevPacketStart);
                if (copyLen > 0)
                {
                    if (ClipSamples)
                        offset += ClippingCopyBuffer(buffer.Slice(offset), copyLen);
                    else
                        offset += CopyBuffer(buffer.Slice(offset), copyLen);
                }
            }
            while (offset < count);

            // update the position
            _currentPosition += offset / _channels;

            // return count of floats written
            return offset;
        }

        private int ClippingCopyBuffer(Span<float> target, int count)
        {
            Debug.Assert(_prevPacketBuf != null);

            int index = 0;
            for (; count > 0; _prevPacketStart++, count--)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    target[index++] = Utils.ClipValue(_prevPacketBuf[ch][_prevPacketStart], ref _hasClipped);
                }
            }
            return index;
        }

        private int CopyBuffer(Span<float> target, int count)
        {
            Debug.Assert(_prevPacketBuf != null);

            int offset = 0;
            for (; count > 0; _prevPacketStart++, count--)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    target[offset++] = _prevPacketBuf[ch][_prevPacketStart];
                }
            }
            return offset;
        }

        private bool ReadNextPacket(int bufferedSamples, out long? samplePosition)
        {
            // decode the next packet now so we can start overlapping with it
            var curPacket = DecodeNextPacket(
                out int startIndex, out int validLen, out int totalLen, out bool isEndOfStream,
                out samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits);

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
                    validLen += diff;
            }

            // start overlapping 
            // (if we don't have an previous packet data, 
            // just loop and the previous packet logic will handle things appropriately)
            if (_prevPacketEnd > 0)
            {
                // overlap the first samples in the packet with the previous packet, then loop
                OverlapBuffers(
                    _prevPacketBuf, curPacket, _prevPacketStart, _prevPacketStop, startIndex, _channels);

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
            out int packetStartindex, out int packetValidLength, out int packetTotalLength, out bool isEndOfStream,
            out long? samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits)
        {
            IPacket? packet = null;
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
                        _hasPosition = false;

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
                        var mode = _modes[(int)packet.ReadBits(_modeFieldBits)];
                        if (_nextPacketBuf == null)
                        {
                            _nextPacketBuf = new float[_channels][];
                            for (var i = 0; i < _channels; i++)
                                _nextPacketBuf[i] = new float[_block1Size];
                        }

                        if (mode.Decode(
                            packet, _nextPacketBuf,
                            out packetStartindex, out packetValidLength, out packetTotalLength))
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

        private static void OverlapBuffers(
            float[][] previous, float[][] next, int prevStart, int prevLen, int nextStart, int channels)
        {
            for (; prevStart < prevLen; prevStart++, nextStart++)
            {
                for (var c = 0; c < channels; c++)
                {
                    next[c][nextStart] += previous[c][prevStart];
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
            if (_packetProvider == null)
                throw new ObjectDisposedException(nameof(StreamDecoder));
            if (!_packetProvider.CanSeek)
                throw new InvalidOperationException("The packet provider is not seekable.");

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
                throw new InvalidOperationException(
                    "Could not read pre-roll packet. Try seeking again prior to reading more samples.");
            }

            // read the actual packet
            if (!ReadNextPacket(0, out _))
            {
                ResetDecoder();
                // we'll use this to force ReadSamples to fail to read
                _eosFound = true;
                throw new InvalidOperationException(
                    "Could not read pre-roll packet. Try seeking again prior to reading more samples.");
            }

            // adjust our indexes to match what we want
            _prevPacketStart += rollForward;
            _currentPosition = samplePosition;
        }

        private int GetPacketGranules(IPacket curPacket, bool isFirst)
        {
            // if it's a resync, there's not any audio data to return
            if (curPacket.IsResync)
                return 0;

            // if it's not an audio packet, there's no audio data (seems obvious, though...)
            if (curPacket.ReadBit())
                return 0;

            // OK, let's ask the appropriate mode how long this packet actually is

            // first we need to know which mode...
            int modeIdx = (int)curPacket.ReadBits(_modeFieldBits);

            // if we got an invalid mode value, we can't decode any audio data anyway...
            if (modeIdx < 0 || modeIdx >= _modes.Length)
                return 0;

            return _modes[modeIdx].GetPacketSampleCount(curPacket, isFirst);
        }

        #endregion

        /// <summary>
        /// Cleans up this instance.
        /// </summary>
        public void Dispose()
        {
            (_packetProvider as IDisposable)?.Dispose();
            _packetProvider = null!;
        }

        #region Properties

        /// <inheritdoc/>
        public int Channels => _channels;

        /// <inheritdoc/>
        public int SampleRate => _sampleRate;

        /// <inheritdoc/>
        public int UpperBitrate { get; private set; }

        /// <inheritdoc/>
        public int NominalBitrate { get; private set; }

        /// <inheritdoc/>
        public int LowerBitrate { get; private set; }

        /// <inheritdoc/>
        public ITagData Tags => _tags ??= new TagData(_vendor, _comments);

        /// <inheritdoc/>
        public TimeSpan TotalTime => TimeSpan.FromSeconds((double)TotalSamples / _sampleRate);

        /// <inheritdoc/>
        public long TotalSamples => _packetProvider?.GetGranuleCount() ??
            throw new ObjectDisposedException(nameof(StreamDecoder));

        /// <inheritdoc/>
        public TimeSpan TimePosition
        {
            get => TimeSpan.FromSeconds((double)_currentPosition / _sampleRate);
            set => SeekTo(value);
        }

        /// <inheritdoc/>
        public long SamplePosition
        {
            get => _currentPosition;
            set => SeekTo(value);
        }

        /// <inheritdoc/>
        public bool ClipSamples { get; set; }

        /// <inheritdoc/>
        public bool HasClipped => _hasClipped;

        /// <inheritdoc/>
        public bool IsEndOfStream => _eosFound && _prevPacketBuf == null;

        /// <inheritdoc/>
        public IStreamStats Stats => _stats;

        #endregion
    }
}
