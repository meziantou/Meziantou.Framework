using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public sealed class TestMarkdownObjectDataExtensions
{
    [Fact]
    public void CanSetAndGetTypedDataUsingTypeKey()
    {
        var block = new ParagraphBlock();

        block.SetData(42);

        Assert.Equal(42, block.GetData<int>());
        Assert.Null(block.GetData<string>());
    }

    [Fact]
    public void CanUseTypedDataKey()
    {
        var block = new ParagraphBlock();
        var key = new DataKey<string>();

        block.SetData<string>(key, "value");

        Assert.Equal("value", block.GetData<string>(key));
        Assert.True(block.TryGetData<string>(key, out var output));
        Assert.Equal("value", output);
    }

    [Fact]
    public void TryGetDataReturnsFalseForTypeMismatch()
    {
        var block = new ParagraphBlock();
        var key = new object();

        block.SetData(key, 123);

        Assert.False(block.TryGetData<string>(key, out var output));
        Assert.Null(output);
    }
}
