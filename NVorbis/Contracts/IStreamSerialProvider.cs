namespace NVorbis.Contracts;

public interface IStreamSerialProvider
{
    /// <summary>
    /// Gets the serial number of this provider's data stream.
    /// </summary>
    int StreamSerial { get; }
}