namespace NVorbis.Contracts.Ogg
{
    internal interface IForwardOnlyPacketProvider : IPacketProvider
    {
        bool AddPage(byte[] buf, bool isResync);
        void SetEndOfStream();
    }
}
