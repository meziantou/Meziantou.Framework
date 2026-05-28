using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Nginx
{
    private static readonly string[] Literals =
    [
        "on","off","yes","no","true","false","none","blocked","debug","info","notice","warn",
        "error","crit","select","break","last","permanent","redirect","kqueue","rtsig","epoll",
        "poll","/dev/poll",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var varMode = new Mode
        {
            Scope = "variable",
            Variants =
            [
                new Mode { Begin = @"\$\d+" },
                new Mode { Begin = @"\$\{\w+\}" },
                new Mode { Begin = @"[$@]" + CommonModes.UnderscoreIdentRe },
            ],
        };

        var defaultContains = new List<Mode>
        {
            CommonModes.HashCommentMode,
            new()
            {
                Scope = "string",
                Variants =
                [
                    new Mode { Begin = "\"", End = "\"" },
                    new Mode { Begin = "'", End = "'" },
                ],
                Contains = [CommonModes.BackslashEscape, varMode],
            },
            new()
            {
                Begin = "([a-z]+):/",
                End = @"\s",
                EndsWithParent = true,
                ExcludeEnd = true,
                Contains = [varMode],
            },
            new()
            {
                Scope = "regexp",
                Contains = [CommonModes.BackslashEscape, varMode],
                Variants =
                [
                    new Mode { Begin = @"\s\^", End = @"\s|\{|;", ReturnEnd = true },
                    new Mode { Begin = @"~\*?\s+", End = @"\s|\{|;", ReturnEnd = true },
                    new Mode { Begin = @"\*(\.[a-z\-]+)+" },
                    new Mode { Begin = @"([a-z\-]+\.)+\*" },
                ],
            },
            new() { Scope = "number", Begin = @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d{1,5})?\b" },
            new() { Scope = "number", Begin = @"\b\d+[kKmMgGdshdwy]?\b" },
            varMode,
        };

        var defaultMode = new Mode
        {
            EndsWithParent = true,
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["literal"] = Literals }),
            KeywordPattern = @"[a-z_]{2,}|\/dev\/poll",
            Illegal = "=>",
            Contains = defaultContains,
        };

        return new Mode
        {
            Illegal = @"[^\s\}\{]",
            Contains =
            [
                CommonModes.HashCommentMode,
                new()
                {
                    BeginKeywords = ["upstream", "location"],
                    End = @";|\{",
                    Contains = defaultContains,
                    Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal) { ["section"] = "upstream location" }),
                },
                new() { Scope = "section", Begin = CommonModes.UnderscoreIdentRe + @"(?=\s+\{)" },
                new()
                {
                    Begin = @"(?=" + CommonModes.UnderscoreIdentRe + @"\s)",
                    End = @";|\{",
                    Contains =
                    [
                        new()
                        {
                            Scope = "attribute",
                            Begin = CommonModes.UnderscoreIdentRe,
                            Starts = defaultMode,
                        },
                    ],
                },
            ],
        };
    }
}
