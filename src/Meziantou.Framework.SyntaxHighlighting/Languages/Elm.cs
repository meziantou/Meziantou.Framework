using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Elm
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = new Mode
        {
            Variants =
            [
                CommonModes.Comment("--", "$"),
                CommonModes.Comment(@"\{-", @"-\}", extraContains: [Mode.Self]),
            ],
        };

        var constructor = new Mode
        {
            Scope = "type",
            Begin = @"\b[A-Z][\w']*",
        };

        Mode[] listContains =
        [
            new Mode
            {
                Scope = "type",
                Begin = @"\b[A-Z][\w]*(\((\.\.|,|\w+)\))?",
            },
            comment,
        ];

        var list = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Illegal = "\"",
            Contains = listContains,
        };

        var record = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Contains = listContains,
        };

        var character = new Mode
        {
            Scope = "string",
            Begin = @"'\\?.",
            End = "'",
            Illegal = ".",
        };

        return new Mode
        {
            Keywords = Engine.Keywords.FromWords(
            [
                "let", "in", "if", "then", "else", "case", "of", "where", "module", "import", "exposing", "type", "alias", "as", "infix", "infixl",
                "infixr", "port", "effect", "command", "subscription",
            ]),
            Illegal = ";",
            Contains =
            [
                // Top-level constructions.

                // Deviation from highlight.js, which starts this mode at any of `port`, `effect` and `module`: a port
                // declaration (`port send : String -> Cmd msg`) has no `exposing` to end it, so it swallows the rest of the
                // document. `port` and `effect` only start it when `module` follows them.
                new Mode
                {
                    Begin = @"\b(?:(?:port|effect)\s+)?module\b",
                    End = "exposing",
                    Keywords = Engine.Keywords.FromWords(["port", "effect", "module", "where", "command", "subscription", "exposing"]),
                    Contains = [list, comment],
                    Illegal = @"\W\.|;",
                },

                // Deviation from highlight.js, which also starts the next three modes inside identifiers (`important`,
                // `typeahead`, `transport`) and colors the rest of the line as a declaration: they must be whole words.
                new Mode
                {
                    Begin = @"\bimport\b",
                    End = "$",
                    Keywords = Engine.Keywords.FromWords(["import", "as", "exposing"]),
                    Contains = [list, comment],
                    Illegal = @"\W\.|;",
                },
                new Mode
                {
                    Begin = @"\btype\b",
                    End = "$",
                    Keywords = Engine.Keywords.FromWords(["type", "alias"]),
                    Contains = [constructor, list, record, comment],
                },
                new Mode
                {
                    BeginKeywords = ["infix", "infixl", "infixr"],
                    End = "$",
                    Contains = [CommonModes.CNumberMode, comment],
                },
                new Mode
                {
                    Begin = @"\bport\b",
                    End = "$",
                    Keywords = Engine.Keywords.FromWords(["port"]),
                    Contains = [comment],
                },

                // Literals and names.
                character,

                // Deviation from highlight.js, which does not support multi-line strings: `"""` is an empty string
                // followed by a string that ends at the next quote, so the quotes they contain alternate the colors.
                new Mode
                {
                    Scope = "string",
                    Begin = "\"\"\"",
                    End = "\"\"\"",
                    Contains = [CommonModes.BackslashEscape],
                },
                CommonModes.QuoteStringMode,
                CommonModes.CNumberMode,
                constructor,
                new Mode(CommonModes.TitleMode) { Begin = @"^[_a-z][\w']*" },
                comment,

                // No markup, relevance booster.
                new Mode { Begin = "->|<-" },
            ],
        };
    }
}
