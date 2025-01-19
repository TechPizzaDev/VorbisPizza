using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis.Tests.Bindings;

using c_int = int;

public static unsafe class Vorbisfile
{
    public const int OV_FALSE = -1;
    public const int OV_EOF = -2;
    public const int OV_HOLE = -3;
    public const int OV_EREAD = -128;
    public const int OV_EFAULT = -129;
    public const int OV_EIMPL = -130;
    public const int OV_EINVAL = -131;
    public const int OV_ENOTVORBIS = -132;
    public const int OV_EBADHEADER = -133;
    public const int OV_EVERSION = -134;
    public const int OV_ENOTAUDIO = -135;
    public const int OV_EBADPACKET = -136;
    public const int OV_EBADLINK = -137;
    public const int OV_ENOSEEK = -138;
    
    public const uint OV_ECTL_RATEMANAGE2_GET = 20;
    public const uint OV_ECTL_RATEMANAGE2_SET = 21;
    public const uint OV_ECTL_LOWPASS_GET = 32;
    public const uint OV_ECTL_LOWPASS_SET = 33;
    public const uint OV_ECTL_IBLOCK_GET = 48;
    public const uint OV_ECTL_IBLOCK_SET = 49;
    public const uint OV_ECTL_COUPLING_GET = 64;
    public const uint OV_ECTL_COUPLING_SET = 65;
    public const uint OV_ECTL_RATEMANAGE_GET = 16;
    public const uint OV_ECTL_RATEMANAGE_SET = 17;
    public const uint OV_ECTL_RATEMANAGE_AVG = 18;
    public const uint OV_ECTL_RATEMANAGE_HARD = 19;
    public const uint NOTOPEN = 0;
    public const uint PARTOPEN = 1;
    public const uint OPENED = 2;
    public const uint STREAMSET = 3;
    public const uint INITSET = 4;
        
    private const string LibName = "libvorbisfile";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern c_int ov_open_callbacks(
        void* datasource,
        OggVorbis_File* vf,
        byte* initial,
        CLong ibytes,
        ov_callbacks callbacks);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern vorbis_info* ov_info(OggVorbis_File* vf, c_int link);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern vorbis_comment* ov_comment(OggVorbis_File* vf, c_int link);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double ov_time_total(OggVorbis_File* vf, c_int i);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long ov_pcm_total(OggVorbis_File* vf, c_int i);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern CLong ov_read(
        OggVorbis_File* vf,
        byte* buffer,
        c_int length,
        c_int bigendianp,
        c_int word,
        c_int sgned,
        c_int* bitstream);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern CLong ov_read_float(
        OggVorbis_File* vf,
        float** pcm_channels,
        c_int samples,
        c_int* bitstream);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern c_int ov_time_seek(OggVorbis_File* vf, double s);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern c_int ov_pcm_seek(OggVorbis_File* vf, long s);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double ov_time_tell(OggVorbis_File* vf);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long ov_pcm_tell(OggVorbis_File* vf);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern c_int ov_clear(OggVorbis_File* vf);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern CLong ov_streams(OggVorbis_File* vf);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern CLong ov_seekable(OggVorbis_File* vf);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern c_int ov_raw_seek(OggVorbis_File* vf, long pos);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long ov_raw_tell(OggVorbis_File* vf);
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct OggVorbis_File
{
    public void* datasource;
    public c_int seekable;
    public long offset;
    public long end;
    public ogg_sync_state oy;
    public c_int links;
    public long* offsets;
    public long* dataoffsets;
    public CLong* serialnos;
    public long* pcmlengths;
    public vorbis_info* vi;
    public vorbis_comment* vc;
    public long pcm_offset;
    public c_int ready_state;
    public CLong current_serialno;
    public c_int current_link;
    public double bittrack;
    public double samptrack;
    public ogg_stream_state os;
    public vorbis_dsp_state vd;
    public vorbis_block vb;
    public ov_callbacks callbacks;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct vorbis_info
{
    public c_int version;
    public c_int channels;
    public CLong rate;
    public CLong bitrate_upper;
    public CLong bitrate_nominal;
    public CLong bitrate_lower;
    public CLong bitrate_window;
    public void* codec_setup;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct vorbis_comment
{
    public byte** user_comments;
    public c_int* comment_lengths;
    public c_int comments;
    public byte* vendor;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ov_callbacks
{
    public delegate* unmanaged[Cdecl]<
        /*ptr:*/ void*,
        /*size:*/ nuint,
        /*nmemb:*/ nuint,
        /*datasource:*/ void*,
        nuint> read_func;

    public delegate* unmanaged[Cdecl]<
        /*datasource:*/ void*,
        /*offset:*/ long,
        /*whence:*/ c_int,
        c_int> seek_func;

    public delegate* unmanaged[Cdecl]<
        /*datasource:*/ void*,
        c_int> close_func;

    public delegate* unmanaged[Cdecl]<
        /*datasource:*/ void*,
        CLong> tell_func;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct vorbis_dsp_state
{
    public c_int analysisp;
    public vorbis_info* vi;
    public float** pcm;
    public float** pcmret;
    public float* preextrapolate_work;
    public c_int pcm_storage;
    public c_int pcm_current;
    public c_int pcm_returned;
    public c_int preextrapolate;
    public c_int eofflag;
    public CLong lW;
    public CLong W;
    public CLong nW;
    public CLong centerW;
    public long granulepos;
    public long sequence;
    public long glue_bits;
    public long time_bits;
    public long floor_bits;
    public long res_bits;
    public void* backend_state;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct oggpack_buffer
{
    public CLong endbyte;
    public c_int endbit;
    public byte* buffer;
    public byte* ptr;
    public CLong storage;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct alloc_chain
{
    public void* ptr;
    public alloc_chain* next;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct vorbis_block
{
    public float** pcm;
    public oggpack_buffer opb;
    public CLong lW;
    public CLong W;
    public CLong nW;
    public c_int pcmend;
    public c_int mode;
    public c_int eofflag;
    public long granulepos;
    public long sequence;
    public vorbis_dsp_state* vd;
    public void* localstore;
    public CLong localtop;
    public CLong localalloc;
    public CLong totaluse;
    public alloc_chain* reap;
    public CLong glue_bits;
    public CLong time_bits;
    public CLong floor_bits;
    public CLong res_bits;
    public void* @internal;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ogg_sync_state
{
    public byte* data;
    public c_int storage;
    public c_int fill;
    public c_int returned;
    public c_int unsynced;
    public c_int headerbytes;
    public c_int bodybytes;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ogg_stream_state
{
    public byte* body_data;
    public CLong body_storage;
    public CLong body_fill;
    public CLong body_returned;
    public c_int* lacing_vals;
    public long* granule_vals;
    public CLong lacing_storage;
    public CLong lacing_fill;
    public CLong lacing_packet;
    public CLong lacing_returned;
    public Header header;
    public c_int header_fill;
    public c_int e_o_s;
    public c_int b_o_s;
    public CLong serialno;
    public CLong pageno;
    public long packetno;
    public long granulepos;

    [InlineArray(282)]
    public struct Header
    {
        private byte _e0;
    }
}