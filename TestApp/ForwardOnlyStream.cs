using System;
using System.IO;

namespace TestApp
{
    class ForwardOnlyStream : Stream
    {
        private Stream _stream;
        private bool _leaveOpen;

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _stream.Position; set => throw new NotSupportedException(); }

        public ForwardOnlyStream(Stream stream, bool leaveOpen)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        public override void Flush()
        {

        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_leaveOpen)
                {
                    _stream?.Dispose();
                }
                _stream = null!;
            }

            base.Dispose(disposing);
        }
    }
}
