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

namespace NVorbis.Ogg
{
    /// <summary>
    /// Provides an <see cref="IVorbisContainerReader"/> implementation for basic Ogg files.
    /// </summary>
    public class OggContainerReader : IVorbisContainerReader
    {
        internal static ReadOnlyMemory<byte> OggsHeader { get; } = new byte[4] { 0x4f, 0x67, 0x67, 0x53 };
        internal static ReadOnlyMemory<byte> RiffHeader { get; } = new byte[4] { 82, 73, 70, 70 };

        private Stream _stream;
        private bool _leaveOpen;
        private Dictionary<int, OggPacketReader> _packetReaders;
        private List<int> _disposedStreamSerials;
        private long _nextPageOffset;
        private int _pageCount;
        private byte[] _readBuffer = new byte[65025];   // up to a full page of data (but no more!)

        private long _containerBits, _wasteBits;

        /// <summary>
        /// Gets the list of stream serials found in the container so far.
        /// </summary>
        public int[] StreamSerials => System.Linq.Enumerable.ToArray(_packetReaders.Keys);

        /// <summary>
        /// Event raised when a new logical stream is found in the container.
        /// </summary>
        public event EventHandler<NewStreamEventArgs> NewStream;

        /// <summary>
        /// Creates a new instance with the specified file.
        /// </summary>
        /// <param name="path">The full path to the file.</param>
        public OggContainerReader(string path)
            : this(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), leaveOpen: false)
        {
        }

        /// <summary>
        /// Creates a new instance with the specified stream.  Optionally sets to close the stream when disposed.
        /// </summary>
        /// <param name="stream">The stream to read.</param>
        /// <param name="leaveOpen">
        /// <c>false</c> to close the stream when <see cref="Dispose"/> is called.
        /// </param>
        public OggContainerReader(Stream stream, bool leaveOpen)
        {
            _packetReaders = new Dictionary<int, OggPacketReader>();
            _disposedStreamSerials = new List<int>();

            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;

            if (!_stream.CanSeek)
                throw new ArgumentException("The specified stream must be seek-able!", nameof(stream));
        }

        /// <summary>
        /// Initializes the container and finds the first stream.
        /// </summary>
        /// <returns><c>true</c> if a valid logical stream is found, otherwise <c>false</c>.</returns>
        public bool Init()
        {
            return GatherNextPage() != -1;
        }

        /// <summary>
        /// Gets the <see cref="IVorbisPacketProvider"/> instance for the specified stream serial.
        /// </summary>
        /// <param name="streamSerial">The stream serial to look for.</param>
        /// <returns>An <see cref="IVorbisPacketProvider"/> instance.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The specified stream serial was not found.</exception>
        public IVorbisPacketProvider GetStream(int streamSerial)
        {
            if (!_packetReaders.TryGetValue(streamSerial, out OggPacketReader provider))
                throw new ArgumentOutOfRangeException(nameof(streamSerial));
            return provider;
        }

        /// <summary>
        /// Finds the next new stream in the container.
        /// </summary>
        /// <returns><c>True</c> if a new stream was found, otherwise <c>False</c>.</returns>
        public bool FindNextStream()
        {
            // goes through all the pages until the serial count increases
            int count = _packetReaders.Count;
            while (_packetReaders.Count == count)
            {
                if (GatherNextPage() == -1)
                    break;
            }
            return count > _packetReaders.Count;
        }

        /// <summary>
        /// Gets the number of pages that have been read in the container.
        /// </summary>
        public int PagesRead => _pageCount;

        /// <summary>
        /// Retrieves the total number of pages in the container.
        /// </summary>
        /// <returns>The total number of pages.</returns>
        public int GetTotalPageCount()
        {
            // just read pages until we can't any more...
            while (true)
            {
                if (GatherNextPage() == -1)
                    break;
            }

            return _pageCount;
        }

        /// <summary>
        /// Gets whether the container supports seeking.
        /// </summary>
        public bool CanSeek => true;

        /// <summary>
        /// Gets the number of bits in the container that are not associated with a logical stream.
        /// </summary>
        public long WasteBits => _wasteBits;

        // private implmentation bits
        private struct PageHeader
        {
            public int StreamSerial { get; set; }
            public OggPageFlags Flags { get; set; }
            public long GranulePosition { get; set; }
            public int SequenceNumber { get; set; }
            public long DataOffset { get; set; }
            public Memory<int> PacketSizes { get; set; }
            public bool LastPacketContinues { get; set; }
            public bool IsResync { get; set; }
        }

