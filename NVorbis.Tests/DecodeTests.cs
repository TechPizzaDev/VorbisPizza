using System.Diagnostics;
using System.Security.Cryptography;
using NVorbis.Tests.Bindings;
using Xunit.Abstractions;

namespace NVorbis.Tests;

public class Methods
{
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
            if (native_packet.Length == 0)
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
                provider.FinishPacket(ref packet);
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

    public static (ulong, ulong) cmp_file_output(string file_path)
    {
        var f_1 = File.OpenRead(file_path);
        var f_2 = File.OpenRead(file_path);
        return cmp_output(f_1, f_2, (u, v, _, _) => (u, v));
    }

    public static T cmp_output<T>(
        Stream rdr_1,
        Stream rdr_2,
        Func<ulong, ulong, ulong, bool, T> f)
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

        var buffer_s16 = new short[2048];
        var buffer_f32 = new float[2048];

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
            if (native_packet.Length == 0)
            {
                break;
            }
            native_dec_data.AddRange(buffer_s16.AsSpan(0, native_packet.Length));

            // TODO tell calling code about this condition
            var pck_decompressed_len = ogg_rdr.ReadSamples(buffer_f32) * ogg_rdr.Channels;
            if (pck_decompressed_len == 0)
            {
                break;
            }

            // Asserting some very basic things:
            Assert.Equal(native_packet.rate, (ulong)ogg_rdr.SampleRate);
            Assert.Equal(native_packet.channels, (ushort)ogg_rdr.Channels);

            total_sample_count += (ulong)pck_decompressed_len;
            if (stream_serial != ogg_rdr.StreamSerial)
            {
                // Chained ogg file
                chained_ogg_file = true;
            }

            // Fill dec_data with stuff from this packet
            var packet_f32 = buffer_f32.AsSpan(0, pck_decompressed_len);
            var packet_s16 = buffer_s16.AsSpan(0, pck_decompressed_len);
            ConvertFloatToShort(packet_f32, packet_s16);
            dec_data.AddRange(packet_s16);

            var diffs = 0;
            foreach (var (a, b) in native_dec_data.Zip(dec_data))
            {
                var diff = b - a;
                // +- 2 deviation is allowed.
                if (Math.Abs(diff) > 2)
                {
                    diffs += 1;
                }
            }

            var native_dec_len = native_dec_data.Count;
            var dec_len = dec_data.Count;

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
                native_dec_data.RemoveRange(0, Math.Min(native_dec_len, dec_len));
                dec_data.RemoveRange(0, Math.Min(native_dec_len, dec_len));
            }
            else
            {
                native_dec_data.Clear();
                dec_data.Clear();
            }
        }
        return f(pcks_with_diffs, n, total_sample_count, chained_ogg_file);
    }

    private static void ConvertFloatToShort(ReadOnlySpan<float> src, Span<short> dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            var val = src[i] * 32768f;
            if (val > 32767)
                val = 32767;
            else if (val < -32768)
                val = -32768;
            dst[i] = (short)val;
        }
    }

    /// Ensures that a file is malformed and returns an error,
    /// but doesn't panic or crash or anything of the like
    public static InvalidDataException? ensure_malformed(string name)
    {
        try
        {
            using var ogg_rdr = new VorbisReader(File.OpenRead($"test-assets/{name}"), false);
            ogg_rdr.Initialize();

            Span<float> buffer = stackalloc float[1024];
            while (ogg_rdr.ReadSamples(buffer) > 0)
            {
            }

            Assert.Fail($"File decoded without errors");
        }
        catch (InvalidDataException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected error: {ex}");
        }
        return null;
    }

    /// Ensures that a file decodes without errors
    public static void ensure_okay(string name)
    {
        using var ogg_rdr = new VorbisReader(File.OpenRead($"test-assets/{name}"), false);
        ogg_rdr.Initialize();

        Span<float> buffer = stackalloc float[1024];
        while (ogg_rdr.ReadSamples(buffer) > 0)
        {
        }
    }
}

public class DecodeTests(ITestOutputHelper output)
{
    void cmp_output(string name, ulong max_diff)
    {
        output.WriteLine("Comparing output for {0}", name);
        var (diff_pck_count, _) = Methods.cmp_file_output($"test-assets/{name}");
        output.WriteLine(": {0} differing packets of allowed {1}.", diff_pck_count, max_diff);
        Assert.True(diff_pck_count <= max_diff);
    }

    void ensure_malformed(string name)
    {
        var ex = Methods.ensure_malformed(name);
        if (ex != null)
        {
            output.WriteLine(ex.ToString());
        }
    }

    [Fact]
    public void test_vals()
    {
        download_test_files(TestAssets.get_asset_defs(), "test-assets", true);

        cmp_output("bwv_1043_vivace.ogg", 0);
        cmp_output("bwv_543_fuge.ogg", 0);
        cmp_output("maple_leaf_rag.ogg", 0);
        cmp_output("hoelle_rache.ogg", 0);
        cmp_output("thingy-floor0.ogg", 0);
        cmp_output("audio_simple_err.ogg", 0);
    }

