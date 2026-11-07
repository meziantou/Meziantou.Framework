using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// Keywords based on http://community.schemewiki.org/?scheme-keywords
internal static class Scheme
{
    private const string IdentRe = @"[^\(\)\[\]\{\}"",'`;#|\\\s]+";
    private const string SimpleNumberRe = @"(-|\+)?\d+([./]\d+)?";
    private const string ComplexNumberRe = SimpleNumberRe + @"[+\-]" + SimpleNumberRe + "i";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["built_in"] =
                "case-lambda call/cc class define-class exit-handler field import "
                + "inherit init-field interface let*-values let-values let/ec mixin "
                + "opt-lambda override protect provide public rename require "
                + "require-for-syntax syntax syntax-case syntax-error unit/sig unless "
                + "when with-syntax and begin call-with-current-continuation "
                + "call-with-input-file call-with-output-file case cond define "
                + "define-syntax delay do dynamic-wind else for-each if lambda let let* "
                + "let-syntax letrec letrec-syntax map or syntax-rules ' * + , ,@ - ... / "
                + "; < <= = => > >= ` abs acos angle append apply asin assoc assq assv atan "
                + "boolean? caar cadr call-with-input-file call-with-output-file "
                + "call-with-values car cdddar cddddr cdr ceiling char->integer "
                + "char-alphabetic? char-ci<=? char-ci<? char-ci=? char-ci>=? char-ci>? "
                + "char-downcase char-lower-case? char-numeric? char-ready? char-upcase "
                + "char-upper-case? char-whitespace? char<=? char<? char=? char>=? char>? "
                + "char? close-input-port close-output-port complex? cons cos "
                + "current-input-port current-output-port denominator display eof-object? "
                + "eq? equal? eqv? eval even? exact->inexact exact? exp expt floor "
                + "force gcd imag-part inexact->exact inexact? input-port? integer->char "
                + "integer? interaction-environment lcm length list list->string "
                + "list->vector list-ref list-tail list? load log magnitude make-polar "
                + "make-rectangular make-string make-vector max member memq memv min "
                + "modulo negative? newline not null-environment null? number->string "
                + "number? numerator odd? open-input-file open-output-file output-port? "
                + "pair? peek-char port? positive? procedure? quasiquote quote quotient "
                + "rational? rationalize read read-char real-part real? remainder reverse "
                + "round scheme-report-environment set! set-car! set-cdr! sin sqrt string "
                + "string->list string->number string->symbol string-append string-ci<=? "
                + "string-ci<? string-ci=? string-ci>=? string-ci>? string-copy "
                + "string-fill! string-length string-ref string-set! string<=? string<? "
                + "string=? string>=? string>? string? substring symbol->string symbol? "
                + "tan transcript-off transcript-on truncate values vector "
                + "vector->list vector-fill! vector-length vector-ref vector-set! "
                + "with-input-from-file with-output-to-file write write-char zero?",
        });

        var literal = new Mode
        {
            Scope = "literal",
            Begin = @"(#t|#f|#\\" + IdentRe + @"|#\\.)",
        };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                // Deviation from highlight.js, which lists the complex number after the simple one, so it never matches
                // (1+2i is the number 1, the number +2 and i). RunStart: the pattern fails identically from every position
                // of a run of digits, which is quadratic on a long one.
                new Mode { Begin = CommonModes.RunStart(@"\d") + ComplexNumberRe },
                new Mode { Begin = SimpleNumberRe },
                new Mode { Begin = "#b[0-1]+(/[0-1]+)?" },
                new Mode { Begin = "#o[0-7]+(/[0-7]+)?" },

                // Deviation from highlight.js, which only accepts lowercase hexadecimal digits (#xff but not #xFF).
                new Mode { Begin = "#x[0-9a-fA-F]+(/[0-9a-fA-F]+)?" },
            ],
        };

        var @string = CommonModes.QuoteStringMode;

        Mode[] comments =
        [
            CommonModes.Comment(";", "$"),
            CommonModes.Comment(@"#\|", @"\|#"),
        ];

        // Where a number starts, the number wins (it comes first in every mode that contains both), so the identifier
        // does not start there either. Otherwise, in `1-1-1-...` every number is followed by a match of the identifier
        // over the whole rest of the run, which is quadratic on a long one.
        var ident = new Mode { Begin = @"(?![-+]?\d)" + IdentRe };

        var quotedIdent = new Mode
        {
            Scope = "symbol",
            Begin = "'" + IdentRe,
        };

        var body = new Mode { EndsWithParent = true };

        var quotedListContent = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };
        quotedListContent.Contains = [Mode.Self, literal, @string, number, ident, quotedIdent];

        var quotedList = new Mode
        {
            Variants =
            [
                new Mode { Begin = "'" },
                new Mode { Begin = "`" },
            ],
            Contains = [quotedListContent],
        };

        var name = new Mode
        {
            Scope = "name",
            Begin = IdentRe,
            KeywordPattern = IdentRe,
            Keywords = keywords,
        };

        var lambda = new Mode
        {
            Begin = "lambda",
            EndsWithParent = true,
            ReturnBegin = true,
            Contains =
            [
                name,
                new Mode
                {
                    EndsParent = true,
                    Variants =
                    [
                        new Mode { Begin = @"\(", End = @"\)" },
                        new Mode { Begin = @"\[", End = @"\]" },
                    ],
                    Contains = [ident],
                },
            ],
        };

        var list = new Mode
        {
            Variants =
            [
                new Mode { Begin = @"\(", End = @"\)" },
                new Mode { Begin = @"\[", End = @"\]" },
            ],
            Contains = [lambda, name, body],
        };

        body.Contains = [literal, number, @string, ident, quotedIdent, quotedList, list, .. comments];

        return new Mode
        {
            Illegal = @"\S",
            Contains =
            [
                // highlight.js only accepts a shebang at the start of the document.
                new Mode { Scope = "meta", Begin = @"\A#![ ]*/", End = "$" },
                number,
                @string,
                quotedIdent,
                quotedList,
                list,
                .. comments,
            ],
        };
    }
}
