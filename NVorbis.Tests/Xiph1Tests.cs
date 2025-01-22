using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class Xiph1Tests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_xiph_asset_defs_1();

    [Theory]
    [InlineData("1.0-test.ogg", 0)]
    [InlineData("1.0.1-test.ogg", 0)]
    [InlineData("48k-mono.ogg", 0)]
    [InlineData("beta3-test.ogg", 0)]
    [InlineData("beta4-test.ogg", 1)]
    public void Compare(string asset, uint max_diff) => cmp_output(PrepareAsset(asset), max_diff);
}