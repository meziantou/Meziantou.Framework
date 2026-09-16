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

        // A key that continues a bare key (or a dotted key) begun earlier would end on the same `=`, so
        // it can never be the leftmost match; skipping it keeps a long key from being rescanned from each
        // of its characters. A quote right after a backslash is an escaped quote inside a quoted key, but
        // a bare key may well follow a backslash.
        var dottedKeyStart = @"(?:\G|(?<![A-Za-z0-9_\-])(?=[A-Za-z0-9_\-])|(?<!\\)(?=[""']))(?<![A-Za-z0-9_\-""']\s*\.\s*)";
        var dottedKey = dottedKeyStart + anyKey + @"(\s*\.\s*" + anyKey + @")*(?=\s*=\s*[^#\s])";

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
