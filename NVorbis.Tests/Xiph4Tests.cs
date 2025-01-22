using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class Xiph4Tests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_xiph_asset_defs_4();

    [Theory]
    [InlineData("moog.ogg")]
    [InlineData("one-entry-codebook-test.ogg")]
    [InlineData("out-of-spec-blocksize.ogg")]
    [InlineData("rc1-test.ogg")]
    [InlineData("rc2-test.ogg")]
    [InlineData("rc2-test2.ogg")]
    [InlineData("rc3-test.ogg")]
    public void Compare(string asset) => cmp_output(PrepareAsset(asset), 0);
}