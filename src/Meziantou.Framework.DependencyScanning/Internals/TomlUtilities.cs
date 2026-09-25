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

    /// <summary>Determines whether a key/value pair is inside an inline table, rather than under a header or at the root.</summary>
    public static bool IsInInlineTable(TomlKeyValue pair) => pair.Property.Parent is TomlInlineTableSyntax;

    /// <summary>Determines whether the text between the quotes of a single-line string is its value, as it is when the string has no escape sequence.</summary>
    /// <remarks>
    /// Only then can the value be found in the text at the same offsets, and a new value be written there as it is.
    /// </remarks>
    public static bool IsVerbatim(TomlStringSyntax value) => !value.IsMultiLine && IsVerbatim(value.StringToken);

    /// <summary>Creates the location of the content of a string, without its quotes, or a location that cannot be updated when the string has escape sequences.</summary>
    public static Location CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax value)
        => IsVerbatim(value) ? CreateLocation(context, tree, GetContentSpan(value.StringToken)) : new NonUpdatableLocation(context);

    /// <summary>Creates the location of the name a part of a key holds, without its quotes, or a location that cannot be updated when the name has escape sequences.</summary>
    public static Location CreateLocation(ScanFileContext context, TomlSyntaxTree tree, SyntaxToken keyPart)
    {
        if (keyPart.IsMissing)
            return new NonUpdatableLocation(context);

        if (keyPart.IsKind(SyntaxKind.BareKeyToken))
            return CreateLocation(context, tree, keyPart.Span);

        return IsVerbatim(keyPart) ? CreateLocation(context, tree, GetContentSpan(keyPart)) : new NonUpdatableLocation(context);
    }

    /// <summary>Gets the span of the text between the quotes of a single-line string.</summary>
    private static TextSpan GetContentSpan(SyntaxToken token) => new(token.Span.Start + 1, token.Span.Length - 2);

    private static bool IsVerbatim(SyntaxToken token)
    {
        var text = token.Text;
        return text.Length >= 2 && text.AsSpan(1, text.Length - 2).SequenceEqual(token.ValueText);
    }

    public static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, span.Length);
    }
}
