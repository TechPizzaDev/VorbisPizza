using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    [SkipLocalsInit]
    internal abstract class PageReaderBase : IPageReader
    {
        internal static Func<ICrc> CreateCrc { get; set; } = () => new Crc();

        private readonly ICrc _crc = CreateCrc();
        private readonly HashSet<int> _ignoredSerials = new HashSet<int>();

        private byte[]? _overflowBuf;
        private int _overflowBufIndex;

        private Stream _stream;
        private bool _leaveOpen;

        protected PageReaderBase(Stream stream, bool leaveOpen)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        protected long StreamPosition => _stream?.Position ??
            throw new ObjectDisposedException(GetType().FullName);

        public long ContainerBits { get; private set; }

        public long WasteBits { get; private set; }

        private bool VerifyPage(
            ReadOnlySpan<byte> headerBuffer,
            [MaybeNullWhen(false)] out byte[] pageBuffer,
            out int bytesRead)
        {
            int segCount = headerBuffer[26];
            if (headerBuffer.Length < 27 + segCount)
            {
                pageBuffer = null;
                bytesRead = 0;
                return false;
            }

            int dataLength = 0;
            for (int i = 0; i < segCount; i++)
                dataLength += headerBuffer[i + 27];

            int pageLength = dataLength + segCount + 27;
            pageBuffer = new byte[pageLength];
            var page = pageBuffer.AsSpan();

            headerBuffer.Slice(0, segCount + 27).CopyTo(page);

            bytesRead = EnsureRead(page.Slice(segCount + 27, dataLength));
            if (bytesRead != dataLength)
                return false;

            _crc.Reset();
            _crc.Update(page.Slice(0, 22));
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(page[26..pageLength]);

            uint pageCrc = BinaryPrimitives.ReadUInt32LittleEndian(page[22..]);
            return _crc.Test(pageCrc);
        }

        private bool AddPage(byte[] pageBuf, bool isResync)
        {
            int streamSerial = BinaryPrimitives.ReadInt32LittleEndian(pageBuf.AsSpan(14));
            if (!_ignoredSerials.Contains(streamSerial))
            {
                if (AddPage(streamSerial, pageBuf, isResync))
                {
                    ContainerBits += 8 * (27 + pageBuf[26]);
                    return true;
                }
                _ignoredSerials.Add(streamSerial);
            }
            return false;
        }

        private void EnqueueData(byte[] buf, int count)
        {
            if (_overflowBuf != null)
            {
                var newBuf = new byte[_overflowBuf.Length - _overflowBufIndex + count];
                Buffer.BlockCopy(_overflowBuf, _overflowBufIndex, newBuf, 0, newBuf.Length - count);
                int index = buf.Length - count;
                Buffer.BlockCopy(buf, index, newBuf, newBuf.Length - count, count);
                _overflowBufIndex = 0;
            }
            else
            {
                _overflowBuf = buf;
                _overflowBufIndex = buf.Length - count;
            }
        }

        private void ClearEnqueuedData(int count)
        {
            if (_overflowBuf != null && (_overflowBufIndex += count) >= _overflowBuf.Length)
            {
                _overflowBuf = null;
            }
        }

        private int FillHeader(Span<byte> buffer, int maxTries = 10)
        {
            int total = 0;

            if (_overflowBuf != null)
            {
                total = Math.Min(_overflowBuf.Length - _overflowBufIndex, buffer.Length);
                _overflowBuf.AsSpan(_overflowBufIndex, total).CopyTo(buffer);
                buffer = buffer[total..];

                if ((_overflowBufIndex += total) == _overflowBuf.Length)
                    _overflowBuf = null;
            }

            if (!buffer.IsEmpty)
                total += EnsureRead(buffer, maxTries);

            return total;
        }

        private bool VerifyHeader(Span<byte> buffer, ref int count, bool isFromReadNextPage)
        {
            if (buffer[0] == 0x4f &&
                buffer[1] == 0x67 &&
                buffer[2] == 0x67 &&
                buffer[3] == 0x53)
            {
                if (count < 27)
                {
                    if (isFromReadNextPage)
                        count += FillHeader(buffer.Slice(27 - count, 27 - count));
                    else
                        count += EnsureRead(buffer.Slice(27 - count, 27 - count));
                }

                if (count >= 27)
                {
                    byte segCnt = buffer[26];
                    if (isFromReadNextPage)
                        count += FillHeader(buffer.Slice(27, segCnt));
                    else
                        count += EnsureRead(buffer.Slice(27, segCnt));

                    if (count == 27 + segCnt)
                        return true;
                }
            }
            return false;
        }

        // Network streams don't always return the requested size immediately, so this
        // method is used to ensure we fill the buffer if it is possible.
        // Note that it will loop until getting a certain count of zero reads (default: 10).
        // This means in most cases, the network stream probably died by the time we return
        // a short read.
        protected int EnsureRead(Span<byte> buffer, int maxTries = 10)
        {
            int totalRead = 0;
            int tries = 0;
            do
            {
                int read = _stream.Read(buffer);
                if (read == 0 && ++tries == maxTries)
                    break;

                totalRead += read;
                buffer = buffer[read..];
            }
            while (!buffer.IsEmpty);
            return totalRead;
        }

        /// <summary>
        /// Verifies the sync sequence and loads the rest of the header.
        /// </summary>
        /// <returns><see langword="true"/> if successful, otherwise <see langword="false"/>.</returns>
        protected bool VerifyHeader(Span<byte> buffer, ref int count)
        {
            return VerifyHeader(buffer, ref count, false);
        }

        /// <summary>
        /// Seeks the underlying stream to the requested position.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <returns>The new position of the stream.</returns>
        /// <exception cref="InvalidOperationException">The stream is not seekable.</exception>
        protected long SeekStream(long offset)
        {
            // make sure we're locked; seeking won't matter if we aren't
            if (!CheckLock())
                throw new InvalidOperationException("Must be locked prior to reading!");

            return _stream.Seek(offset, SeekOrigin.Begin);
        }

        protected virtual void PrepareStreamForNextPage() { }

        protected virtual void SaveNextPageSearch() { }

        protected abstract bool AddPage(int streamSerial, byte[] pageBuf, bool isResync);

        protected abstract void SetEndOfStreams();

        public virtual void Lock() { }

        protected virtual bool CheckLock() => true;

        public virtual bool Release() => false;

        public bool ReadNextPage()
        {
            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock())
                throw new InvalidOperationException("Must be locked prior to reading!");


            // 27 - 4 + 27 + 255 (found sync at end of first buffer, and found page has full segment count)
            Span<byte> headerBuf = stackalloc byte[305];

            bool isResync = false;

            int offset = 0;
            int count;
            PrepareStreamForNextPage();
            while ((count = FillHeader(headerBuf[offset..27])) > 0)
            {
                count += offset;
                for (int i = 0; i < count - 4; i++)
                {
                    if (VerifyHeader(headerBuf[i..], ref count, true))
                    {
                        if (VerifyPage(headerBuf.Slice(i, count), out byte[]? pageBuf, out int bytesRead))
                        {
                            // one way or the other, we have to clear out the page's bytes from the queue (if queued)
                            ClearEnqueuedData(bytesRead);

                            // also, we need to let our inheritors have a chance to save state for next time
                            SaveNextPageSearch();

                            // pass it to our inheritor
                            if (AddPage(pageBuf, isResync))
                                return true;

                            // otherwise, the whole page is useless...

                            // save off that we've burned that many bits
                            WasteBits += pageBuf.Length * 8;

                            // set up to load the next page, then loop
                            offset = 0;
                            count = 0;
                            break;
                        }
                        else if (pageBuf != null)
                        {
                            EnqueueData(pageBuf, bytesRead);
                        }
                    }
                    WasteBits += 8;
                    isResync = true;
                }

                if (count >= 3)
                {
                    headerBuf[0] = headerBuf[count - 3];
                    headerBuf[1] = headerBuf[count - 2];
                    headerBuf[2] = headerBuf[count - 1];
                    offset = 3;
                }
            }

            if (count == 0)
                SetEndOfStreams();

            return false;
        }

        public abstract bool ReadPageAt(long offset);

        public void Dispose()
        {
            SetEndOfStreams();

            if (!_leaveOpen)
                _stream?.Dispose();
            _stream = null!;
        }
    }
}
