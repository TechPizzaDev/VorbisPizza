namespace NVorbis.Contracts
{
    interface IMdct
    {
        void Reverse(float[] samples, float[] buf2, int sampleCount);
    }
}
