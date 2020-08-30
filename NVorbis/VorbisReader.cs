using System;
using System.Collections.Generic;
using System.IO;
using NVorbis.Contracts;

namespace NVorbis
{
    /// <summary>
    /// Implements an easy to use wrapper around <see cref="IContainerReader"/> and <see cref="IStreamDecoder"/>.
    /// </summary>
    public sealed class VorbisReader : IVorbisReader
    {
        internal static Func<Stream, bool, IContainerReader> CreateContainerReader { get; set; } =
            (s, lo) => new Ogg.ContainerReader(s, lo);

        internal static Func<IPacketProvider, IStreamDecoder> CreateStreamDecoder { get; set; } =
            pp => new StreamDecoder(pp, new Factory());

        private readonly List<IStreamDecoder> _decoders;
        private readonly IContainerReader _containerReader;
        private readonly bool _leaveOpen;

        private IStreamDecoder _streamDecoder;

        /// <summary>
        /// Raised when a new stream has been encountered in the file or container.
        /// </summary>
        public event NewLogicalStreamHandler? NewStream;

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified file.
        /// </summary>
        /// <param name="fileName">The file to read from.</param>
        public VorbisReader(string fileName)
            : this(File.OpenRead(fileName), leaveOpen: false)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified stream,
        /// optionally taking ownership of it.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read from.</param>
        /// <param name="leaveOpen"><c>false</c> to close the stream when disposed, otherwise <c>true</c>.</param>
        public VorbisReader(Stream stream, bool leaveOpen = false)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _decoders = new List<IStreamDecoder>();

            var containerReader = CreateContainerReader(stream, leaveOpen);
            containerReader.NewStreamCallback = ProcessNewStream;

            if (!containerReader.TryInit() || _decoders.Count == 0)
            {
                containerReader.NewStreamCallback = null;
                containerReader.Dispose();

                if (!leaveOpen)
                    stream.Dispose();

                throw new InvalidDataException("Could not load the specified container.");
            }

            _leaveOpen = leaveOpen;
            _containerReader = containerReader;
            _streamDecoder = _decoders[0];
        }

        private bool ProcessNewStream(IPacketProvider packetProvider)
        {
            var decoder = CreateStreamDecoder(packetProvider);
            decoder.ClipSamples = true;

            var ea = new NewStreamEventArgs(decoder);
            NewStream?.Invoke(this, ref ea);
            if (!ea.IgnoreStream)
            {
                _decoders.Add(decoder);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cleans up this instance.
        /// </summary>
        public void Dispose()
        {
            if (_decoders != null)
            {
                foreach (var decoder in _decoders)
                    decoder.Dispose();
                _decoders.Clear();
            }

            if (_containerReader != null)
            {
                _containerReader.NewStreamCallback = null;
                if (!_leaveOpen)
                    _containerReader.Dispose();
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<IStreamDecoder> Streams => _decoders;

        #region Convenience Helpers

        // Since most uses of VorbisReader are for single-stream audio files,
        // we can make life simpler for users by exposing the first stream's properties and methods here.

        /// <inheritdoc/>
        public int Channels => _streamDecoder.Channels;

        /// <inheritdoc/>
        public int SampleRate => _streamDecoder.SampleRate;

        /// <inheritdoc/>
        public int UpperBitrate => _streamDecoder.UpperBitrate;

        /// <inheritdoc/>
        public int NominalBitrate => _streamDecoder.NominalBitrate;

        /// <inheritdoc/>
        public int LowerBitrate => _streamDecoder.LowerBitrate;

        /// <inheritdoc/>
        public ITagData Tags => _streamDecoder.Tags;

        /// <inheritdoc/>
        public long ContainerOverheadBits => _containerReader?.ContainerBits ?? 0;

        /// <inheritdoc/>
        public long ContainerWasteBits => _containerReader?.WasteBits ?? 0;

        /// <inheritdoc/>
        public int StreamIndex => _decoders.IndexOf(_streamDecoder);

        /// <inheritdoc/>
        public TimeSpan TotalTime => _streamDecoder.TotalTime;

        /// <inheritdoc/>
        public long TotalSamples => _streamDecoder.TotalSamples;

        /// <inheritdoc/>
        public TimeSpan TimePosition
        {
            get => _streamDecoder.TimePosition;
            set => _streamDecoder.TimePosition = value;
        }

        /// <inheritdoc/>
        public long SamplePosition
        {
            get => _streamDecoder.SamplePosition;
            set => _streamDecoder.SamplePosition = value;
        }

        /// <inheritdoc/>
        public bool IsEndOfStream => _streamDecoder.IsEndOfStream;

        /// <inheritdoc/>
        public bool ClipSamples
        {
            get => _streamDecoder.ClipSamples;
            set => _streamDecoder.ClipSamples = value;
        }

        /// <inheritdoc/>
        public bool HasClipped => _streamDecoder.HasClipped;

        /// <inheritdoc/>
        public IStreamStats StreamStats => _streamDecoder.Stats;

        /// <inheritdoc/>
        public bool FindNextStream()
        {
            if (_containerReader == null)
                return false;
            return _containerReader.FindNextStream();
        }

        /// <inheritdoc/>
        public bool SwitchStreams(int index)
        {
            if (index < 0 || index >= _decoders.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var newDecoder = _decoders[index];
            var oldDecoder = _streamDecoder;
            if (newDecoder == oldDecoder)
                return false;

            // carry-through the clipping setting
            newDecoder.ClipSamples = oldDecoder.ClipSamples;

            _streamDecoder = newDecoder;

            return newDecoder.Channels != oldDecoder.Channels || newDecoder.SampleRate != oldDecoder.SampleRate;
        }

        /// <inheritdoc/>
        public void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            _streamDecoder.SeekTo(timePosition, seekOrigin);
        }

        /// <inheritdoc/>
        public void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            _streamDecoder.SeekTo(samplePosition, seekOrigin);
        }

        /// <inheritdoc/>
        public int ReadSamples(Span<float> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            // don't allow non-aligned reads (always on a full sample boundary!)
            int count = buffer.Length;
            count -= count % _streamDecoder.Channels;
            buffer = buffer.Slice(0, count);

            return _streamDecoder.Read(buffer);
        }

        #endregion
    }
}
