using System;

namespace NVorbis.Contracts.Ogg
{
    internal interface ICrc
    {
        void Reset();
        void Update(byte value);
        void Update(ReadOnlySpan<byte> value);
        bool Test(uint checkCrc);
    }
}
