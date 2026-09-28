using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Properties
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // whitespaces: space, tab, formfeed
        const string Ws0 = @"[ \t\f]*";
        const string Ws1 = @"[ \t\f]+";

        // delimiter
        const string EqualDelim = Ws0 + "[:=]" + Ws0;
        const string WsDelim = Ws1;
        const string Delim = "(" + EqualDelim + "|" + WsDelim + ")";
        const string KeyChars = @"^\\:= \t\f\n";
        const string Key = @"([" + KeyChars + @"]|\\.)+";

        // A key that starts where a key that begins one or two characters earlier would also match can never be the
        // leftmost match. Skipping those positions keeps a long key from being rescanned from each of its characters.
        // A key starting at p also matches from:
        // - p-1 when p-1 is a plain key character;
        // - p-2 when p-2 is a backslash (the escape `\x` covers p-1), e.g. in a run of backslashes;
        // - p-1 when p-1 is a backslash and p is a plain key character (the escape `\x` covers p).
        // The earlier position must not be before the scan start (\G).
        const string NotAfterKeyChar = "(?<![" + KeyChars + "])";
        const string NotAfterEscape = @"(?<!\\[^\n])";
        const string NotEscaped = @"(?!(?<=\\)[" + KeyChars + "])";
        const string KeyStart = @"(?:\G|(?<=\G.)" + NotAfterKeyChar + NotEscaped + "|" + NotAfterKeyChar + NotAfterEscape + NotEscaped + ")";

        var delimAndValue = new Mode
        {
            // skip DELIM
            End = Delim,
            Starts = new Mode
            {
                // value: everything until end of line (again, taking into account backslashes)
                Scope = "string",
                End = "$",
                Contains =
                [
                    new Mode { Begin = @"\\\\" },
                    new Mode { Begin = @"\\\n" },
                ],
            },
        };

        return new Mode
        {
            CaseInsensitive = true,
            Illegal = @"\S",
            Contains =
            [
                // Deviation from highlight.js: the indentation cannot contain line breaks (`^\s*` upstream), so the
                // blank lines before a comment are not part of it.
                CommonModes.Comment(@"^[^\S\n]*[!#]", "$"),

                // key: everything until whitespace or = or : (taking into account backslashes)
                // case of a key-value pair
                new Mode
                {
                    ReturnBegin = true,
                    Variants =
                    [
                        new Mode { Begin = KeyStart + Key + EqualDelim },
                        new Mode { Begin = KeyStart + Key + WsDelim },
                    ],
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "attr",
                            Begin = Key,
                            EndsParent = true,
                        },
                    ],
                    Starts = delimAndValue,
                },

                // case of an empty key
                new Mode
                {
                    Scope = "attr",
                    Begin = KeyStart + Key + Ws0 + "$",
                },
            ],
        };
    }
}
