namespace NVorbis.Tests.Bindings;

public struct NativePacket
{
    public int samples;
    public ushort channels;
    public ulong rate;
    public ulong bitrate_upper;
    public ulong bitrate_nominal;
    public ulong bitrate_lower;
    public ulong bitrate_window;
}