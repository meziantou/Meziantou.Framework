using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class R
{
    // Identifiers cannot start with `_`, but they can start with `.` if it is not immediately followed by a digit.
    // Quoted identifiers (`` `a b` ``) are handled by a separate mode.
    private const string IdentRe = @"(?:(?:[a-zA-Z]|\.[._a-zA-Z])[._a-zA-Z0-9]*)|\.(?!\d)";

    private const string NumberTypesRe =
        "(?:"
        // Special case: only hexadecimal binary powers can contain fractions
        + @"0[xX][0-9a-fA-F]+\.[0-9a-fA-F]*[pP][+-]?\d+i?"
        // Hexadecimal numbers without fraction and optional binary power
        + @"|0[xX][0-9a-fA-F]+(?:[pP][+-]?\d+)?[Li]?"
        // Decimal numbers
        + @"|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[Li]?"
        + ")";

    private const string OperatorsRe = @"[=!<>:]=|\|\||&&|:::?|<-|<<-|->>|->|\|>|[-+*\/?!$&|:<=>@^~]|\*\*";
    private const string PunctuationRe = @"(?:[()]|[{}]|\[\[|[[\]]|\\|,)";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = "function if in break next repeat else for while",
            ["literal"] = "NULL NA TRUE FALSE Inf NaN NA_integer_ NA_real_ NA_character_ NA_complex_",
            ["built_in"] =
                // Builtin constants
                "LETTERS letters month.abb month.name pi T F "
                // Primitive functions: all the functions in `base` that are implemented as a `.Primitive`, minus those
                // that are also keywords.
                + "abs acos acosh all any anyNA Arg as.call as.character "
                + "as.complex as.double as.environment as.integer as.logical "
                + "as.null.default as.numeric as.raw asin asinh atan atanh attr "
                + "attributes baseenv browser c call ceiling class Conj cos cosh "
                + "cospi cummax cummin cumprod cumsum digamma dim dimnames "
                + "emptyenv exp expression floor forceAndCall gamma gc.time "
                + "globalenv Im interactive invisible is.array is.atomic is.call "
                + "is.character is.complex is.double is.environment is.expression "
                + "is.finite is.function is.infinite is.integer is.language "
                + "is.list is.logical is.matrix is.na is.name is.nan is.null "
                + "is.numeric is.object is.pairlist is.raw is.recursive is.single "
                + "is.symbol lazyLoadDBfetch length lgamma list log max min "
                + "missing Mod names nargs nzchar oldClass on.exit pos.to.env "
                + "proc.time prod quote range Re rep retracemem return round "
                + "seq_along seq_len seq.int sign signif sin sinh sinpi sqrt "
                + "standardGeneric substitute sum switch tan tanh tanpi tracemem "
                + "trigamma trunc unclass untracemem UseMethod xtfrm",
        });

        var roxygenComment = CommonModes.Comment("#'", "$", extraContains:
        [
            // `@examples` keeps all the following code as-is, until the next `@`-tag on its own line: it is example R
            // code, so nested doctags are not doctags.
            new Mode
            {
                Scope = "doctag",
                Match = "@examples",
                Starts = new Mode
                {
                    End = @"(?=\n^#'\s*(?=@[a-zA-Z]+)|\n^(?!#'))",
                    EndsParent = true,
                },
            },
            // `@param` highlights the name of the parameter that follows.
            new Mode
            {
                Scope = "doctag",
                Begin = "@param",
                End = "$",
                Contains =
                [
                    new Mode
                    {
                        Scope = "variable",
                        Variants =
                        [
                            new Mode { Match = IdentRe },
                            new Mode { Match = @"`(?:\\.|[^`\\])+`" },
                        ],
                        EndsParent = true,
                    },
                ],
            },
            new Mode { Scope = "doctag", Match = "@[a-zA-Z]+" },
            new Mode { Scope = "keyword", Match = @"\\[a-zA-Z]+" },
        ]);

        return new Mode
        {
            KeywordPattern = IdentRe,
            Keywords = keywords,
            Contains =
            [
                roxygenComment,
                CommonModes.HashCommentMode,
                new Mode
                {
                    Scope = "string",
                    Contains = [CommonModes.BackslashEscape],
                    Variants =
                    [
                        new Mode { Begin = @"[rR]""(-*)\(", End = @"\)(-*)""", EndSameAsBegin = true },
                        new Mode { Begin = @"[rR]""(-*)\{", End = @"\}(-*)""", EndSameAsBegin = true },
                        new Mode { Begin = @"[rR]""(-*)\[", End = @"\](-*)""", EndSameAsBegin = true },
                        new Mode { Begin = @"[rR]'(-*)\(", End = @"\)(-*)'", EndSameAsBegin = true },
                        new Mode { Begin = @"[rR]'(-*)\{", End = @"\}(-*)'", EndSameAsBegin = true },
                        new Mode { Begin = @"[rR]'(-*)\[", End = @"\](-*)'", EndSameAsBegin = true },
                        new Mode { Begin = "\"", End = "\"" },
                        new Mode { Begin = "'", End = "'" },
                    ],
                },
                // A number directly after an operator or a punctuation is matched with it, so that a number that is
                // part of an identifier (`x1`) is not a number.
                new Mode
                {
                    Variants =
                    [
                        new Mode
                        {
                            BeginParts = [OperatorsRe, NumberTypesRe],
                            BeginScope = new Dictionary<int, string> { [1] = "operator", [2] = "number" },
                        },
                        new Mode
                        {
                            BeginParts = ["%[^%]*%", NumberTypesRe],
                            BeginScope = new Dictionary<int, string> { [1] = "operator", [2] = "number" },
                        },
                        new Mode
                        {
                            BeginParts = [PunctuationRe, NumberTypesRe],
                            BeginScope = new Dictionary<int, string> { [1] = "punctuation", [2] = "number" },
                        },
                        // Not part of an identifier, or start of a line.
                        new Mode
                        {
                            BeginParts = ["[^a-zA-Z0-9._]|^", NumberTypesRe],
                            BeginScope = new Dictionary<int, string> { [2] = "number" },
                        },
                    ],
                },
                // Assignment. Only the first position of an identifier is tried: otherwise, each character of a long
                // identifier that is not followed by `<-` would rescan the rest of it.
                // Deviation from highlight.js, which consumes the spaces after `<-`: they are left to the next mode, so
                // that the number of `x <- 5` is a number.
                new Mode
                {
                    BeginParts = [CommonModes.RunStart("._a-zA-Z0-9", "a-zA-Z.") + "(?:" + IdentRe + ")", @"\s+", "<-", @"(?=\s)"],
                    BeginScope = new Dictionary<int, string> { [3] = "operator" },
                    // highlight.js highlights the identifier with the keywords of the enclosing mode (`pi <- 3`).
                    KeywordPattern = IdentRe,
                    Keywords = keywords,
                },
                new Mode
                {
                    Scope = "operator",
                    Variants =
                    [
                        new Mode { Match = OperatorsRe },
                        new Mode { Match = "%[^%]*%" },
                    ],
                },
                new Mode { Scope = "punctuation", Match = PunctuationRe },
                // Escaped identifier
                new Mode
                {
                    Begin = "`",
                    End = "`",
                    Contains = [new Mode { Begin = @"\\." }],
                },
            ],
        };
    }
}
