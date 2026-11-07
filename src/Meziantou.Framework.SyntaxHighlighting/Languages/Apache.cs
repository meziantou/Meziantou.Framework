using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Apache
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var numberRef = new Mode { Scope = "number", Begin = @"[$%]\d+" };
        var number = new Mode { Scope = "number", Begin = @"\b\d+" };
        var ipAddress = new Mode { Scope = "number", Begin = @"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d{1,5})?" };
        var portNumber = new Mode { Scope = "number", Begin = @":\d{1,5}" };

        // Deviation from highlight.js, whose variable only ends at a `}`: `%{HTTP_HOST` without one turned the
        // following lines into a variable, up to the next `}`. It also ends at the end of the line. (Ending with the
        // directive instead would make each token inside deeply nested variables check the end of every enclosing one.)
        var variable = new Mode
        {
            Scope = "variable",
            Begin = @"[\$%]\{",
            End = @"\}|$",
        };
        variable.Contains = [Mode.Self, numberRef];

        return new Mode
        {
            CaseInsensitive = true,
            Illegal = @"\S",
            Contains =
            [
                CommonModes.HashCommentMode,
                new Mode
                {
                    Scope = "section",
                    Begin = "</?",
                    End = ">",
                    Contains = [ipAddress, portNumber, CommonModes.QuoteStringMode],
                },

                // highlight.js also lists the common directives as keywords of the `_` group: they are not
                // highlighted, they only help the language auto-detection, which is not supported.
                new Mode
                {
                    Scope = "attribute",
                    Begin = @"\w+",
                    Starts = new Mode
                    {
                        End = "$",
                        Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal) { ["literal"] = "on off all deny allow" }),
                        Contains =
                        [
                            new Mode { Scope = "punctuation", Match = @"\\\n" },

                            // Deviation from highlight.js, whose flags only end at a `]` that ends a line: a `[` that
                            // follows a space without such a `]` (`RewriteRule [0-9]+ x`, `[L] # comment`) turned the
                            // following lines into flags. They also end at the end of the directive.
                            new Mode { Scope = "meta", Begin = @"\s\[", End = @"\]$", EndsWithParent = true },
                            variable,
                            ipAddress,
                            number,
                            CommonModes.QuoteStringMode,
                        ],
                    },
                },
            ],
        };
    }
}
