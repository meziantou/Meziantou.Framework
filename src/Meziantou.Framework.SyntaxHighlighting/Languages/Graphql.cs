using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Graphql
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        const string GqlName = @"[_A-Za-z][_0-9A-Za-z]*";
        return new Mode
        {
            CaseInsensitive = true,
            Illegal = @"[;<']|BEGIN",
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = ["query", "mutation", "subscription", "type", "input", "schema", "directive", "interface", "union", "scalar", "fragment", "enum", "on"],
                ["literal"] = ["true", "false", "null"],
            }),
            Contains =
            [
                CommonModes.HashCommentMode,
                new()
                {
                    Scope = "string",
                    Begin = "\"\"\"",
                    End = "\"\"\"",
                },
                CommonModes.QuoteStringMode,
                CommonModes.NumberMode,
                new() { Scope = "punctuation", Match = @"[.]{3}" },
                new() { Scope = "punctuation", Begin = @"[\!\(\)\:\=\[\]\{\|\}]{1}" },
                new()
                {
                    Scope = "variable",
                    Begin = @"\$",
                    End = @"\W",
                    ExcludeEnd = true,
                },
                new() { Scope = "meta", Match = @"@\w+" },
                new() { Scope = "symbol", Begin = GqlName + @"(?=\s*:)" },
            ],
        };
    }
}