    [Fact]
    public void test_libnogg_vals()
    {
        download_test_files(TestAssets.get_libnogg_asset_defs(), "test-assets", true);

        cmp_output("6-mode-bits-multipage.ogg", 2);
        cmp_output("6-mode-bits.ogg", 2);
        cmp_output("6ch-all-page-types.ogg", 0);
        cmp_output("6ch-long-first-packet.ogg", 0);
        cmp_output("6ch-moving-sine-floor0.ogg", 0);
        cmp_output("6ch-moving-sine.ogg", 0);
        // NOTE: The bad-continued-packet-flag.ogg test is
        // actually supposed to return an error in libnogg.
        // However, libvorbis doesn't, nor does lewton.
        // Given a (slightly) erroneous ogg file where there
        // are audio packets following the last header packet,
        // we follow libvorbis behaviour and simply ignore those packets.
        // Apparently the test case has been created in a way
        // where this behaviour doesn't evoke an error from lewton.
        cmp_output("bad-continued-packet-flag.ogg", 0);
        cmp_output("bitrate-123.ogg", 0);
        cmp_output("bitrate-456-0.ogg", 0);
        cmp_output("bitrate-456-789.ogg", 0);
        cmp_output("empty-page.ogg", 0);
        cmp_output("large-pages.ogg", 2);
        cmp_output("long-short.ogg", 2);
        cmp_output("noise-6ch.ogg", 0);
        cmp_output("noise-stereo.ogg", 0);
        cmp_output("partial-granule-position.ogg", 2);
//#[cfg(not(target_os = "windows"))]
        cmp_output("sample-rate-max.ogg", 0);
        ensure_malformed("single-code-2bits.ogg" /*, BadHeader(HeaderBadFormat)*/);
        // We can't cmp the output here because native
        // libvorbis doesn't accept the file as valid
        Methods.ensure_okay("single-code-nonsparse.ogg");
        Methods.ensure_okay("single-code-ordered.ogg");
        cmp_output("single-code-sparse.ogg", 0);
//#[cfg(not(target_os = "macos"))]
        cmp_output("sketch008-floor0.ogg", 0);
        cmp_output("sketch008.ogg", 0);
        cmp_output("sketch039.ogg", 0);
        cmp_output("split-packet.ogg", 2);
        cmp_output("square-interleaved.ogg", 0);
        cmp_output("square-multipage.ogg", 0);
        cmp_output("square-stereo.ogg", 0);
        // This is really more an issue of the ogg crate,
        // if it's an issue at all.
        // https://github.com/RustAudio/ogg/issues/7
        //ensure_malformed!("square-with-junk.ogg", OggError(NoCapturePatternFound));
        cmp_output("square.ogg", 0);
        cmp_output("thingy.ogg", 0);
        cmp_output("zero-length.ogg", 0);
    }

    [Fact]
    public void test_xiph_vals_1()
    {
        download_test_files(TestAssets.get_xiph_asset_defs_1(), "test-assets", true);

        cmp_output("1.0-test.ogg", 0);
        cmp_output("1.0.1-test.ogg", 0);
        cmp_output("48k-mono.ogg", 0);
        cmp_output("beta3-test.ogg", 0);
        cmp_output("beta4-test.ogg", 1);
    }

    [Fact]
    public void test_xiph_vals_2()
    {
        download_test_files(TestAssets.get_xiph_asset_defs_2(), "test-assets", true);

        cmp_output("bimS-silence.ogg", 0);
        cmp_output("chain-test1.ogg", 0);
        cmp_output("chain-test2.ogg", 0);
        cmp_output("chain-test3.ogg", 1);
        cmp_output("highrate-test.ogg", 0);
    }

    [Fact]
    public void test_xiph_vals_3()
    {
        download_test_files(TestAssets.get_xiph_asset_defs_3(), "test-assets", true);

        cmp_output("lsp-test.ogg", 0);
        cmp_output("lsp-test2.ogg", 0);
        cmp_output("lsp-test3.ogg", 0);
        cmp_output("lsp-test4.ogg", 0);
        cmp_output("mono.ogg", 0);
    }

    [Fact]
    public void test_xiph_vals_4()
    {
        download_test_files(TestAssets.get_xiph_asset_defs_4(), "test-assets", true);

        cmp_output("moog.ogg", 0);
        cmp_output("one-entry-codebook-test.ogg", 0);
        cmp_output("out-of-spec-blocksize.ogg", 0);
        cmp_output("rc1-test.ogg", 0);
        cmp_output("rc2-test.ogg", 0);
        cmp_output("rc2-test2.ogg", 0);
        cmp_output("rc3-test.ogg", 0);
    }

    [Fact]
    public void test_xiph_vals_5()
    {
        download_test_files(TestAssets.get_xiph_asset_defs_5(), "test-assets", true);

        cmp_output("singlemap-test.ogg", 0);
//#[cfg(not(target_os = "macos"))]
        cmp_output("sleepzor.ogg", 0);
        cmp_output("test-short.ogg", 0);
        cmp_output("test-short2.ogg", 0);
        // Contains an out of bounds mode index
        ensure_malformed("unused-mode-test.ogg" /*, BadAudio(AudioBadFormat)*/);
    }

    private void download_test_files(IEnumerable<TestAssetDef> assets, string dir, bool verbose)
    {
        Directory.CreateDirectory(dir);

        HttpClient client = new();

        foreach (var asset in assets)
        {
            using var file = new FileStream(Path.Join(dir, asset.filename), FileMode.OpenOrCreate);
            if (file.Length > 0)
            {
                output.WriteLine($"Skipping file download {asset.filename} ...");
                continue;
            }
            
            if (verbose)
            {
                output.WriteLine($"Fetching file {asset.filename} ...");
            }
            download_test_file(client, asset, file);
        }
    }

    private Sha256 download_test_file(
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