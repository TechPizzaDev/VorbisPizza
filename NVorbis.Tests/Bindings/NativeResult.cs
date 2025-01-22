namespace NVorbis.Tests.Bindings;

public enum NativeResult
{
    Ok,
    NotVorbis,
    VersionMismatch,
    BadHeader,
    InvalidSetup,
    Hole,
    Read,
    Unimplemented,
    NoInfo,
}
