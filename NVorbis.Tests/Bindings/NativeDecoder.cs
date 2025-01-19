using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis.Tests.Bindings;

public unsafe class NativeDecoder : SafeHandle
{
    private Stream stream;
    private bool leaveOpen;

    private GCHandle selfHandle;
    private OggVorbis_File* vorbis;
    private int current_logical_bitstream;
    private Exception? read_error;

    public NativeDecoder(Stream stream, bool leaveOpen) : base(IntPtr.Zero, true)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.leaveOpen = leaveOpen;

        selfHandle = GCHandle.Alloc(this);
        vorbis = (OggVorbis_File*)NativeMemory.AlignedAlloc(
            (uint)sizeof(OggVorbis_File), (uint)sizeof(nint));

        ov_callbacks callbacks = new()
        {
            read_func = &read_func,
            seek_func = &seek_func,
            tell_func = &tell_func
        };
        
        var result = check_errors(Vorbisfile.ov_open_callbacks(
            (void*)GCHandle.ToIntPtr(selfHandle),
            vorbis,
            null,
            new CLong(0),
            callbacks));
        if (result != NativeResult.Ok)
        {
            throw new Exception(result.ToString());
        }
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        int result = Vorbisfile.ov_clear(vorbis);
        NativeMemory.AlignedFree(vorbis);
        if (!leaveOpen)
        {
            stream.Dispose();
        }
        return result == 0;
    }

    public NativeResult next_packet(Span<short> destination, out NativePacket packet)
    {
        nint result;
        fixed (short* dstPtr = destination)
        fixed (int* bitstreamPtr = &current_logical_bitstream)
        {
            result = Vorbisfile.ov_read(
                vorbis,
                (byte*)dstPtr,
                destination.Length * sizeof(short),
                0,
                2,
                1,
                bitstreamPtr).Value;
        }

        if (result == 0)
        {
            if (read_error != null)
            {
                var error = read_error;
                read_error = null;
                throw error;
            }
        }

        if (result < 0)
        {
            packet = default;
            return check_errors(result);
        }

        var info = Vorbisfile.ov_info(vorbis, current_logical_bitstream);
        packet = new NativePacket()
        {
            Length = (int)(result / sizeof(short)),
            channels = (ushort)info->channels,
            rate = (ulong)info->rate.Value,
            bitrate_upper = (ulong)info->bitrate_upper.Value,
            bitrate_nominal = (ulong)info->bitrate_nominal.Value,
            bitrate_lower = (ulong)info->bitrate_lower.Value,
        };
        return NativeResult.Ok;
    }

    private static NativeResult check_errors(nint code)
    {
        return code switch
        {
            0 => NativeResult.Ok,
            Vorbisfile.OV_ENOTVORBIS => NativeResult.NotVorbis,
            Vorbisfile.OV_EVERSION => NativeResult.VersionMismatch,
            Vorbisfile.OV_EBADHEADER => NativeResult.BadHeader,
            Vorbisfile.OV_EINVAL => NativeResult.InvalidSetup,
            Vorbisfile.OV_HOLE => NativeResult.Hole,
            Vorbisfile.OV_EREAD => NativeResult.Read,
            Vorbisfile.OV_EIMPL => NativeResult.Unimplemented,
            // indicates a bug or heap/stack corruption
            Vorbisfile.OV_EFAULT => throw new Exception("Internal libvorbis error"),
            _ => throw new Exception($"Unknown vorbis error: {code}"),
        };
    }

    private static NativeDecoder? GetSelf(void* datasource)
    {
        return GCHandle.FromIntPtr((IntPtr)datasource).Target as NativeDecoder;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint read_func(void* ptr, nuint size, nuint nmemb, void* datasource)
    {
        var self = GetSelf(datasource);
        if (self == null)
        {
            return 0;
        }

        /*
         * In practice libvorbisfile always sets size to 1.
         * This assumption makes things much simpler
         */
        if (size != 1)
        {
            return 0;
        }

        var buffer = new Span<byte>(ptr, (int)nmemb);
        try
        {
            return (uint)self.stream.ReadAtLeast(buffer, buffer.Length, false);
        }
        catch (Exception ex)
        {
            self.read_error = ex;
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int seek_func(void* datasource, long offset, int whence)
    {
        var self = GetSelf(datasource);
        if (self == null)
        {
            return -1;
        }

        try
        {
            const int SEEK_SET = 0;
            const int SEEK_CUR = 1;
            const int SEEK_END = 2;
            switch (whence)
            {
                case SEEK_SET:
                    self.stream.Seek(offset, SeekOrigin.Begin);
                    break;

                case SEEK_CUR:
                    self.stream.Seek(offset, SeekOrigin.Current);
                    break;

                case SEEK_END:
                    self.stream.Seek(offset, SeekOrigin.End);
                    break;

                default:
                    return -1;
            }
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static CLong tell_func(void* datasource)
    {
        var self = GetSelf(datasource);
        if (self == null)
        {
            return new CLong(-1);
        }

        try
        {
            return new CLong((nint)self.stream.Position);
        }
        catch
        {
            return new CLong(-1);
        }
    }
}

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
}

public struct NativePacket
{
    public int Length;
    public ushort channels;
    public ulong rate;
    public ulong bitrate_upper;
    public ulong bitrate_nominal;
    public ulong bitrate_lower;
    public ulong bitrate_window;
}