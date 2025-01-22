using NVorbis.Tests.Utils;

namespace NVorbis.Tests;

public class Xiph5Tests : AssetTest
{
    public override IEnumerable<TestAssetDef> GetAssetDefs() => TestAssets.get_xiph_asset_defs_5();

    public class CompareData : TheoryData<string>
    {
        public CompareData()
        {
            Add("singlemap-test.ogg");
            if (!OperatingSystem.IsMacOS())
            {
                Add("sleepzor.ogg");
            }
            Add("test-short.ogg");
            Add("test-short2.ogg");
        }
    }

    [Theory, ClassData(typeof(CompareData))]
    public void Compare(string asset) => cmp_output(PrepareAsset(asset), 0);

    [Theory]
    // Contains an out of bounds mode index
    [InlineData("unused-mode-test.ogg" /*, BadAudio(AudioBadFormat)*/)]
    public void Malformed(string asset) => ensure_malformed(PrepareAsset(asset));
}