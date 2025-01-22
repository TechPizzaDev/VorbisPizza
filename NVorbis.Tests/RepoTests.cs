namespace NVorbis.Tests;

public class RepoTests : AssetTest
{
    [Theory]
    [InlineData("1test.ogg")]
    [InlineData("2test.ogg")]
    [InlineData("3test.ogg")]
    [InlineData("issue6test.ogg")]
    public void Compare(string file) => cmp_output(Path.Join("../../../../TestFiles", file), 0);
}