        private PageHeader? ReadPageHeader(long position)
        {
            // set the stream's position
            _stream.Seek(position, SeekOrigin.Begin);

            // header
            // NB: if the stream didn't have an EOS flag, 
            // this is the most likely spot for the EOF to be found...
            if (_stream.Read(_readBuffer, 0, 27) != 27)
                return null;

            // capture signature
            var sigSpan = _readBuffer.AsSpan(0, 4);
            if (!sigSpan.SequenceEqual(OggsHeader.Span))
            {
                if (sigSpan.SequenceEqual(RiffHeader.Span))
                    throw new NotImplementedException("RIFF is currently not supported.");

                return null;
            }

            // check the stream version
            if (_readBuffer[4] != 0)
                return null;

            // start populating the header
            var hdr = new PageHeader();

            // bit flags
            hdr.Flags = (OggPageFlags)_readBuffer[5];

            // granulePosition
            hdr.GranulePosition = BitConverter.ToInt64(_readBuffer, 6);

            // stream serial
            hdr.StreamSerial = BitConverter.ToInt32(_readBuffer, 14);

            // sequence number
            hdr.SequenceNumber = BitConverter.ToInt32(_readBuffer, 18);

            // save off the CRC
            var pageCrc = BitConverter.ToUInt32(_readBuffer, 22);

            // start calculating the CRC value for this page
            var _crc = new Crc32();
            _crc.Update(_readBuffer.AsSpan(0, 22));
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(_readBuffer[26]);

            // figure out the length of the page
            int segCnt = (int)_readBuffer[26];
            if (_stream.Read(_readBuffer, 0, segCnt) != segCnt)
                return null;

            var packetSizes = new int[segCnt];
            int packetSizeCount = 0;

            int size = 0, idx = 0;
            for (int i = 0; i < segCnt; i++)
            {
                if (packetSizeCount == idx)
                    packetSizeCount++;

                byte tmp = _readBuffer[i];
                packetSizes[idx] += tmp;
                size += tmp;

                if (tmp < 255)
                {
                    idx++;
                    hdr.LastPacketContinues = false;
                }
                else
                {
                    hdr.LastPacketContinues = true;
                }
            }
            _crc.Update(_readBuffer.AsSpan(0, segCnt));

            hdr.PacketSizes = packetSizes.AsMemory(0, packetSizeCount);
            hdr.DataOffset = position + 27 + segCnt;

            // now we have to go through every byte in the page
            if (_stream.Read(_readBuffer, 0, size) != size)
                return null;

            _crc.Update(_readBuffer.AsSpan(0, size));

            if (_crc.Test(pageCrc))
            {
                _containerBits += 8 * (27 + segCnt);
                ++_pageCount;
                return hdr;
            }
            return null;
        }

        private PageHeader? FindNextPageHeader()
        {
            long startPos = _nextPageOffset;
            bool isResync = false;
            PageHeader? hdr;
            while ((hdr = ReadPageHeader(startPos)) == null)
            {
                isResync = true;
                _wasteBits += 8;
                _stream.Position = ++startPos;

                Span<byte> buf = stackalloc byte[4];
                int read = _stream.Read(buf);
                if (read != 4)
                    return null;

                int cnt = 0;
                do
                {
                    if (buf[0] == 0x4f)
                    {
                        if (buf[1] == 0x67 &&
                            buf[2] == 0x67 &&
                            buf[3] == 0x53)
                        {
                            // found it!
                            startPos += cnt;
                            break;
                        }
                    }
                    else
                    {
                        _wasteBits += 8;
                    }

                    int b = _stream.ReadByte();
                    if (b == -1)
                        return null;

                    // shift bytes back and add the fresh byte
                    buf[0] = buf[1];
                    buf[1] = buf[2];
                    buf[2] = buf[3];
                    buf[3] = (byte)b;
                }
                // we will only search through 64KB of data to find the next sync marker. 
                // if it can't be found, we have a badly corrupted stream.
                while (++cnt < 65536);

                if (cnt >= 65536)
                    return null;
            }

            var readHdr = hdr.Value;
            readHdr.IsResync = isResync;

            _nextPageOffset = readHdr.DataOffset;
            foreach (int packetSize in readHdr.PacketSizes.Span)
                _nextPageOffset += packetSize;

            return readHdr;
        }

