namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The positions of the parse errors reported by <see cref="HtmlParserOptions.ParseErrorHandler"/>.</summary>
public sealed class ParseErrorTests
{
    private static List<string> Parse(string html)
    {
        var errors = new List<string>();
        HtmlDocumentParser.ParseDocument(html, new HtmlParserOptions { ParseErrorHandler = error => errors.Add(error.ToString()) });
        return errors;
    }

    [Fact]
    public void TreeBuilderErrors_AreReportedAtTheEndOfTheToken()
    {
        // The errors of the tree construction are at the last character of the token, the end-of-file errors at the end of the input
        Assert.Equal(["(1,6): expected-doctype-but-got-other", "(1,15): unexpected-end-tag", "(1,16): eof-in-body"], Parse("<html><b></div>"));
    }

    [Fact]
    public void TreeBuilderErrors_CharactersBeforeATag()
    {
        // The characters end before the tag that follows them
        Assert.Equal(["(2,2): unexpected-token-after-after-body"], Parse("<!DOCTYPE html><body></body></html>\nab<p>"));
    }

    [Fact]
    public void TreeBuilderErrors_TableText_AreReportedWhenTheCharactersAreProcessed()
    {
        // The pending table characters are processed when the next token is not a character
        Assert.Equal(["(2,6): unexpected-character-in-table"], Parse("<!DOCTYPE html><table>\nab<tr></table>"));
    }

    [Fact]
    public void TokenizerErrors_AreReportedAtTheCharacter()
    {
        // The tokenizer reports the NULL character, then the "in body" insertion mode ignores it (another parse error)
        Assert.Equal(["(1,21): unexpected-null-character", "(1,21): unexpected-null-character"], Parse("<!DOCTYPE html><p>ab\0"));
    }
}
