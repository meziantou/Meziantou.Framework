using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("a", "a")]
    [InlineData("z", "z")]
    [InlineData("A", "A")]
    [InlineData("Z", "Z")]
    [InlineData("0", "0")]
    [InlineData("9", "9")]
    [InlineData("test", "test")]
    [InlineData("TeSt", "TeSt")]
    [InlineData("teste\u0301", "teste")]
    [InlineData("TeSt test", "TeSt-test")]
    [InlineData("TeSt test ", "TeSt-test")]
    [InlineData("TeSt:test ", "TeSt-test")]
    public void Slug_WithDefaultOptions(string text, string expected)
    {
        var slug = Slug.Create(text);

        slug.Should().Be(expected);
    }

    [Theory]
    [InlineData("test", "test")]
    [InlineData("TeSt", "test")]
    public void Slug_Lowercase(string text, string expected)
    {
        var options = new SlugOptions
        {
            CasingTransformation = CasingTransformation.ToLowerCase,
        };
        var slug = Slug.Create(text, options);

        slug.Should().Be(expected);
    }
}
