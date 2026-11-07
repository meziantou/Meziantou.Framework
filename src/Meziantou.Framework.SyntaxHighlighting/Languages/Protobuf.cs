using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Protobuf
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ["package", "import", "option", "optional", "required", "repeated", "group", "oneof"],
            ["type"] = ["double", "float", "int32", "int64", "uint32", "uint64", "sint32", "sint64", "fixed32", "fixed64", "sfixed32", "sfixed64", "bool", "string", "bytes"],
            ["literal"] = ["true", "false"],
        });

        // Deviation from highlight.js: `\b`, so that `some_service service_list` is not a service definition.
        var classDefinition = new Mode
        {
            BeginParts = [@"\b(message|enum|service)\s+", CommonModes.IdentRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [2] = "title.class",
            },
        };

        // Deviation from highlight.js, which only knows the keywords above: contextual keywords are highlighted
        // where they are keywords, so that fields can still be named `to`, `max`, `map`, `stream`, `public`, etc.
        const string StatementStart = @"(?<=(?:^|[;{}])\s*\w+)";
        var comments = new[] { CommonModes.CLineCommentMode, CommonModes.CBlockCommentMode };
        var numbers = CommonModes.CNumberMode;
        var strings = new[] { CommonModes.QuoteStringMode, CommonModes.AposStringMode };

        return new Mode
        {
            Keywords = keywords,
            Contains =
            [
                // Deviation from highlight.js: single-quoted strings.
                .. strings,

                // Deviation from highlight.js: hexadecimal and exponent numbers, negative default values.
                numbers,
                .. comments,
                new Mode { Scope = "keyword", Match = @"\b(?:syntax|edition)\b(?=\s*=)" + StatementStart },
                new Mode { Scope = "keyword", Match = @"\bextend\b(?=\s+[\w.]+\s*\{)" + StatementStart },

                // Symbol visibility (edition 2024)
                new Mode { Scope = "keyword", Match = @"\b(?:export|local)(?=\s+(?:message|enum)\b)" },
                new Mode
                {
                    BeginParts = [@"\bimport", @"\s+", @"(?:public|weak)\b"],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "keyword",
                    },
                },
                new Mode { Scope = "keyword", Match = @"\bmap(?=\s*<)" },
                new Mode
                {
                    Begin = @"\b(?:reserved|extensions)\b" + StatementStart,
                    End = ";",
                    Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["keyword"] = ["reserved", "extensions", "to", "max"],
                        ["literal"] = ["true", "false"],
                    }),
                    Contains = [.. strings, numbers, .. comments],
                },
                classDefinition,

                // Deviation from highlight.js: the method name is a title, and `stream` is a keyword.
                new Mode
                {
                    Scope = "function",
                    BeginParts = [@"(?<!\.)\brpc\b", @"\s+", CommonModes.IdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.function",
                    },
                    End = "[{;]",
                    ExcludeEnd = true,
                    Keywords = Keywords.FromWords(["rpc", "returns", "stream"]),
                    Contains = [.. comments],
                },

                // match enum items (relevance)
                // BLAH = ...;
                new Mode { Begin = CommonModes.IndentedLineStartRe + @"[A-Z_]+(?=\s*=[^\n]+;$)" },
            ],
        };
    }
}