        private bool AddPage(in PageHeader hdr)
        {
            // get our packet reader (create one if we have to)
            if (!_packetReaders.TryGetValue(hdr.StreamSerial, out OggPacketReader packetReader))
                packetReader = new OggPacketReader(this, hdr.StreamSerial);

            // save off the container bits
            packetReader.ContainerBits += _containerBits;
            _containerBits = 0;

            // get our flags prepped
            bool isContinued = hdr.PacketSizes.Length == 1 && hdr.LastPacketContinues;
            bool isContinuation = (hdr.Flags & OggPageFlags.ContinuesPacket) == OggPageFlags.ContinuesPacket;
            bool isEOS = false;
            bool isResync = hdr.IsResync;

            // add all the packets, making sure to update flags as needed
            long dataOffset = hdr.DataOffset;
            int cnt = hdr.PacketSizes.Length;
            foreach (int size in hdr.PacketSizes.Span)
            {
                var packet = new OggPacket(this, dataOffset, size)
                {
                    PageGranulePosition = hdr.GranulePosition,
                    IsEndOfStream = isEOS,
                    PageSequenceNumber = hdr.SequenceNumber,
                    IsContinued = isContinued,
                    IsContinuation = isContinuation,
                    IsResync = isResync,
                };
                packetReader.AddPacket(packet);

                // update the offset into the stream for each packet
                dataOffset += size;

                // only the first packet in a page can be a continuation or resync
                isContinuation = false;
                isResync = false;

                // only the last packet in a page can be continued or flagged end of stream
                if (--cnt == 1)
                {
                    isContinued = hdr.LastPacketContinues;
                    isEOS = (hdr.Flags & OggPageFlags.EndOfStream) == OggPageFlags.EndOfStream;
                }
            }

            // if the packet reader list doesn't include the serial in question, 
            // add it to the list and indicate a new stream to the caller
            if (!_packetReaders.ContainsKey(hdr.StreamSerial))
            {
                _packetReaders.Add(hdr.StreamSerial, packetReader);
                return true;
            }
            else
            {
                // otherwise, indicate an existing stream to the caller
                return false;
            }
        }

        private int GatherNextPage()
        {
            while (true)
            {
                // get our next header
                var hdr = FindNextPageHeader();
                if (hdr == null)
                    return -1;

                var readHdr = hdr.Value;

                // if it's in a disposed stream, grab the next page instead
                if (_disposedStreamSerials.Contains(readHdr.StreamSerial))
                    continue;

                // otherwise, add it
                if (AddPage(readHdr))
                {
                    var callback = NewStream;
                    if (callback != null)
                    {
                        var ea = new NewStreamEventArgs(_packetReaders[readHdr.StreamSerial]);
                        callback.Invoke(this, ea);
                        if (ea.IgnoreStream)
                        {
                            _packetReaders[readHdr.StreamSerial].Dispose();
                            continue;
                        }
                    }
                }
                return readHdr.StreamSerial;
            }
        }

        // packet reader bits...
        internal void DisposePacketReader(OggPacketReader packetReader)
        {
            _disposedStreamSerials.Add(packetReader.StreamSerial);
            _packetReaders.Remove(packetReader.StreamSerial);
        }

        internal Memory<byte> ReadPacketData(long offset, int size)
        {
            _stream.Position = offset;

            var buffer = new byte[size];
            int read = _stream.Read(buffer);
            return buffer.AsMemory(0, read);
        }

        internal void GatherNextPage(int streamSerial)
        {
            if (!_packetReaders.ContainsKey(streamSerial))
                throw new ArgumentOutOfRangeException(nameof(streamSerial));

            int nextSerial;
            do
            {
                if (_packetReaders[streamSerial].HasEndOfStream)
                    break;

                nextSerial = GatherNextPage();
                if (nextSerial == -1)
                {
                    foreach (var reader in _packetReaders)
                    {
                        if (!reader.Value.HasEndOfStream)
                            reader.Value.SetEndOfStream();
                    }
                    break;
                }
            } while (nextSerial != streamSerial);
        }

        /// <summary>
        /// Disposes this reader and underlying packet readers.
        /// </summary>
        public void Dispose()
        {
            // don't use _packetReaders directly since that'll change the enumeration...
            foreach (var streamSerial in StreamSerials)
                _packetReaders[streamSerial].Dispose();

            _nextPageOffset = 0L;
            _containerBits = 0L;
            _wasteBits = 0L;

            if (!_leaveOpen)
                _stream.Dispose();
        }
    }
}
