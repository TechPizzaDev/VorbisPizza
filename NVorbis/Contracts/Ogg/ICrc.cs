using System;

namespace NVorbis.Contracts.Ogg
{
    interface ICrc
    {
        void Reset();
        void Update(byte value);
        void Update(ReadOnlySpan<byte> value);
        bool Test(uint checkCrc);
    }
}
