using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// https://coffeescript.org/#language
internal static class CoffeeScript
{
    private const string PossibleParamsRe = @"(\(.*\)\s*)?\B[-=]>";

    // Deviation from highlight.js, which uses its C number (CommonModes.CNumberRe), where the dots of a range are
    // fractions (`[1..10]` is `1.` followed by `.10`): a dot followed or preceded by another dot is not a fraction.
    private const string NumberRe = @"(-?)(\b0[xX][a-fA-F0-9]+|(\b\d+(\.(?!\.)\d*)?|(?<!\.)\.\d+)([eE][-+]?\d+)?)";

    // JavaScript keywords that are not CoffeeScript keywords.
    private static readonly string[] NotValidKeywords = ["var", "const", "let", "function", "static"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] =
            [
                .. EcmaScript.Keywords.Where(keyword => !NotValidKeywords.Contains(keyword, StringComparer.Ordinal)),
                "then", "unless", "until", "loop", "by", "when", "and", "or", "is", "isnt", "not",
            ],
            ["literal"] = [.. EcmaScript.Literals, "yes", "no", "on", "off"],
            ["built_in"] = [.. EcmaScript.BuiltIns, "npm", "print"],
        });

        var subst = new Mode { Scope = "subst", Begin = @"#\{", End = @"\}", Keywords = keywords };

        Mode[] expressions =
        [
            new Mode { Scope = "number", Begin = @"\b(0b[01]+)" },

            // A number tries to eat the following slash to prevent treating it as a regexp.
            new Mode { Scope = "number", Begin = NumberRe, Starts = new Mode { End = @"(\s*/)?" } },
            new Mode
            {
                Scope = "string",
                Variants =
                [
                    new Mode { Begin = "'''", End = "'''", Contains = [CommonModes.BackslashEscape] },
                    new Mode { Begin = "'", End = "'", Contains = [CommonModes.BackslashEscape] },
                    new Mode { Begin = "\"\"\"", End = "\"\"\"", Contains = [CommonModes.BackslashEscape, subst] },
                    new Mode { Begin = "\"", End = "\"", Contains = [CommonModes.BackslashEscape, subst] },
                ],
            },
            new Mode
            {
                Scope = "regexp",
                Variants =
                [
                    new Mode { Begin = "///", End = "///", Contains = [subst, CommonModes.HashCommentMode] },
                    new Mode { Begin = @"//[gim]{0,3}(?=\W)" },

                    // A regexp cannot start with a space, to parse `x / 2 / 3` as two divisions, nor with `*`, which is
                    // illegal in the root mode.
                    new Mode { Begin = @"\/(?![ *]).*?(?![\\]).\/[gim]{0,3}(?=\W)" },
                ],
            },
            new Mode { Begin = "@" + EcmaScript.IdentRe },
            new Mode
            {
                SubLanguage = "javascript",
                ExcludeBegin = true,
                ExcludeEnd = true,
                Variants =
                [
                    new Mode { Begin = "```", End = "```" },
                    new Mode { Begin = "`", End = "`" },
                ],
            },
        ];
        subst.Contains = expressions;

        // A nameless mode inside, so that nested parentheses are not all parameters.
        var parameterList = new Mode { Begin = @"\(", End = @"\)", Keywords = keywords };
        parameterList.Contains = [Mode.Self, .. expressions];
        var parameters = new Mode
        {
            Scope = "params",
            Begin = @"\([^\(]",
            ReturnBegin = true,
            Contains = [parameterList],
        };

        return new Mode
        {
            Keywords = keywords,
            Illegal = @"\/\*",
            Contains =
            [
                .. expressions,
                CommonModes.Comment("###", "###"),
                CommonModes.HashCommentMode,

                // IndentedLineStartRe is `^\s*`, without rescanning a run of blank lines from each of its line starts.
                new Mode
                {
                    Scope = "function",
                    Begin = CommonModes.IndentedLineStartRe + EcmaScript.IdentRe + @"\s*=\s*" + PossibleParamsRe,
                    End = "[-=]>",
                    ReturnBegin = true,
                    Contains = [new Mode { Scope = "title", Begin = EcmaScript.IdentRe }, parameters],
                },

                // Anonymous function start
                new Mode
                {
                    Begin = @"[:\(,=]\s*",
                    Contains =
                    [
                        // RunStart: the parameter list is only tried from the first `(` of the line that follows the
                        // search start. When `\(.*\)` fails from it, there is no `)` followed by an arrow on the rest of
                        // the line, so it fails from the later ones too, after scanning to the end of the line from each of
                        // them, which is quadratic on a line of many parentheses.
                        new Mode
                        {
                            Scope = "function",
                            Begin = "(" + CommonModes.RunStart(@"^\n", "(") + @"\(.*\)\s*)?\B[-=]>",
                            End = "[-=]>",
                            ReturnBegin = true,
                            Contains = [parameters],
                        },
                    ],
                },
                new Mode
                {
                    Variants =
                    [
                        new Mode { BeginParts = [@"class\s+", EcmaScript.IdentRe, @"\s+extends\s+", EcmaScript.IdentRe] },
                        new Mode { BeginParts = [@"class\s+", EcmaScript.IdentRe] },
                    ],
                    BeginScope = new Dictionary<int, string>
                    {
                        [2] = "title.class",
                        [4] = "title.class.inherited",
                    },
                    Keywords = keywords,
                },

                // An object key is not a keyword (`default: 1`). RunStart only lets the pattern start at the first
                // character of an identifier: it fails identically from every later position, which is quadratic on a
                // long identifier that is not followed by a colon.
                new Mode
                {
                    Begin = CommonModes.RunStart("0-9A-Za-z$_", "A-Za-z$_") + EcmaScript.IdentRe + ":",
                    End = ":",
                    ReturnBegin = true,
                    ReturnEnd = true,
                },
            ],
        };
    }
}
