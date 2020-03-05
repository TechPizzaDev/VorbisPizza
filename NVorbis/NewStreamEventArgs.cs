using NVorbis.Contracts;
using System;

namespace NVorbis
{
    /// <summary>
    /// Event data for when a new logical stream is found in a container.
    /// </summary>
    [Serializable]
    public class NewStreamEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance of <see cref="NewStreamEventArgs"/> with the specified <see cref="IVorbisStreamDecoder"/>.
        /// </summary>
        /// <param name="streamDecoder">An <see cref="IVorbisStreamDecoder"/> instance.</param>
        public NewStreamEventArgs(IVorbisStreamDecoder streamDecoder)
        {
            StreamDecoder = streamDecoder ?? throw new ArgumentNullException(nameof(streamDecoder));
        }

        /// <summary>
        /// Gets new the <see cref="IVorbisStreamDecoder"/> instance.
        /// </summary>
        public IVorbisStreamDecoder StreamDecoder { get; private set; }

        /// <summary>
        /// Gets or sets whether to ignore the logical stream associated with the packet provider.
        /// </summary>
        public bool IgnoreStream { get; set; }
    }
}
