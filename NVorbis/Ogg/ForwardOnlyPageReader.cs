using System;
using System.Collections.Generic;
using System.IO;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class ForwardOnlyPageReader : PageReaderBase
    {
        private readonly Dictionary<int, IForwardOnlyPacketProvider> _packetProviders = new();
        private readonly NewStreamCallback _newStreamCallback;

        public ForwardOnlyPageReader(Stream stream, bool leaveOpen, NewStreamCallback newStreamCallback)
            : base(stream, leaveOpen)
        {
            _newStreamCallback = newStreamCallback;
        }

        protected override bool AddPage(int streamSerial, byte[] pageBuf, bool isResync)
        {
            if (_packetProviders.TryGetValue(streamSerial, out IForwardOnlyPacketProvider? pp))
            {
                // try to add the page...
                if (pp.AddPage(pageBuf, isResync))
                {
                    // ..., then check to see if this is the end of the stream...
                    if (((PageFlags)pageBuf[5] & PageFlags.EndOfStream) != 0)
                    {
                        // ... and if so tell the packet provider the remove it from our list
                        pp.SetEndOfStream();
                        _packetProviders.Remove(streamSerial);
                    }
                    // ..., then let our caller know we're good
                    return true;
                }
                // otherwise, let PageReaderBase.ReadNextPage() know that we can't use the page:
                return false;
            }

            // try to add the stream to the list.
            pp = new ForwardOnlyPacketProvider(this, streamSerial);
            if (pp.AddPage(pageBuf, isResync))
            {
                _packetProviders.Add(streamSerial, pp);
                if (_newStreamCallback(pp))
                {
                    return true;
                }
                _packetProviders.Remove(streamSerial);
            }
            return false;
        }

        protected override void SetEndOfStreams()
        {
            foreach (KeyValuePair<int, IForwardOnlyPacketProvider> kvp in _packetProviders)
            {
                kvp.Value.SetEndOfStream();
            }
            _packetProviders.Clear();
        }

        public override bool ReadPageAt(long offset)
        {
            throw new NotSupportedException();
        }
    }
}
