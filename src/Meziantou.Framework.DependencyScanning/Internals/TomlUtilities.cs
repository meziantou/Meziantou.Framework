using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class TomlUtilities
{
    /// <summary>Gets the name of a key, such as <c>version.ref</c>, with quoted parts unquoted.</summary>
    public static string GetName(TomlKeySyntax key) => string.Join('.', key.Names);

    /// <summary>Gets a single-line string value. Multi-line strings are ignored as a version cannot span several lines.</summary>
    /// <remarks>A string the parser reported, such as one without its closing quote, is ignored too: its value is a guess, and its quotes are not where a location expects them.</remarks>
    public static TomlStringSyntax? GetString(TomlValueSyntax value) => value is TomlStringSyntax { IsMultiLine: false } str && !str.StringToken.ContainsDiagnostics ? str : null;

    /// <summary>Gets a string entry of an inline table, such as <c>version</c> in <c>{ features = ["a"], version = "1.0" }</c>.</summary>
    public static TomlStringSyntax? GetInlineTableString(TomlValueSyntax value, string key)
    {
        if (value is TomlInlineTableSyntax table)
        {
            foreach (var property in table.Properties)
            {
                if (GetName(property.Key) == key)
                    return GetString(property.Value);
            }
        }

        return null;
    }

    /// <summary>Gets whether a property has both a key and a value, so that it is not the parser's recovery from a malformed line.</summary>
    /// <remarks>For instance, a line holding only <c>"serde = "1.0"</c> is a complete quoted key, <c>serde = </c>, followed by text that is not a value.</remarks>
    public static bool IsWellFormed(TomlPropertySyntax property) => !property.EqualsToken.IsMissing && !property.Key.ContainsDiagnostics;

    public static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, span.Length);
    }

    /// <summary>Creates the location of the content of a string, which is not updatable when the text of the string is not its value, such as <c>"1.0"</c>.</summary>
    /// <remarks>A location replaces the text of the file, so writing a value where escape sequences were would change more than the value.</remarks>
    public static Location CreateValueLocation(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax value) => CreateTokenLocation(context, tree, value.StringToken);

    /// <summary>Creates the location of a key part, such as <c>serde</c> or <c>"serde"</c>, which is not updatable when the text of the key is not its name.</summary>
    public static Location CreateKeyLocation(ScanFileContext context, TomlSyntaxTree tree, SyntaxToken keyPart) => CreateTokenLocation(context, tree, keyPart);

    private static Location CreateTokenLocation(ScanFileContext context, TomlSyntaxTree tree, SyntaxToken token)
    {
        if (TryGetVerbatimSpan(token, out var span))
            return CreateLocation(context, tree, span);

        return new NonUpdatableLocation(context);
    }

    /// <summary>Gets the span of the content of a bare key or a single-line string, when that content is exactly its value.</summary>
    private static bool TryGetVerbatimSpan(SyntaxToken token, out TextSpan span)
    {
        span = default;
        if (token.ContainsDiagnostics || token.IsMissing)
            return false;

        var text = token.Text;
        var value = token.ValueText;
        switch (token.Kind())
        {
            case SyntaxKind.BareKeyToken when text == value:
                span = token.Span;
                return true;

            case SyntaxKind.BasicStringToken or SyntaxKind.LiteralStringToken when text.Length >= 2 && text[0] == text[^1] && text.AsSpan(1, text.Length - 2).SequenceEqual(value):
                span = new TextSpan(token.Span.Start + 1, token.Span.Length - 2);
                return true;

            default:
                return false;
        }
    }
}
