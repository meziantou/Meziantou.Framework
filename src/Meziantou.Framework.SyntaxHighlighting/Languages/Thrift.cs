using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Thrift
{
    private static readonly string[] Types = ["bool", "byte", "i16", "i32", "i64", "double", "string", "binary"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var container = new Mode
        {
            Begin = @"\b(set|list|map)\s*<",
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["type"] = [.. Types, "set", "list", "map"],
            }),
            End = ">",
        };
        container.Contains = [Mode.Self];

        return new Mode
        {
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = ["namespace", "const", "typedef", "struct", "enum", "service", "exception", "void", "oneway", "set", "list", "map", "required", "optional"],
                ["type"] = Types,
                ["literal"] = ["true", "false"],
            }),
            Contains =
            [
                CommonModes.QuoteStringMode,
                CommonModes.NumberMode,
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                new Mode
                {
                    Scope = "class",
                    BeginKeywords = ["struct", "enum", "service", "exception"],
                    End = @"\{",
                    Illegal = @"\n",
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "title",
                            Begin = CommonModes.IdentRe,

                            // hack: eating everything after the first title
                            Starts = new Mode { EndsWithParent = true, ExcludeEnd = true },
                        },
                    ],
                },
                container,
            ],
        };
    }
}
