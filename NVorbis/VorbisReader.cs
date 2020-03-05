using NVorbis.Contracts;
using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis
{
    /// <summary>
    /// Implements an easy to use wrapper around <see cref="IContainerReader"/> and <see cref="IVorbisStreamDecoder"/>.
    /// </summary>
    public sealed class VorbisReader : IVorbisReader
    {
        internal static Func<Stream, bool, IContainerReader> CreateContainerReader { get; set; } = 
            (s, leaveOpen) => new Ogg.ContainerReader(s, leaveOpen);
        
        internal static Func<IPacketProvider, IVorbisStreamDecoder> CreateStreamDecoder { get; set; } = 
            pp => new VorbisStreamDecoder(pp, new Factory());

        private readonly List<IVorbisStreamDecoder> _decoders;
        private readonly IContainerReader _containerReader;
        private readonly bool _leaveOpen;

        private IVorbisStreamDecoder _streamDecoder;

        public event EventHandler<NewStreamEventArgs> NewStream;

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified file.
        /// </summary>
        /// <param name="fileName">The file to read from.</param>
        public VorbisReader(string fileName) : this(File.OpenRead(fileName), true)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified stream, 
        /// optionally taking ownership of it.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read from.</param>
        /// <param name="leaveOpen"><c>true</c> to not close the stream when disposed, otherwise <c>false</c>.</param>
        public VorbisReader(Stream stream, bool leaveOpen = false)
        {
            _decoders = new List<IVorbisStreamDecoder>();

            var containerReader = CreateContainerReader(stream, leaveOpen);
            containerReader.NewStreamCallback = ProcessNewStream;

            if (!containerReader.TryInit() || _decoders.Count == 0)
            {
                containerReader.NewStreamCallback = null;
                containerReader.Dispose();

                if (!leaveOpen)
                    stream.Dispose();
                throw new ArgumentException("Could not load the specified container!", nameof(containerReader));
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
            NewStream?.Invoke(this, ea);
            if (!ea.IgnoreStream)
            {
                _decoders.Add(decoder);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Releases resources held by this <see cref="VorbisReader"/>.
        /// </summary>
        public void Dispose()
        {
            if (_decoders != null)
            {
                foreach (var decoder in _decoders)
                {
                    (decoder as IDisposable)?.Dispose();
                }
                _decoders.Clear();
            }

            if (_containerReader != null)
            {
                _containerReader.NewStreamCallback = null;

                if (!_leaveOpen)
                    _containerReader.Dispose();
            }
        }

        public IReadOnlyList<IVorbisStreamDecoder> Streams => _decoders;

        #region Convenience Helpers

        // Since most uses of VorbisReader are for single-stream audio files, we can make life simpler for users
        // by exposing the first stream's properties and methods here.

        public int Channels => _streamDecoder.Channels;
        public int SampleRate => _streamDecoder.SampleRate;
        public int UpperBitrate => _streamDecoder.UpperBitrate;
        public int NominalBitrate => _streamDecoder.NominalBitrate;
        public int LowerBitrate => _streamDecoder.LowerBitrate;
        public ITagData Tags => _streamDecoder.Tags;
        public long ContainerOverheadBits => _containerReader?.ContainerBits ?? 0;
        public long ContainerWasteBits => _containerReader?.WasteBits ?? 0;
        public int StreamIndex => _decoders.IndexOf(_streamDecoder);
        public TimeSpan TotalTime => _streamDecoder.TotalTime;
        public long TotalSamples => _streamDecoder.TotalSamples;
        public bool IsEndOfStream => _streamDecoder.IsEndOfStream;
        public bool HasClipped => _streamDecoder.HasClipped;
        public IStreamStats StreamStats => _streamDecoder.Stats;

        public TimeSpan TimePosition
        {
            get => _streamDecoder.TimePosition;
            set => _streamDecoder.TimePosition = value;
        }

        public long SamplePosition
        {
            get => _streamDecoder.SamplePosition;
            set => _streamDecoder.SamplePosition = value;
        }

        public bool ClipSamples
        {
            get => _streamDecoder.ClipSamples;
            set => _streamDecoder.ClipSamples = value;
        }

        public bool FindNextStream()
        {
            if (_containerReader == null) 
                return false;
            return _containerReader.FindNextStream();
        }

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

        public void SeekTo(TimeSpan timePosition)
        {
            _streamDecoder.SeekTo(timePosition);
        }

        public void SeekTo(long samplePosition)
        {
            _streamDecoder.SeekTo(samplePosition);
        }

        public int ReadSamples(Span<float> buffer)
        {
            // don't allow non-aligned reads (always on a full sample boundary!)
            int count = buffer.Length % _streamDecoder.Channels;
            if (count > 0)
                return _streamDecoder.Read(buffer.Slice(0, count));
            return 0;
        }

        #endregion
    }
}
