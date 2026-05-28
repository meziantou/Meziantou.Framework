using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Dos
{
    private static readonly string[] Keywords =
    [
        "if","else","goto","for","in","do","call","exit","not","exist",
        "errorlevel","defined","equ","neq","lss","leq","gtr","geq",
    ];

    private static readonly string[] BuiltIns =
    [
        "prn","nul","lpt3","lpt2","lpt1","con","com4","com3","com2","com1","aux",
        "shift","cd","dir","echo","setlocal","endlocal","set","pause","copy",
        "append","assoc","at","attrib","break","cacls","cd","chcp","chdir",
        "chkdsk","chkntfs","cls","cmd","color","comp","compact","convert","date",
        "dir","diskcomp","diskcopy","doskey","erase","fs","find","findstr","format",
        "ftype","graftabl","help","keyb","label","md","mkdir","mode","more","move",
        "path","pause","print","popd","pushd","promt","rd","recover","rem","rename",
        "replace","restore","rmdir","shift","sort","start","subst","time","title",
        "tree","type","ver","verify","vol","ping","net","ipconfig","taskkill",
        "xcopy","ren","del",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = CommonModes.Comment(@"^\s*@?rem\b", "$");
        const string LabelBegin = @"^\s*[A-Za-z._?][A-Za-z0-9_$#@~.?]*(:|\s+label)";

        return new Mode
        {
            CaseInsensitive = true,
            Illegal = @"/\*",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = Keywords,
                ["built_in"] = BuiltIns,
            }),
            Contains =
            [
                new() { Scope = "variable", Begin = @"%%[^ ]|%[^ ]+?%|![^ ]+?!" },
                new()
                {
                    Scope = "function",
                    Begin = LabelBegin,
                    End = "goto:eof",
                    Contains =
                    [
                        new() { Scope = "title", Begin = @"([_a-zA-Z]\w*\.)*([_a-zA-Z]\w*:)?[_a-zA-Z]\w*" },
                        comment,
                    ],
                },
                new() { Scope = "number", Begin = @"\b\d[\dA-F]*" },
                comment,
            ],
        };
    }
}
