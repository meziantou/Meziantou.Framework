using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The encoding declared by a <c>meta</c> element (https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding).</summary>
public sealed class EncodingDeclarationTests
{
    private static readonly string Padding = "<!--" + new string(' ', 1024) + "-->";

    // The byte 0xE6 is U+0436 in windows-1251 and U+00E6 in windows-1252
    private const string Byte = "\U000000E6";
    private const string Windows1251Character = "\U00000436";

    // The encoding is not detected from the content: the documents that do not declare an encoding have the default one
    private static Document Parse(string html) => new HtmlDocumentParser(new HtmlParserOptions { DefaultEncoding = "windows-1252", EncodingDetector = null }).Parse(Encoding.Latin1.GetBytes(html));

    [Fact]
    public void MetaInTheHeadAfterTheFirstKilobyte_ChangesTheEncodingOfTheRestOfTheInput()
    {
        var document = Parse("<head>" + Padding + "<meta charset=windows-1251><title>" + Byte + "</title></head><body>" + Byte);
        Assert.Equal("windows-1251", document.CharacterSet);
        Assert.Equal(Windows1251Character, document.Title);
        Assert.Equal(Windows1251Character, document.Body!.TextContent);
    }

    [Fact]
    public void MetaInTheHeadAfterTheFirstKilobyte_AfterNonAsciiBytes_IsFoundByThePrescan()
    {
        var document = Parse("<head><title>" + Byte + "</title>" + Padding + "<meta charset=windows-1251></head><body>" + Byte);
        Assert.Equal("windows-1251", document.CharacterSet);
        Assert.Equal(Windows1251Character, document.Title);
        Assert.Equal(Windows1251Character, document.Body!.TextContent);
    }

    [Theory]
    [InlineData("</head><meta charset=windows-1251>")]
    [InlineData("<template><meta charset=windows-1251></template>")]
    [InlineData("<plaintext><meta charset=windows-1251>")]
    [InlineData("<body><meta charset=windows-1251>")]
    public void MetaOutsideOfTheHeadAfterTheFirstKilobyte_DoesNotChangeTheEncoding(string markup)
    {
        var document = Parse("<head>" + Padding + markup + Byte);
        Assert.Equal("windows-1252", document.CharacterSet);
        Assert.EndsWith(Byte, document.DocumentElement!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void MetaInTheFirstKilobyte_DeclaresTheEncoding()
    {
        var document = Parse("<body><svg><meta charset=windows-1251></svg>" + Byte);
        Assert.Equal("windows-1251", document.CharacterSet);
        Assert.Equal(Windows1251Character, document.Body!.TextContent);
    }
}
