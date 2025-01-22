
using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class OggTests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_libnogg_asset_defs();

    public class CompareData : TheoryData<string, uint>
    {
        public CompareData()
        {
            Add("6-mode-bits-multipage.ogg", 2);
            Add("6-mode-bits.ogg", 2);

            // TODO: decoding 6 channels is completely broken
            Add("6ch-all-page-types.ogg", 0);
            Add("6ch-long-first-packet.ogg", 0);
            Add("6ch-moving-sine-floor0.ogg", 0);
            Add("6ch-moving-sine.ogg", 0);

            // NOTE: The bad-continued-packet-flag.ogg test is
            // actually supposed to return an error in libnogg.
            // However, libvorbis doesn't, nor does lewton.
            // Given a (slightly) erroneous ogg file where there
            // are audio packets following the last header packet,
            // we follow libvorbis behaviour and simply ignore those packets.
            // Apparently the test case has been created in a way
            // where this behaviour doesn't evoke an error from lewton.
            Add("bad-continued-packet-flag.ogg", 0);

            Add("bitrate-123.ogg", 0);
            Add("bitrate-456-0.ogg", 0);
            Add("bitrate-456-789.ogg", 0);

            // TODO: don't throw on empty page?
            Add("empty-page.ogg", 0);

            Add("large-pages.ogg", 2);
            Add("long-short.ogg", 2);
            Add("noise-6ch.ogg", 0); // TODO: 6 channels
            Add("noise-stereo.ogg", 0);
            Add("partial-granule-position.ogg", 2);

            if (!OperatingSystem.IsWindows())
            {
                Add("sample-rate-max.ogg", 0);
            }

            // We can't cmp the output here because native
            // libvorbis doesn't accept the file as valid
            Add("single-code-sparse.ogg", 0);

            if (!OperatingSystem.IsMacOS())
            {
                Add("sketch008-floor0.ogg", 0);
            }

            Add("sketch008.ogg", 0);
            Add("sketch039.ogg", 0);
            Add("split-packet.ogg", 2);
            Add("square-interleaved.ogg", 0);
            Add("square-multipage.ogg", 0);
            Add("square-stereo.ogg", 0);

            // This is really more an issue of the ogg crate,
            // if it's an issue at all.
            // https://github.com/RustAudio/ogg/issues/7
            Add("square.ogg", 0);

            Add("thingy.ogg", 0);
            Add("zero-length.ogg", 0);
        }
    }

    [Theory, ClassData(typeof(CompareData))]
    public void Compare(string asset, uint max_diff) => cmp_output(PrepareAsset(asset), max_diff);

    [Theory]
    [InlineData("single-code-nonsparse.ogg")]
    [InlineData("single-code-ordered.ogg")]
    public void Okay(string asset) => ensure_okay(PrepareAsset(asset));

    [Theory]
    [InlineData("single-code-2bits.ogg")]
    [InlineData("square-with-junk.ogg")]
    public void Malformed(string asset) => ensure_malformed(PrepareAsset(asset));

}