using System.Diagnostics;
using System.Runtime.InteropServices;
using NVorbis.Tests.Bindings;
using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public abstract class AssetTest
{
    public static HttpClient Http { get; } = new();

    public virtual string GetAssetDir() => "test-assets";

    public virtual IEnumerable<TestAssetDef> GetAssetDefs() => [];

    public (TimeSpan, TimeSpan, ulong) cmp_perf(string file_path)
    {
        // Read the file to memory to create fairer playing ground
        var file_buf = File.ReadAllBytes(file_path);

        var start_native_decode = Stopwatch.GetTimestamp();
        var native_dec = new NativeDecoder(new MemoryStream(file_buf), false);
        var buffer_s16 = new short[2048];
        while (true)
        {
            var native_result = native_dec.next_packet(buffer_s16, out var native_packet);
            if (native_result != NativeResult.Ok)
            {
                throw new Exception(native_result.ToString());
            }
            if (native_packet.samples == 0)
            {
                break;
            }
        }
        var native_decode_duration = Stopwatch.GetElapsedTime(start_native_decode);

        ulong n = 0;
        var start_decode = Stopwatch.GetTimestamp();
        using var ogg_rdr = new Ogg.ContainerReader(
            VorbisConfig.Default, new MemoryStream(file_buf), false);
        ogg_rdr.NewStreamCallback += provider =>
        {
            do
            {
                var packet = provider.GetNextPacket();
                if (!packet.IsValid)
                {
                    break;
                }
                n += 1;
                packet.Finish();
            }
            while (true);
            return true;
        };
        while (ogg_rdr.FindNextStream())
        {
        }

        var decode_duration = Stopwatch.GetElapsedTime(start_decode);
        return (decode_duration, native_decode_duration, n);
    }

    public static (ulong pck_diff_cnt, ulong pck_cnt, ulong sample_cnt, bool chained) cmp_file_output(string file_path)
    {
        var f_1 = File.OpenRead(file_path);
        var f_2 = File.OpenRead(file_path);
        return cmp_output(f_1, f_2);
    }

    public static (ulong pck_diff_cnt, ulong pck_cnt, ulong sample_cnt, bool chained) cmp_output(
        Stream rdr_1, Stream rdr_2)
    {
        using var native_dec = new NativeDecoder(rdr_1, false);

        using var ogg_rdr = new VorbisReader(rdr_2, false);
        ogg_rdr.Initialize();

        var stream_serial = ogg_rdr.StreamSerial;

        // Now the fun starts...
        ulong n = 0;

        ulong pcks_with_diffs = 0;

        // This parameter is useful when we only want to check whether the
        // actually returned data are the same, regardless of where the
        // two implementations put packet borders.
        // Of course, when debugging bugs which modify the size of packets
        // you usually want to set this flag to false so that you don't
        // suffer from the "carry over" effect of errors.
        bool ignore_packet_borders = true;

        var native_dec_data = new List<short>();
        var dec_data = new List<short>();

        int stride = 2048;
        var buffer_f32 = new float[stride * 8];
        var buffer_s16 = new short[stride * 8];

        ulong total_sample_count = 0;

        bool chained_ogg_file = false;
        while (true)
        {
            n += 1;

            var native_result = native_dec.next_packet(buffer_s16, out var native_packet);
            if (native_result != NativeResult.Ok)
            {
                throw new Exception(native_result.ToString());
            }
            if (native_packet.samples == 0)
            {
                break;
            }

            native_dec_data.AddRange(buffer_s16.AsSpan(0, native_packet.samples * native_packet.channels));

            var samples_read = ogg_rdr.ReadSamples(buffer_f32);
            if (samples_read == 0)
            {
                // TODO tell calling code about this condition
                break;
            }

            var buffer_span = buffer_f32.AsSpan(0, samples_read * ogg_rdr.Channels);
            for (int i = 0; i < buffer_span.Length; i++)
            {
                int v = (int)(buffer_span[i] * 32768f);
                dec_data.Add((short)Math.Min(Math.Max(short.MinValue, v), short.MaxValue));
            }

            // Asserting some very basic things:
            Assert.Equal(native_packet.rate, (uint)ogg_rdr.SampleRate);
            Assert.Equal(native_packet.channels, (ushort)ogg_rdr.Channels);

            total_sample_count += (ulong)samples_read * (uint)ogg_rdr.Channels;
            if (stream_serial != ogg_rdr.StreamSerial)
            {
                // Chained ogg file
                chained_ogg_file = true;
            }

            var native_dec_span = CollectionsMarshal.AsSpan(native_dec_data);
            var dec_span = CollectionsMarshal.AsSpan(dec_data);

            int native_dec_len = native_dec_span.Length;
            int dec_len = dec_span.Length;
            int consume_count = Math.Min(native_dec_len, dec_len);

            long diffs = 0;
            for (int i = 0; i < consume_count; i++)
            {
                short a = native_dec_span[i];
                short b = dec_span[i];
                int diff = b - a;

                // Some deviation is allowed.
                if (Math.Abs(diff) > 2)
                {
                    diffs += 1;
                }
            }

            if (diffs > 0 || (!ignore_packet_borders && dec_len != native_dec_len))
            {
                /*
                print!("Differences found in packet no {}... ", n);
                print!("ours={} native={}", dec_len, native_dec_len);
                println!(" (diffs {})", diffs);
                */
                pcks_with_diffs += 1;
            }

            if (ignore_packet_borders)
            {
                native_dec_data.RemoveRange(0, consume_count);
                dec_data.RemoveRange(0, consume_count);
            }
            else
            {
                native_dec_data.Clear();
                dec_data.Clear();
            }
        }
        return (pcks_with_diffs, n, total_sample_count, chained_ogg_file);
    }

    public void cmp_output(string filePath, ulong max_diff)
    {
        var (pck_diff_cnt, pck_cnt, _, _) = cmp_file_output(filePath);
        Assert.True(
            pck_diff_cnt <= max_diff,
            $"{pck_diff_cnt} differing packets of allowed {max_diff}. {pck_cnt} packets in total.");
    }

    /// Ensures that a file is malformed and returns an error,
    /// but doesn't panic or crash or anything of the like
    public static void ensure_malformed(string filePath)
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            using var ogg_rdr = new VorbisReader(File.OpenRead(filePath), false);
            ogg_rdr.Initialize();

            Span<float> buffer = stackalloc float[1024];
            while (ogg_rdr.ReadSamples(buffer) > 0)
            {
            }
        });
    }

    /// Ensures that a file decodes without errors
    public static void ensure_okay(string filePath)
    {
        using var ogg_rdr = new VorbisReader(File.OpenRead(filePath), false);
        ogg_rdr.Initialize();

        Span<float> buffer = stackalloc float[1024];
        while (ogg_rdr.ReadSamples(buffer) > 0)
        {
        }
    }

    protected string PrepareAsset(string name)
    {
        Directory.CreateDirectory(GetAssetDir());

        string path = Path.Join(GetAssetDir(), name);
        using var file = new FileStream(path, FileMode.OpenOrCreate);
        if (file.Length > 0)
        {
            return path;
        }

        TestAssetDef asset = GetAssetDefs().Single(def => def.filename == name);
        download_test_file(Http, asset, file);
        return path;
    }

    private static Sha256 download_test_file(
        HttpClient client,
        TestAssetDef asset,
        FileStream file)
    {
        var response = client.Send(new HttpRequestMessage(HttpMethod.Get, asset.url));
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpIOException(HttpRequestError.InvalidResponse);
        }

        response.Content.CopyTo(file, null, CancellationToken.None);

        // TODO:
        //file.Seek(0, SeekOrigin.Begin);
        //var hash = SHA256.HashData(file);

        return new Sha256();
    }
}
