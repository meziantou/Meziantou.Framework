using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Lua
{
    // Deviation from highlight.js, whose long brackets end at any closing bracket (`]]` ends `[==[`) and nest: a long
    // bracket ends at the closing bracket of the same level (`[==[ ]] ]==]`), and does not nest, as in Lua 5.1+.
    private const string OpeningLongBracketRe = @"\[(=*)\[";
    private const string ClosingLongBracketRe = @"\](=*)\]";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        Mode[] comments =
        [
            CommonModes.Comment(@"--(?!\[=*\[)", "$"),
            new Mode(CommonModes.Comment("--" + OpeningLongBracketRe, ClosingLongBracketRe)) { EndSameAsBegin = true },
        ];

        return new Mode
        {
            KeywordPattern = CommonModes.UnderscoreIdentRe,
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["literal"] = "true false nil",
                ["keyword"] = "and break do else elseif end for goto if in local not or repeat return then until while",
                ["built_in"] =
                    // Metatags and globals:
                    "_G _ENV _VERSION __index __newindex __mode __call __metatable __tostring __len "
                    + "__gc __add __sub __mul __div __mod __pow __concat __unm __eq __lt __le assert "
                    // Standard methods and properties:
                    + "collectgarbage dofile error getfenv getmetatable ipairs load loadfile loadstring "
                    + "module next pairs pcall print rawequal rawget rawset require select setfenv "
                    + "setmetatable tonumber tostring type unpack xpcall arg self "
                    // Library methods and properties (one line per library):
                    + "coroutine resume yield status wrap create running debug getupvalue "
                    + "debug sethook getmetatable gethook setmetatable setlocal traceback setfenv getinfo setupvalue getlocal getregistry getfenv "
                    + "io lines write close flush open output type read stderr stdin input stdout popen tmpfile "
                    + "math log max acos huge ldexp pi cos tanh pow deg tan cosh sinh random randomseed frexp ceil floor rad abs sqrt modf asin min mod fmod log10 atan2 exp sin atan "
                    + "os exit setlocale date getenv difftime remove time clock tmpname rename execute package preload loadlib loaded loaders cpath config path seeall "
                    + "string sub upper len gfind rep find match char dump gmatch reverse byte format gsub lower "
                    + "table setn insert getn foreachi maxn foreach concat sort remove",
            }),
            Contains =
            [
                .. comments,
                new Mode
                {
                    Scope = "function",
                    BeginKeywords = ["function"],
                    End = @"\)",
                    Contains =
                    [
                        new Mode { Scope = "title", Begin = @"([_a-zA-Z]\w*\.)*([_a-zA-Z]\w*:)?[_a-zA-Z]\w*" },
                        new Mode
                        {
                            Scope = "params",
                            Begin = @"\(",
                            EndsWithParent = true,
                            Contains = comments,
                        },
                        .. comments,
                    ],
                },
                CommonModes.CNumberMode,
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
                new Mode
                {
                    Scope = "string",
                    Begin = OpeningLongBracketRe,
                    End = ClosingLongBracketRe,
                    EndSameAsBegin = true,
                },
            ],
        };
    }
}
