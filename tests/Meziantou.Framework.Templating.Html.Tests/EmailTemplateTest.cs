using FluentAssertions;
using Xunit;

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
        metadata.Title.Should().BeNull();
    }

    [Fact]
    public void EmailTemplate_Section_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin section title}}{{# \"Meziantou\" }}{{@end section}}!");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
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
        metadata.ContentIdentifiers.Should().SatisfyRespectively(
            item => Assert.Equal("test1.png", item),
            item => Assert.Equal("test2.png", item));
    }
}
