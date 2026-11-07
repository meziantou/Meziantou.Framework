using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// Generic Lisp syntax (Common Lisp, Emacs Lisp, ...).
internal static class Lisp
{
    private const string IdentRe = @"[a-zA-Z_\-+\*\/<=>&#][a-zA-Z0-9_\-+*\/<=>&#!]*";

    // A symbol between vertical bars (multiple escape characters): |foo bar|.
    private const string MecRe = @"\|[\s\S]*?\|";
    private const string SimpleNumberRe = @"(-|\+)?\d+(\.\d+|\/\d+)?((d|e|f|l|s|D|E|F|L|S)(\+|-)?\d+)?";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var literal = new Mode
        {
            Scope = "literal",
            Begin = @"\b(t{1}|nil)\b",
        };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Begin = SimpleNumberRe },
                new Mode { Begin = "#(b|B)[0-1]+(/[0-1]+)?" },
                new Mode { Begin = "#(o|O)[0-7]+(/[0-7]+)?" },
                new Mode { Begin = "#(x|X)[0-9a-fA-F]+(/[0-9a-fA-F]+)?" },
                new Mode { Begin = @"#(c|C)\(" + SimpleNumberRe + " +" + SimpleNumberRe, End = @"\)" },
            ],
        };

        var @string = new Mode(CommonModes.QuoteStringMode) { Illegal = null };
        var comment = CommonModes.Comment(";", "$");

        // Deviation from highlight.js, which does not support block comments (#| ... |#, which nest in Common Lisp).
        var blockComment = CommonModes.Comment(@"#\|", @"\|#", extraContains: [Mode.Self]);

        // Deviation from highlight.js, which does not know that a backslash escapes the next character (Common Lisp
        // characters such as #\( #\" #\;, Emacs Lisp characters such as ?\(, escaped characters in symbols): the character
        // starts a list, a string or a comment, which unbalances the parentheses or swallows the rest of the document.
        var escape = new Mode { Begin = @"\\[\s\S]" };

        // Deviation from highlight.js, whose variable (*name*) ends at the next `*`, wherever it is: a lone `*` that is
        // not the head of a list, such as (list 1 * 2), swallows the rest of the document. It also ends before a space or
        // a parenthesis.
        var variable = new Mode { Begin = @"\*", End = @"\*|(?=[\s()])" };
        var keyword = new Mode
        {
            Scope = "symbol",
            Begin = "[:&]" + IdentRe,
        };

        // Where a number starts, the number wins (it comes first in every mode that contains both), so the identifier
        // does not start there either. Otherwise, in `-1-1-1...` every number is followed by a match of the identifier
        // over the whole rest of the run, which is quadratic on a long one.
        var ident = new Mode { Begin = @"(?![-+]?\d)" + IdentRe };
        var mec = new Mode { Begin = MecRe };

        var quotedList = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };
        quotedList.Contains = [Mode.Self, escape, literal, @string, number, ident];

        var quoted = new Mode
        {
            Contains = [escape, number, @string, variable, keyword, quotedList, ident],
            Variants =
            [
                new Mode { Begin = @"['`]\(", End = @"\)" },
                new Mode
                {
                    Begin = @"\(quote ",
                    End = @"\)",
                    Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["name"] = "quote",
                    }),
                },
                new Mode { Begin = "'" + MecRe },
            ],
        };

        var quotedAtom = new Mode
        {
            Variants =
            [
                new Mode { Begin = "'" + IdentRe },
                new Mode { Begin = "#'" + IdentRe + "(::" + IdentRe + ")*" },
            ],
        };

        var list = new Mode
        {
            Begin = @"\(\s*",
            End = @"\)",
        };

        var body = new Mode { EndsWithParent = true };

        list.Contains =
        [
            new Mode
            {
                Scope = "name",
                Variants =
                [
                    new Mode { Begin = IdentRe },
                    new Mode { Begin = MecRe },
                ],
            },
            body,
        ];

        body.Contains = [escape, blockComment, quoted, quotedAtom, list, literal, number, @string, comment, variable, keyword, mec, ident];

        return new Mode
        {
            Illegal = @"\S",
            Contains =
            [
                number,

                // highlight.js only accepts a shebang at the start of the document.
                new Mode { Scope = "meta", Begin = @"\A#![ ]*/", End = "$" },
                literal,
                @string,
                comment,
                blockComment,
                escape,
                quoted,
                quotedAtom,
                list,
                ident,
            ],
        };
    }
}
