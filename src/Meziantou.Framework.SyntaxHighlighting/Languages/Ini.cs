using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Ini
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comments = new Mode
        {
            Scope = "comment",
            Variants =
            [
                new Mode { Begin = ";", End = "$" },
                new Mode { Begin = "#", End = "$" },
            ],
            Contains = [],
        };

        const string BareKey = @"[A-Za-z0-9_-]+";
        const string QuotedKeyDouble = @"""(\\""|[^""])*""";
        const string QuotedKeySingle = @"'[^']*'";
        var anyKey = "(?:" + BareKey + "|" + QuotedKeyDouble + "|" + QuotedKeySingle + ")";
        var dottedKey = anyKey + @"(\s*\.\s*" + anyKey + @")*(?=\s*=\s*[^#\s])";

        return new Mode
        {
            CaseInsensitive = true,
            Illegal = @"\S",
            Contains =
            [
                comments,
                new() { Scope = "section", Begin = @"\[+", End = @"\]+" },
                new()
                {
                    Scope = "attr",
                    Begin = dottedKey,
                    Starts = new Mode
                    {
                        End = "$",
                        Contains = [],
                    },
                },
            ],
        };
    }
}
