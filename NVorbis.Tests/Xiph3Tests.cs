using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class Xiph3Tests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_xiph_asset_defs_3();

    [Theory]
    [InlineData("lsp-test.ogg")]
    [InlineData("lsp-test2.ogg")]
    [InlineData("lsp-test3.ogg")]
    [InlineData("lsp-test4.ogg")]
    [InlineData("mono.ogg")]
    public void Compare(string asset) => cmp_output(PrepareAsset(asset), 0);
}