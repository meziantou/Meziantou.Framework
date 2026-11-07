using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Internals;

/// <summary>The entry points of CSS Syntax Level 3 that a style sheet does not use, which its test suite exercises.</summary>
/// <remarks>Each returns a style sheet so that every token, the end of the text included, has a place in a tree.</remarks>
[SuppressMessage("Design", "MA0182: Internal type is apparently never used", Justification = "Used by the tests")]
internal static class CssTestHooks
{
    /// <summary>Parses <paramref name="text"/> as a list of component values, held by a single bad declaration.</summary>
    public static CssStyleSheetSyntax ParseComponentValueList(string text)
    {
        var parser = new Green.LanguageParser(SourceText.From(text), CssParseOptions.Default);
        var values = parser.ParseComponentValueList();
        GreenNode? statements = values.Count == 0 ? null : new Green.CssBadDeclarationSyntax(Green.SyntaxFactory.List(values), semicolonToken: null);

        return (CssStyleSheetSyntax)new Green.CssStyleSheetSyntax(statements, parser.EndOfFileToken).CreateRed();
    }

    /// <summary>Parses <paramref name="text"/> as a single declaration, or as a bad declaration when it does not start with a name and a colon.</summary>
    public static CssStyleSheetSyntax ParseSingleDeclaration(string text)
    {
        var parser = new Green.LanguageParser(SourceText.From(text), CssParseOptions.Default);
        GreenNode? statement = parser.ParseSingleDeclaration();
        if (statement is null)
        {
            var values = parser.ParseComponentValueList();
            statement = values.Count == 0 ? null : new Green.CssBadDeclarationSyntax(Green.SyntaxFactory.List(values), semicolonToken: null);
        }

        return (CssStyleSheetSyntax)new Green.CssStyleSheetSyntax(statement, parser.EndOfFileToken).CreateRed();
    }

    public static bool TryParseAnPlusB(string text, out int a, out int b) => new Green.LanguageParser(SourceText.From(text), CssParseOptions.Default).TryParseAnPlusB(out a, out b);
}
