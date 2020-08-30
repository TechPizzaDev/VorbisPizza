using NVorbis.Contracts;
using NVorbis.Contracts.Ogg;
using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis.Ogg
{
    /// <summary>
    /// Implements <see cref="IContainerReader"/> for Ogg format files for low memory cost.
    /// </summary>
    public sealed class ContainerReader : IContainerReader
    {
        internal static Func<Stream, bool, Func<IPacketProvider, bool>, IPageReader> CreatePageReader { get; set; } =
            (s, lo, cb) => new PageReader(s, lo, cb);

        internal static Func<Stream, bool, Func<IPacketProvider, bool>, IPageReader> CreateForwardOnlyPageReader { get; set; } =
            (s, lo, cb) => new ForwardOnlyPageReader(s, lo, cb);

        private IPageReader _reader;
        private List<WeakReference<IPacketProvider>> _packetProviders;
        private bool _foundStream;

        /// <summary>
        /// Gets or sets the callback to invoke when a new stream is encountered in the container.
        /// </summary>
        public NewStreamHandler? NewStreamCallback { get; set; }

        /// <summary>
        /// Returns a list of streams available from this container.
        /// </summary>
        public IReadOnlyList<IPacketProvider> GetStreams()
        {
            var list = new List<IPacketProvider>(_packetProviders.Count);
            for (int i = 0; i < _packetProviders.Count; i++)
            {
                if (_packetProviders[i].TryGetTarget(out var pp))
                {
                    list.Add(pp);
                }
                else
                {
                    list.RemoveAt(i);
                    i--;
                }
            }
            return list;
        }

        /// <summary>
        /// Gets whether the underlying stream can seek.
        /// </summary>
        public bool CanSeek { get; }

        /// <summary>
        /// Gets the number of bits in the container that are not associated with a logical stream.
        /// </summary>
        public long WasteBits => _reader.WasteBits;

        /// <summary>
        /// Gets the number of bits in the container that are strictly for framing of logical streams.
        /// </summary>
        public long ContainerBits => _reader.ContainerBits;


        /// <summary>
        /// Creates a new instance of <see cref="ContainerReader"/>.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read.</param>
        /// <param name="leaveOpen"><c>false</c> to close the stream when disposed, otherwise <c>true</c>.</param>
        /// <exception cref="ArgumentException">The <paramref name="stream"/> is not seekable.</exception>
        public ContainerReader(Stream stream, bool leaveOpen)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _packetProviders = new List<WeakReference<IPacketProvider>>();

            if (stream.CanSeek)
            {
                _reader = CreatePageReader(stream, leaveOpen, ProcessNewStream);
                CanSeek = true;
            }
            else
            {
                _reader = CreateForwardOnlyPageReader(stream, leaveOpen, ProcessNewStream);
            }
        }

        /// <summary>
        /// Attempts to initialize the container.
        /// </summary>
        /// <returns><see langword="true"/> if successful, otherwise <see langword="false"/>.</returns>
        public bool TryInit()
        {
            return FindNextStream();
        }

        /// <summary>
        /// Finds the next new stream in the container.
        /// </summary>
        /// <returns><c>True</c> if a new stream was found, otherwise <c>False</c>.</returns>
        public bool FindNextStream()
        {
            _reader.Lock();
            try
            {
                _foundStream = false;
                while (_reader.ReadNextPage())
                {
                    if (_foundStream)
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _reader.Release();
            }
        }

        private bool ProcessNewStream(IPacketProvider packetProvider)
        {
            var relock = _reader.Release();
            try
            {
                if (NewStreamCallback?.Invoke(packetProvider) ?? true)
                {
                    _packetProviders.Add(new WeakReference<IPacketProvider>(packetProvider));
                    _foundStream = true;
                    return true;
                }
                return false;
            }
            finally
            {
                if (relock)
                {
                    _reader.Lock();
                }
            }
        }

        /// <summary>
        /// Cleans up
        /// </summary>
        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null!;
        }
    }
}
