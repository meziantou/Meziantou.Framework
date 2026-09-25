using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class TomlUtilities
{
    /// <summary>Gets the name of a key, such as <c>version.ref</c>, with quoted parts unquoted.</summary>
    public static string GetName(TomlKeySyntax key) => string.Join('.', key.Names);

    /// <summary>Gets a single-line string value. Multi-line strings are ignored as a version cannot span several lines.</summary>
    public static TomlStringSyntax? GetString(TomlValueSyntax value) => value is TomlStringSyntax { IsMultiLine: false } str ? str : null;

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

    /// <summary>Creates the location of the content of a string, without its quotes.</summary>
    public static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TomlStringSyntax value)
    {
        var span = value.StringToken.Span;
        return CreateLocation(context, tree, new TextSpan(span.Start + 1, span.Length - 2));
    }

    public static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, span.Length);
    }
}
