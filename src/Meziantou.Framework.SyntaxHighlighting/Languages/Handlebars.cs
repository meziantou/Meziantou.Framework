using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <remarks>
/// Also registered as <c>mustache</c>: highlight.js has no Mustache grammar, and Handlebars is a superset of it.
/// </remarks>
internal static class Handlebars
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    // Literal segments (https://handlebarsjs.com/guide/expressions.html#literal-segments): ' abc ', [ abc ], as well
    // as helpers and paths like a/b, ./abc/cde and abc.bcd.
    private const string DoubleQuotedIdRe = "\"\"|\"[^\"]+\"";
    private const string SingleQuotedIdRe = "''|'[^']+'";

    // Deviation from highlight.js, whose `[ abc ]` segment is `\[\]|\[[^\]]+\]`: a segment that is not closed was
    // scanned to the end of the document from each `[` that follows it. A segment cannot contain a `[`, and it ends at
    // the end of its mustache (`}`).
    private const string BracketQuotedIdRe = @"\[[^\[\]}]*\]";
    private const string PlainIdChars = @"^\s!""#%&'()*+,./;<=>@\[\\\]^`{|}~";
    private const string PlainIdRe = "[" + PlainIdChars + "]+";
    private const string AnyIdRe = "(?:" + DoubleQuotedIdRe + "|" + SingleQuotedIdRe + "|" + BracketQuotedIdRe + "|" + PlainIdRe + ")";
    private const string IdentifierRe = @"(?:\.|\./|/)?" + AnyIdRe + @"(?:(?:\.|/)" + AnyIdRe + ")*";

    // Deviation from highlight.js, whose keyword pattern is `[\w.\/]+`: it splits `link-to`, `each-in` and
    // `query-params` at the hyphen, so those built-ins were never highlighted as a whole, while the `if` of a
    // `{{if-foo}}` helper was. A hyphen is part of an identifier.
    private const string KeywordPatternRe = @"[\w.\/-]+";

    private static Mode CreateMode()
    {
        var builtIns = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["built_in"] =
                "action bindattr collection component concat debugger each each-in get hash if in input link-to loc log "
                + "lookup mut outlet partial query-params render template textarea unbound unless view with yield",
        });

        var literals = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["literal"] = "true false undefined null",
        });

        // An identifier followed by an equal sign (without the equal sign). `(\[\]|\[[^\]]+\]|[^…]+)(?==)` in
        // highlight.js, which is quadratic on a long run of identifier characters that is not followed by `=`.
        var hashParamRe = "(?:" + BracketQuotedIdRe + "|" + CommonModes.RunStart(PlainIdChars) + PlainIdRe + ")(?==)";

        var helperParameter = new Mode { Begin = IdentifierRe, KeywordPattern = KeywordPatternRe, Keywords = literals };

        // The contents are set below, once every mode they need is defined.
        var subExpression = new Mode { Begin = @"\(", End = @"\)" };

        // Parameters of the form `key=value` (fka "attribute-assignment").
        var hash = new Mode
        {
            Scope = "attr",
            Begin = hashParamRe,
            Starts = new Mode
            {
                Begin = "=",
                End = "=",
                Starts = new Mode
                {
                    Contains = [CommonModes.NumberMode, CommonModes.QuoteStringMode, CommonModes.AposStringMode, helperParameter, subExpression],
                },
            },
        };

        // Parameters of the form `{{#with x as | y |}}...{{/with}}`.
        var blockParams = new Mode
        {
            Begin = @"as\s+\|",
            Keywords = Engine.Keywords.FromWords(["as"]),
            End = @"\|",
            Contains =
            [
                // A sub-mode, so that a block parameter named "as" is not highlighted.
                new Mode { Begin = @"\w+" },
            ],
        };

        // The end depends on the surrounding mode, and EndsWithParent does not work here (the parameters would
        // include the end token of the surrounding mode).
        Mode HelperParameters(string end) => new()
        {
            Contains = [CommonModes.NumberMode, CommonModes.QuoteStringMode, CommonModes.AposStringMode, blockParams, hash, helperParameter, subExpression],
            ReturnEnd = true,
            End = end,
        };

        subExpression.Contains = [new Mode { Scope = "name", Begin = IdentifierRe, KeywordPattern = KeywordPatternRe, Keywords = builtIns, Starts = HelperParameters(@"\)") }];

        var openingBlockMustacheContents = new Mode { Scope = "name", Begin = IdentifierRe, KeywordPattern = KeywordPatternRe, Keywords = builtIns, Starts = HelperParameters(@"\}\}") };
        var closingBlockMustacheContents = new Mode { Scope = "name", Begin = IdentifierRe, KeywordPattern = KeywordPatternRe, Keywords = builtIns };
        var basicMustacheContents = new Mode { Scope = "name", Begin = IdentifierRe, KeywordPattern = KeywordPatternRe, Keywords = builtIns, Starts = HelperParameters(@"\}\}") };

        return new Mode
        {
            CaseInsensitive = true,

            // Deviation from highlight.js, which embeds `xml`: its xml grammar is this library's `html` grammar (the
            // `xml` one does not highlight <script> and <style> contents), so the markup is wrapped in `language-html`.
            SubLanguage = "html",
            Contains =
            [
                // An escaped mustache: `\{{name}}` is output as is.
                new Mode { Begin = @"\\\{\{", Skip = true },

                // `\\{{name}}` is a backslash followed by a mustache.
                new Mode { Begin = @"\\\\(?=\{\{)", Skip = true },
                CommonModes.Comment(@"\{\{!--", @"--\}\}"),
                CommonModes.Comment(@"\{\{!", @"\}\}"),

                // Open raw block: `{{{{raw}}}} content not evaluated {{{{/raw}}}}`.
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{\{\{\{(?!/)",
                    End = @"\}\}\}\}",
                    Contains = [openingBlockMustacheContents],
                    Starts = new Mode
                    {
                        End = @"\{\{\{\{/",
                        ReturnEnd = true,
                        SubLanguage = "html",
                    },
                },

                // Close raw block.
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{\{\{\{/",
                    End = @"\}\}\}\}",
                    Contains = [closingBlockMustacheContents],
                },

                // Open block statement.
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{\{#",
                    End = @"\}\}",
                    Contains = [openingBlockMustacheContents],
                },
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{\{(?=else\}\})",
                    End = @"\}\}",
                    Keywords = Engine.Keywords.FromWords(["else"]),
                },
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{\{(?=else if)",
                    End = @"\}\}",
                    Keywords = Engine.Keywords.FromWords(["else", "if"]),
                },

                // Closing block statement.
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{\{/",
                    End = @"\}\}",
                    Contains = [closingBlockMustacheContents],
                },

                // Template variable or helper call that is NOT HTML-escaped.
                new Mode
                {
                    Scope = "template-variable",
                    Begin = @"\{\{\{",
                    End = @"\}\}\}",
                    Contains = [basicMustacheContents],
                },

                // Template variable or helper call that is HTML-escaped.
                new Mode
                {
                    Scope = "template-variable",
                    Begin = @"\{\{",
                    End = @"\}\}",
                    Contains = [basicMustacheContents],
                },
            ],
        };
    }
}
