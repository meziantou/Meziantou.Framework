using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static partial class TomlUtilities
{
    /// <summary>Gets the content of a basic or literal string value, and its offset in the value text.</summary>
    public static bool TryGetString(string value, [NotNullWhen(true)] out string? content, out int offset)
    {
        if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
        {
            content = value[1..^1];
            offset = 1;
            return true;
        }

        content = null;
        offset = 0;
        return false;
    }

    /// <summary>Gets a top-level string entry of an inline table, such as <c>version = "1.0"</c> in <c>{ features = ["a"], version = "1.0" }</c>, and its offset in the value text.</summary>
    public static bool TryGetInlineTableString(string value, string key, [NotNullWhen(true)] out string? content, out int offset)
    {
        if (value.StartsWith('{', StringComparison.Ordinal))
        {
            foreach (Match match in InlineTableStringEntryRegex().Matches(value))
            {
                if (!match.Groups["key"].ValueSpan.Equals(key, StringComparison.Ordinal))
                    continue;

                var group = match.Groups["basic"].Success ? match.Groups["basic"] : match.Groups["literal"];
                content = group.Value;
                offset = group.Index;
                return true;
            }
        }

        content = null;
        offset = 0;
        return false;
    }

    public static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, span.Length);
    }

    [GeneratedRegex("""(?:^\{|,)\s*(?<key>[A-Za-z0-9_.-]+)\s*=\s*(?:"(?<basic>[^"]*)"|'(?<literal>[^']*)')""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex InlineTableStringEntryRegex();
}
