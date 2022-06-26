using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal delegate bool NewStreamCallback(Contracts.IPacketProvider packetProvider);

    internal abstract class PageReaderBase : IPageReader
    {
        private readonly HashSet<int> _ignoredSerials = new();
        private byte[] _overflowBuf;
        private int _overflowBufIndex;

        private Stream _stream;
        private bool _leaveOpen;

        protected PageReaderBase(Stream stream, bool leaveOpen)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        protected long StreamPosition => _stream?.Position ?? throw new ObjectDisposedException(nameof(PageReaderBase));

        public long ContainerBits { get; private set; }

        public long WasteBits { get; private set; }

        private bool VerifyPage(ReadOnlySpan<byte> headerBuf, out byte[] pageBuf, out int bytesRead)
        {
            byte segCnt = headerBuf[26];
            if (headerBuf.Length < 27 + segCnt)
            {
                pageBuf = null;
                bytesRead = 0;
                return false;
            }

            int dataLen = 0;
            for (int i = 0; i < segCnt; i++)
            {
                dataLen += headerBuf[i + 27];
            }

            pageBuf = new byte[dataLen + segCnt + 27];
            headerBuf.Slice(0, segCnt + 27).CopyTo(pageBuf);
            bytesRead = EnsureRead(pageBuf.AsSpan(segCnt + 27, dataLen));
            if (bytesRead != dataLen) return false;

            Crc crc = Crc.Create();
            Span<byte> pb0 = pageBuf.AsSpan(0, 22);
            crc.Update(pb0);
            crc.Update(0);
            crc.Update(0);
            crc.Update(0);
            crc.Update(0);

            Span<byte> pb1 = pageBuf.AsSpan(26, pageBuf.Length - 26);
            crc.Update(pb1);
            return crc.Test(BinaryPrimitives.ReadUInt32BigEndian(pageBuf.AsSpan(22, 4)));
        }

        private bool AddPage(byte[] pageBuf, bool isResync)
        {
            int streamSerial = BitConverter.ToInt32(pageBuf, 14);
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
                byte[] newBuf = new byte[_overflowBuf.Length - _overflowBufIndex + count];
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

        private int FillHeader(Span<byte> buf, int maxTries = 10)
        {
            int copyCount = 0;
            if (_overflowBuf != null)
            {
                copyCount = Math.Min(_overflowBuf.Length - _overflowBufIndex, buf.Length);
                _overflowBuf.AsSpan(_overflowBufIndex, copyCount).CopyTo(buf);
                buf = buf.Slice(copyCount);
                if ((_overflowBufIndex += copyCount) == _overflowBuf.Length)
                {
                    _overflowBuf = null;
                }
            }
            if (buf.Length > 0)
            {
                copyCount += EnsureRead(buf, maxTries);
            }
            return copyCount;
        }

        private bool VerifyHeader(Span<byte> buffer, ref int cnt, bool isFromReadNextPage)
        {
            if (buffer[0] == 0x4f && buffer[1] == 0x67 && buffer[2] == 0x67 && buffer[3] == 0x53)
            {
                if (cnt < 27)
                {
                    if (isFromReadNextPage)
                    {
                        cnt += FillHeader(buffer.Slice(27 - cnt, 27 - cnt));
                    }
                    else
                    {
                        cnt += EnsureRead(buffer.Slice(27 - cnt, 27 - cnt));
                    }
                }

                if (cnt >= 27)
                {
                    byte segCnt = buffer[26];
                    if (isFromReadNextPage)
                    {
                        cnt += FillHeader(buffer.Slice(27, segCnt));
                    }
                    else
                    {
                        cnt += EnsureRead(buffer.Slice(27, segCnt));
                    }
                    if (cnt == 27 + segCnt)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Network streams don't always return the requested size immediately, so this
        // method is used to ensure we fill the buffer if it is possible.
        // Note that it will loop until getting a certain count of zero reads (default: 10).
        // This means in most cases, the network stream probably died by the time we return
        // a short read.
        protected int EnsureRead(Span<byte> buf, int maxTries = 10)
        {
            int read = 0;
            int tries = 0;
            do
            {
                int cnt = _stream.Read(buf.Slice(read, buf.Length - read));
                if (cnt == 0 && ++tries == maxTries)
                {
                    break;
                }
                read += cnt;
            }
            while (read < buf.Length);
            return read;
        }

        /// <summary>
        /// Verifies the sync sequence and loads the rest of the header.
        /// </summary>
        /// <returns><see langword="true"/> if successful, otherwise <see langword="false"/>.</returns>
        protected bool VerifyHeader(Span<byte> buffer, ref int cnt)
        {
            return VerifyHeader(buffer, ref cnt, false);
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
            if (!CheckLock()) throw new InvalidOperationException("Must be locked prior to reading!");

            return _stream.Seek(offset, SeekOrigin.Begin);
        }

        protected virtual void PrepareStreamForNextPage()
        {
        }

        protected virtual void SaveNextPageSearch()
        {
        }

        protected abstract bool AddPage(int streamSerial, byte[] pageBuf, bool isResync);

        protected abstract void SetEndOfStreams();

        public virtual void Lock()
        {
        }

        protected virtual bool CheckLock()
        {
            return true;
        }

        public virtual bool Release()
        {
            return false;
        }

        public bool ReadNextPage()
        {
            // 27 - 4 + 27 + 255 (found sync at end of first buffer, and found page has full segment count)
            Span<byte> headerBuf = stackalloc byte[305];

            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock()) throw new InvalidOperationException("Must be locked prior to reading!");

            bool isResync = false;

            int ofs = 0;
            int cnt;
            PrepareStreamForNextPage();
            while ((cnt = FillHeader(headerBuf.Slice(ofs, 27 - ofs))) > 0)
            {
                cnt += ofs;
                for (int i = 0; i < cnt - 4; i++)
                {
                    if (VerifyHeader(headerBuf.Slice(i), ref cnt, true))
                    {
                        if (VerifyPage(headerBuf.Slice(i, cnt), out byte[] pageBuf, out int bytesRead))
                        {
                            // one way or the other, we have to clear out the page's bytes from the queue (if queued)
                            ClearEnqueuedData(bytesRead);

                            // also, we need to let our inheritors have a chance to save state for next time
                            SaveNextPageSearch();

                            // pass it to our inheritor
                            if (AddPage(pageBuf, isResync))
                            {
                                return true;
                            }

                            // otherwise, the whole page is useless...

                            // save off that we've burned that many bits
                            WasteBits += pageBuf.Length * 8;

                            // set up to load the next page, then loop
                            ofs = 0;
                            cnt = 0;
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

                if (cnt >= 3)
                {
                    headerBuf[0] = headerBuf[cnt - 3];
                    headerBuf[1] = headerBuf[cnt - 2];
                    headerBuf[2] = headerBuf[cnt - 1];
                    ofs = 3;
                }
            }

            if (cnt == 0)
            {
                SetEndOfStreams();
            }

            return false;
        }

        public abstract bool ReadPageAt(long offset);

        public void Dispose()
        {
            SetEndOfStreams();

            if (!_leaveOpen)
            {
                _stream?.Dispose();
            }
            _stream = null;
        }
    }
}
