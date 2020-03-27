/****************************************************************************
 * NVorbis                                                                  *
 * Copyright (C) 2014, Andrew Ward <afward@gmail.com>                       *
 *                                                                          *
 * See COPYING for license terms (Ms-PL).                                   *
 *                                                                          *
 ***************************************************************************/
using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis
{
    public class VorbisReader : IDisposable
    {
        private IVorbisContainerReader _containerReader;
        private List<VorbisStreamDecoder> _decoders;
        private List<int> _serials;

        private VorbisReader()
        {
            ClipSamples = true;

            _decoders = new List<VorbisStreamDecoder>();
            _serials = new List<int>();
        }

        public VorbisReader(string filePath)
            : this(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read), leaveOpen: false)
        {
        }

        public VorbisReader(Stream stream, bool leaveOpen) : this()
        {
            var oggContainer = new Ogg.OggContainerReader(stream, leaveOpen);
            if (!LoadContainer(oggContainer))
            {
                // oops, not Ogg!
                // we don't support any other container types here, so error out
                oggContainer.Dispose();
                throw new InvalidDataException("Could not determine container type!");
            }
            _containerReader = oggContainer;

            if (_decoders.Count == 0) 
                throw new InvalidDataException("No Vorbis data found!");
        }

        public VorbisReader(IVorbisContainerReader containerReader) : this()
        {
            if (!LoadContainer(containerReader))
                throw new InvalidDataException("Container did not initialize!");
            
            _containerReader = containerReader;

            if (_decoders.Count == 0) 
                throw new InvalidDataException("No Vorbis data found!");
        }

        public VorbisReader(IVorbisPacketProvider packetProvider) : this()
        {
            var ea = new NewStreamEventArgs(packetProvider);
            NewStream(this, ea);
            if (ea.IgnoreStream)
                throw new InvalidDataException("No Vorbis data found!");
        }

        private bool LoadContainer(IVorbisContainerReader containerReader)
        {
            containerReader.NewStream += NewStream;
            if (!containerReader.Init())
            {
                containerReader.NewStream -= NewStream;
                return false;
            }
            return true;
        }

        private void NewStream(object sender, NewStreamEventArgs ea)
        {
            var packetProvider = ea.PacketProvider;
            var decoder = new VorbisStreamDecoder(packetProvider);
            if (decoder.TryInit())
            {
                _decoders.Add(decoder);
                _serials.Add(packetProvider.StreamSerial);
            }
            else
            {
                // This is almost certainly not a Vorbis stream
                ea.IgnoreStream = true;
            }
        }

        public void Dispose()
        {
            if (_decoders != null)
            {
                foreach (var decoder in _decoders)
                    decoder.Dispose();
                
                _decoders.Clear();
                _decoders = null;
            }

            if (_containerReader != null)
            {
                _containerReader.NewStream -= NewStream;
                _containerReader.Dispose();
                _containerReader = null;
            }
        }

        private VorbisStreamDecoder ActiveDecoder
        {
            get
            {
                if (_decoders == null)
                    throw new ObjectDisposedException(GetType().FullName);
                return _decoders[StreamIndex];
            }
        }

        #region Public Interface

        /// <summary>
        /// Gets the number of channels in the current stream.
        /// </summary>
        public int Channels => ActiveDecoder._channels;

        /// <summary>
        /// Gets the sample rate of the current stream.
        /// </summary>
        public int SampleRate => ActiveDecoder._sampleRate;

        /// <summary>
        /// Gets the encoder's upper bitrate of the current stream.
        /// </summary>
        public int UpperBitrate => ActiveDecoder._upperBitrate;

        /// <summary>
        /// Gets the encoder's nominal bitrate of the current stream.
        /// </summary>
        public int NominalBitrate => ActiveDecoder._nominalBitrate;

        /// <summary>
        /// Gets the encoder's lower bitrate of the current stream.
        /// </summary>
        public int LowerBitrate => ActiveDecoder._lowerBitrate;

        /// <summary>
        /// Gets the encoder's vendor string for the current stream.
        /// </summary>
        public string Vendor => ActiveDecoder._vendor;

        /// <summary>
        /// Gets the comments in the current stream.
        /// </summary>
        public string[] Comments => ActiveDecoder._comments;

        /// <summary>
        /// Gets whether the previous short sample count was due to a parameter change in the stream.
        /// </summary>
        public bool IsParameterChange => ActiveDecoder.IsParameterChange;

        /// <summary>
        /// Gets the number of bits read that are related to framing and transport alone.
        /// </summary>
        public long ContainerOverheadBits => ActiveDecoder.ContainerBits;

        /// <summary>
        /// Gets or sets whether to automatically apply clipping to samples returned by <see cref="ReadSamples"/>.
        /// </summary>
        public bool ClipSamples { get; set; }

        /// <summary>
        /// Gets the currently selected stream's index.
        /// </summary>
        public int StreamIndex { get; private set; }

        /// <summary>
        /// Gets stats from each available decoder stream.
        /// </summary>
        public IVorbisStreamStatus[] GetStatusReaders()
        {
            var stats = new IVorbisStreamStatus[_decoders.Count];
            for (int i = 0; i < stats.Length; i++)
                stats[i] = _decoders[i];
            return stats;
        }

        /// <summary>
        /// Reads decoded samples from the current logical stream
        /// </summary>
        /// <param name="buffer">The buffer to write the samples to</param>
        /// <returns>The number of samples written</returns>
        public int ReadSamples(Span<float> buffer)
        {
            int count = ActiveDecoder.ReadSamples(buffer);

            if (ClipSamples)
            {
                var decoder = _decoders[StreamIndex];
                for (int i = 0; i < count; i++)
                    buffer[i] = Utils.ClipValue(buffer[i], ref decoder._clipped);
            }

            return count;
        }

        /// <summary>
        /// Clears the parameter change flag so further samples can be requested.
        /// </summary>
        public void ClearParameterChange()
        {
            ActiveDecoder.IsParameterChange = false;
        }

        /// <summary>
        /// Returns the number of logical streams found so far in the physical container.
        /// </summary>
        public int StreamCount => _decoders.Count;

        /// <summary>
        /// Searches for the next stream in a concatenated file.
        /// </summary>
        /// <returns>Whether if a new stream was found.</returns>
        public bool FindNextStream()
        {
            if (_containerReader == null) 
                return false;
            return _containerReader.FindNextStream();
        }

        /// <summary>
        /// Switches to an alternate logical stream.
        /// </summary>
        /// <param name="index">The logical stream index to switch to,</param>
        /// <returns>
        /// Whether the properties of the logical stream differ from those of
        /// the one previously being decoded.
        /// </returns>
        public bool SwitchStreams(int index)
        {
            if (index < 0 || index >= StreamCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (_decoders == null)
                throw new ObjectDisposedException(GetType().FullName);

            if (StreamIndex == index)
                return false;

            var curentDecoder = _decoders[StreamIndex];
            StreamIndex = index;
            var newDecoder = _decoders[StreamIndex];

            return curentDecoder._channels != newDecoder._channels 
                || curentDecoder._sampleRate != newDecoder._sampleRate;
        }

        /// <summary>
        /// Gets or sets the current timestamp of the decoder.  
        /// Is the timestamp before the next sample to be decoded,
        /// </summary>
        public TimeSpan TimePosition
        {
            get => TimeSpan.FromSeconds((double)ActiveDecoder.CurrentPosition / SampleRate);
            set => ActiveDecoder.SeekTo((long)(value.TotalSeconds * SampleRate));

        }

        /// <summary>
        /// Gets or sets the current position of the next sample to be decoded.
        /// </summary>
        public long SamplePosition
        {
            get => ActiveDecoder.CurrentPosition;
            set => ActiveDecoder.SeekTo(value);
        }

        /// <summary>
        /// Gets the total length of the current logical stream.
        /// </summary>
        public TimeSpan? TotalTime
        {
            get
            {
                var decoder = ActiveDecoder;
                if (decoder.CanSeek)
                    return TimeSpan.FromSeconds((double)decoder.GetLastGranulePos() / decoder._sampleRate);
                return null;
            }
        }

        /// <summary>
        /// Gets the total sample amount of the current logical stream.
        /// </summary>
        public long? TotalSamples
        {
            get
            {
                var decoder = ActiveDecoder;
                if (decoder.CanSeek)
                    return decoder.GetLastGranulePos();
                return null;
            }
        }

        #endregion
    }
}
