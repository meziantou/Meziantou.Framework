namespace Meziantou.Framework.Templating.Tests;

public class EmailTemplateTest
{
    [Fact]
    public void EmailTemplate_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{# \"Meziantou\" }}!");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
        Assert.NotNull(metadata);
        Assert.Null(metadata.Title);
    }

    [Fact]
    public void EmailTemplate_Section_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}{{# \"Meziantou\" }}{{@end_section}}!");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
        Assert.NotNull(metadata);
        Assert.Equal("Meziantou", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_HtmlEncode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{#html \"<Meziantou>\" }}!");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("Hello &lt;Meziantou&gt;!", result);
    }

    [Fact]
    public void EmailTemplate_UrlEncode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("Hello <a href=\"http://www.localhost.com/Sample%26Url\">Meziantou</a>!", result);
    }

    [Fact]
    public void EmailTemplate_HtmlAttributeEncode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!", result);
    }

    [Fact]
    public void EmailTemplate_HtmlCode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("0 1 2 ", result);
    }

    [Fact]
    public void EmailTemplate_Cid_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("<img src=\"{{cid test1.png}}\" /><img src=\"{{cid test2.png}}\" />");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("<img src=\"cid:test1.png\" /><img src=\"cid:test2.png\" />", result);
        Assert.NotNull(metadata);
        Assert.NotNull(metadata.ContentIdentifiers);
        Assert.Collection(metadata.ContentIdentifiers,
             item => Assert.Equal("test1.png", item),
             item => Assert.Equal("test2.png", item));
    }
    [Fact]
    public void EmailTemplate_Section_CapturesPlainText()
    {
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}Plain text{{@end_section}}!");

        var result = template.Run(out var metadata);
        Assert.Equal("Hello Plain text!", result);
        Assert.NotNull(metadata);
        Assert.Equal("Plain text", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_Section_CapturesExpressionValue()
    {
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}{{= 42 }}{{@end_section}}!");

        var result = template.Run(out var metadata);
        Assert.Equal("Hello 42!", result);
        Assert.NotNull(metadata);
        Assert.Equal("42", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_UnclosedSection_Throws()
    {
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}Subject");

        var exception = Assert.Throws<TemplateException>(() => template.Run(out _));

        Assert.Contains("end_section", exception.Message, ignoreCase: false);
        Assert.Contains("'title'", exception.Message, ignoreCase: false);
    }

    [Fact]
    public void EmailTemplate_SeveralUnclosedSections_AreAllReported()
    {
        var template = new HtmlEmailTemplate();
        template.Load("{{@begin_section title}}a{{@begin_section footer}}b");

        var exception = Assert.Throws<TemplateException>(() => template.Run(out _));

        Assert.Contains("'title'", exception.Message, ignoreCase: false);
        Assert.Contains("'footer'", exception.Message, ignoreCase: false);
    }

    [Fact]
    public void EmailTemplate_UnclosedSection_ThrowsFromInheritedRunOverloadsToo()
    {
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}Subject");

        Assert.Throws<TemplateException>(() => ((Template)template).Run());
    }

    [Fact]
    public void EmailTemplate_NestedSectionsWithTheSameName_KeepTheOutermostContent()
    {
        var template = new HtmlEmailTemplate();
        template.Load("{{@begin_section title}}A{{@begin_section title}}B{{@end_section}}C{{@end_section}}");

        var result = template.Run(out var metadata);

        Assert.Equal("ABC", result);
        Assert.NotNull(metadata);
        Assert.Equal("ABC", metadata.Title);
    }
}
