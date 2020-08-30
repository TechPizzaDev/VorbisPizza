namespace NVorbis.Contracts
{
    internal interface IMdct
    {
        void Reverse(float[] samples, int sampleCount);
    }
}
