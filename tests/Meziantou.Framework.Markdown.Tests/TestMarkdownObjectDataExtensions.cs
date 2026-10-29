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

    [Fact]
    public void DocumentationExample_SetData()
    {
        #region MarkdownObjectDataExtensions_TypedKey
        var sectionNumberKey = new DataKey<int>();

        var document = MarkdownConverter.Parse("# Install\n\nText\n\n# Configure");
        var number = 0;
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            number++;
            heading.SetData(sectionNumberKey, number);
        }

        var configure = document.Descendants<HeadingBlock>().Last();
        Assert.Equal(2, configure.GetData(sectionNumberKey));
        Assert.False(document[1].TryGetData(sectionNumberKey, out _)); // Nothing on the paragraph
        #endregion
    }
}
