namespace Meziantou.Framework.Tests;

public sealed class UnicodeTests
{
    [Fact]
    public void ReplaceConfusablesCharacters_ReplacesMappedCharacters()
    {
        var input = "\u0410\u0430\u03B1";

        var output = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Equal("Aaa", output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_NoChange()
    {
        var input = "Hello";

        var output = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Same(input, output);
    }
}
