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

        // Assert
        result.Should().Be("Hello Meziantou!");
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

        // Assert
        result.Should().Be("Hello Meziantou!");
        metadata.Title.Should().Be("Meziantou");
    }

    [Fact]
    public void EmailTemplate_HtmlEncode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello {{#html \"<Meziantou>\" }}!");

        // Act 
        var result = template.Run(out _);

        // Assert
        result.Should().Be("Hello &lt;Meziantou&gt;!");
    }

    [Fact]
    public void EmailTemplate_UrlEncode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");

        // Act 
        var result = template.Run(out _);

        // Assert
        result.Should().Be("Hello <a href=\"http://www.localhost.com/Sample%26Url\">Meziantou</a>!");
    }

    [Fact]
    public void EmailTemplate_HtmlAttributeEncode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

        // Act 
        var result = template.Run(out _);

        // Assert
        result.Should().Be("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!");
    }

    [Fact]
    public void EmailTemplate_HtmlCode_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

        // Act 
        var result = template.Run(out _);

        // Assert
        result.Should().Be("0 1 2 ");
    }

    [Fact]
    public void EmailTemplate_Cid_01()
    {
        // Arrange
        var template = new HtmlEmailTemplate();
        template.Load("<img src=\"{{cid test1.png}}\" /><img src=\"{{cid test2.png}}\" />");

        // Act 
        var result = template.Run(out var metadata);

        // Assert
        result.Should().Be("<img src=\"cid:test1.png\" /><img src=\"cid:test2.png\" />");
        metadata.ContentIdentifiers.Should().SatisfyRespectively(
            item => item.Should().Be("test1.png"),
            item => item.Should().Be("test2.png"));
    }
}
