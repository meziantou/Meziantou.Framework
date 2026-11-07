using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// https://nim-lang.org/docs/manual.html
internal static class Nim
{
    // A generalized raw string literal starts with an identifier (`r"..."`, `fmt"..."`). RunStart only lets the pattern
    // start at the first letter of an identifier: it fails identically from every later position, which is quadratic on
    // a long identifier that is not followed by a quote.
    private static readonly string StringPrefixRe = CommonModes.RunStart(@"\w", "a-zA-Z") + @"[a-zA-Z]\w*";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // Deviation from highlight.js, which only knows line comments: `#[ ... ]#` is a (nestable) block comment, and
        // `##[ ... ]##` a (nestable) documentation block comment, whose following lines highlight.js renders as code. As in
        // the Nim lexer, a documentation comment only nests `##[` and only ends at `]##`.
        var blockComment = CommonModes.Comment(@"#\[", @"\]#");
        blockComment.Contains = [Mode.Self, .. blockComment.Contains];
        var docBlockComment = CommonModes.Comment(@"##\[", @"\]##");
        docBlockComment.Contains = [Mode.Self, .. docBlockComment.Contains];

        return new Mode
        {
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] =
                    "addr and as asm bind block break case cast concept const continue converter defer discard distinct div do "
                    + "elif else end enum except export finally for from func generic guarded if import in include interface is "
                    + "isnot iterator let macro method mixin mod nil not notin object of or out proc ptr raise ref return shared "
                    + "shl shr static template try tuple type using var when while with without xor yield",
                ["literal"] = "true false",
                ["type"] =
                    "int int8 int16 int32 int64 uint uint8 uint16 uint32 uint64 float float32 float64 bool char string cstring "
                    + "pointer expr stmt void auto any range array openarray varargs seq set clong culong cchar cschar cshort "
                    + "cint csize clonglong cfloat cdouble clongdouble cuchar cushort cuint culonglong cstringarray semistatic",
                ["built_in"] = "stdin stdout stderr result",
            }),
            Contains =
            [
                // Pragma
                new Mode { Scope = "meta", Begin = @"\{\.", End = @"\.\}" },
                new Mode
                {
                    Scope = "string",
                    Begin = StringPrefixRe + "\"",
                    End = "\"",
                    Contains = [new Mode { Begin = "\"\"" }],
                },
                new Mode
                {
                    Scope = "string",
                    Begin = "(" + StringPrefixRe + ")?\"\"\"",
                    End = "\"\"\"",
                },
                CommonModes.QuoteStringMode,

                // Deviation from highlight.js, which has no character literals: `'"'` would start a string there.
                new Mode { Scope = "string", Match = @"'(?:\\(?:x[0-9a-fA-F]{2}|\d+|[^\n])|[^\\'\n])'" },
                new Mode { Scope = "type", Begin = @"\b[A-Z]\w+\b" },
                new Mode
                {
                    Scope = "number",
                    Variants =
                    [
                        new Mode { Begin = @"\b(0[xX][0-9a-fA-F][_0-9a-fA-F]*)('?[iIuU](8|16|32|64))?" },
                        new Mode { Begin = @"\b(0o[0-7][_0-7]*)('?[iIuUfF](8|16|32|64))?" },
                        new Mode { Begin = @"\b(0(b|B)[01][_01]*)('?[iIuUfF](8|16|32|64))?" },
                        // Deviation from highlight.js, which has no float literals (`3.14` is two numbers there): a decimal
                        // literal can have a fraction and an exponent. The fraction needs a digit, so a range (`1..10`) is
                        // not a float.
                        new Mode { Begin = @"\b(\d[_\d]*)(\.\d[_\d]*)?([eE][+-]?\d[_\d]*)?('?[iIuUfF](8|16|32|64))?" },
                    ],
                },
                docBlockComment,
                blockComment,
                CommonModes.HashCommentMode,
            ],
        };
    }
}
