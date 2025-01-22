using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class CustomTests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_asset_defs();

    [Theory]
    [InlineData("bwv_1043_vivace.ogg")]
    [InlineData("bwv_543_fuge.ogg")]
    [InlineData("maple_leaf_rag.ogg")]
    [InlineData("hoelle_rache.ogg")]
    [InlineData("thingy-floor0.ogg")]
    [InlineData("audio_simple_err.ogg")]
    public void Compare(string asset) => cmp_output(PrepareAsset(asset), 0);
}