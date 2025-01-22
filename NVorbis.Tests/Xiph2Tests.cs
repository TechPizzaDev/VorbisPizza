using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class Xiph2Tests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_xiph_asset_defs_2();

    [Theory]
    [InlineData("bimS-silence.ogg", 0)]
    [InlineData("chain-test1.ogg", 0)]
    [InlineData("chain-test2.ogg", 0)]
    [InlineData("chain-test3.ogg", 1)]
    [InlineData("highrate-test.ogg", 0)]
    public void Compare(string asset, uint max_diff) => cmp_output(PrepareAsset(asset), max_diff);
}