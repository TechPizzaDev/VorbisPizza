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
        private readonly List<IStreamDecoder> _decoders;
        private readonly IContainerReader _containerReader;
        private readonly bool _leaveOpen;
        
        private IStreamDecoder _streamDecoder;

        /// <summary>
        /// Raised when a new stream has been encountered in the file or container.
        /// </summary>
        public event NewStreamEventHandler? NewStream;

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified file.
        /// </summary>
        /// <param name="fileName">The file to read from.</param>
        public VorbisReader(string fileName)
            : this(File.OpenRead(fileName), true)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified stream, optionally taking ownership of it.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read from.</param>
        /// <param name="leaveOpen"><see langword="false"/> to dispose the stream when disposed, otherwise <see langword="true"/>.</param>
        public VorbisReader(Stream stream, bool leaveOpen)
        {
            _decoders = new List<IStreamDecoder>();

            Ogg.ContainerReader containerReader = new(stream, leaveOpen);
            containerReader.NewStreamCallback = ProcessNewStream;

            _leaveOpen = leaveOpen;
            _containerReader = containerReader;

            _streamDecoder = null!;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            if (!_containerReader.TryInit() || _decoders.Count == 0)
            {
                _containerReader.NewStreamCallback = null;
                _containerReader.Dispose();

                throw new InvalidDataException("Could not load the specified container.");
            }
            _streamDecoder = _decoders[0];
        }

        private bool ProcessNewStream(IPacketProvider packetProvider)
        {
            StreamDecoder decoder = new(packetProvider);
            decoder.ClipSamples = true;
            decoder.SkipTags = false;

            NewStreamEventArgs ea = new(decoder);
            NewStream?.Invoke(this, ref ea);

            decoder.Initialize();

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
                foreach (IStreamDecoder decoder in _decoders)
                {
                    decoder.Dispose();
                }
                _decoders.Clear();
            }

            if (_containerReader != null)
            {
                _containerReader.NewStreamCallback = null;
                if (!_leaveOpen)
                {
                    _containerReader.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets the list of <see cref="IStreamDecoder"/> instances associated with the loaded file / container.
        /// </summary>
        public IReadOnlyList<IStreamDecoder> Streams => _decoders;

        #region Convenience Helpers

        // Since most uses of VorbisReader are for single-stream audio files,
        // we can make life simpler for users by exposing the first stream's properties and methods here.

        /// <summary>
        /// Gets the number of channels in the stream.
        /// </summary>
        public int Channels => _streamDecoder.Channels;

        /// <summary>
        /// Gets the sample rate of the stream.
        /// </summary>
        public int SampleRate => _streamDecoder.SampleRate;

        /// <summary>
        /// Gets the upper bitrate limit for the stream, if specified.
        /// </summary>
        public int UpperBitrate => _streamDecoder.UpperBitrate;

        /// <summary>
        /// Gets the nominal bitrate of the stream, if specified. 
        /// May be calculated from <see cref="LowerBitrate"/> and <see cref="UpperBitrate"/>.
        /// </summary>
        public int NominalBitrate => _streamDecoder.NominalBitrate;

        /// <summary>
        /// Gets the lower bitrate limit for the stream, if specified.
        /// </summary>
        public int LowerBitrate => _streamDecoder.LowerBitrate;

        /// <summary>
        /// Gets the tag data from the stream's header.
        /// </summary>
        public ITagData Tags => _streamDecoder.Tags;

        /// <summary>
        /// Gets the number of bits read that are related to framing and transport alone.
        /// </summary>
        public long ContainerOverheadBits => _containerReader?.ContainerBits ?? 0;

        /// <summary>
        /// Gets the number of bits skipped in the container due to framing, ignored streams, or sync loss.
        /// </summary>
        public long ContainerWasteBits => _containerReader?.WasteBits ?? 0;

        /// <summary>
        /// Gets the currently-selected stream's index.
        /// </summary>
        public int StreamIndex => _decoders.IndexOf(_streamDecoder);

        /// <summary>
        /// Gets the total duration of the decoded stream.
        /// </summary>
        public TimeSpan TotalTime => _streamDecoder.TotalTime;

        /// <summary>
        /// Gets the total number of samples in the decoded stream.
        /// </summary>
        public long TotalSamples => _streamDecoder.TotalSamples;

        /// <summary>
        /// Gets or sets the current time position of the stream.
        /// </summary>
        public TimeSpan TimePosition
        {
            get => _streamDecoder.TimePosition;
            set => _streamDecoder.TimePosition = value;
        }

        /// <summary>
        /// Gets or sets the current sample position of the stream.
        /// </summary>
        public long SamplePosition
        {
            get => _streamDecoder.SamplePosition;
            set => _streamDecoder.SamplePosition = value;
        }

        /// <summary>
        /// Gets whether the current stream has ended.
        /// </summary>
        public bool IsEndOfStream => _streamDecoder.IsEndOfStream;

        /// <summary>
        /// Gets or sets whether to clip samples returned by <see cref="ReadSamples(Span{float})"/>.
        /// </summary>
        public bool ClipSamples
        {
            get => _streamDecoder.ClipSamples;
            set => _streamDecoder.ClipSamples = value;
        }

        /// <summary>
        /// Gets whether <see cref="ReadSamples(Span{float})"/> has returned any clipped samples.
        /// </summary>
        public bool HasClipped => _streamDecoder.HasClipped;

        /// <summary>
        /// Gets the <see cref="IStreamStats"/> instance for this stream.
        /// </summary>
        public IStreamStats StreamStats => _streamDecoder.Stats;

        /// <summary>
        /// Gets whether the underlying stream can seek.
        /// </summary>
        public bool CanSeek => _containerReader.CanSeek;

        /// <summary>
        /// Searches for the next stream in a concatenated file.
        /// Will raise <see cref="NewStream"/> for the found stream, 
        /// and will add it to <see cref="Streams"/> if not marked as ignored.
        /// </summary>
        /// <returns><see langword="true"/> if a new stream was found, otherwise <see langword="false"/>.</returns>
        public bool FindNextStream()
        {
            if (_containerReader == null) return false;
            return _containerReader.FindNextStream();
        }

        /// <summary>
        /// Switches to an alternate logical stream.
        /// </summary>
        /// <param name="index">The logical stream index to switch to</param>
        /// <returns>
        /// <see langword="true"/> if the properties of the logical stream differ from 
        /// those of the one previously being decoded. Otherwise, <see langword="false"/>.
        /// </returns>
        public bool SwitchStreams(int index)
        {
            if (index < 0 || index >= _decoders.Count) throw new ArgumentOutOfRangeException(nameof(index));

            IStreamDecoder newDecoder = _decoders[index];
            IStreamDecoder oldDecoder = _streamDecoder;
            if (newDecoder == oldDecoder) return false;

            // carry-through the clipping setting
            newDecoder.ClipSamples = oldDecoder.ClipSamples;

            _streamDecoder = newDecoder;

            return newDecoder.Channels != oldDecoder.Channels || newDecoder.SampleRate != oldDecoder.SampleRate;
        }

        /// <summary>
        /// Seeks the stream by the specified duration.
        /// </summary>
        /// <param name="timePosition">The relative time to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        public void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            _streamDecoder.SeekTo(timePosition, seekOrigin);
        }

        /// <summary>
        /// Seeks the stream by the specified sample count.
        /// </summary>
        /// <param name="samplePosition">The relative sample position to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        public void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            _streamDecoder.SeekTo(samplePosition, seekOrigin);
        }

        /// <summary>
        /// Reads samples into the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer to read the samples into.</param>
        /// <param name="offset">The index to start reading samples into the buffer.</param>
        /// <param name="count">The number of samples that should be read into the buffer.</param>
        /// <returns>The number of floats read into the buffer.</returns>
        /// <exception cref="ArgumentException">
        /// The buffer is too small or the length is not a multiple of <see cref="Channels"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is less than zero.</exception>
        /// <remarks>
        /// The data populated into <paramref name="buffer"/> is interleaved by channel 
        /// in normal PCM fashion: Left, Right, Left, Right, Left, Right.
        /// </remarks>
        public int ReadSamples(float[] buffer, int offset, int count)
        {
            return ReadSamples(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc/>
        public int ReadSamples(Span<float> buffer)
        {
            // don't allow non-aligned reads (always on a full sample boundary!)
            int count = buffer.Length - buffer.Length % _streamDecoder.Channels;
            if (count != 0)
            {
                return _streamDecoder.Read(buffer.Slice(0, count));
            }
            return 0;
        }

        #endregion
    }
}
