using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Ocaml
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            KeywordPattern = @"[a-z_]\w*!?",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] =
                    "and as assert asr begin class constraint do done downto else end "
                    + "exception external for fun function functor if in include "
                    + "inherit! inherit initializer land lazy let lor lsl lsr lxor match method!|10 method "
                    + "mod module mutable new object of open! open or private rec sig struct "
                    + "then to try type val! val virtual when while with "
                    // camlp4:
                    + "parser value "
                    // Deviation from highlight.js, which misses `nonrec` (OCaml 4.02).
                    + "nonrec",
                ["built_in"] =
                    // Built-in types. Deviation from highlight.js, which misses `option`.
                    "array bool bytes char exn|5 float int int32 int64 list lazy_t|5 nativeint|5 option string unit "
                    // (Some) types in Pervasives:
                    + "in_channel out_channel ref",
                ["literal"] = "true false",
            }),
            Illegal = @"//|>>",
            Contains =
            [
                new Mode
                {
                    Scope = "literal",
                    Begin = @"\[(\|\|)?\]|\(\)",
                },
                CommonModes.Comment(@"\(\*", @"\*\)", extraContains: [Mode.Self]),

                // Type variable. The grammar is ambiguous on how 'a'b should be interpreted, but not the compiler.
                new Mode
                {
                    Scope = "symbol",
                    Begin = @"'[A-Za-z_](?!')[\w']*",
                },

                // Polymorphic variant.
                new Mode
                {
                    Scope = "type",
                    Begin = @"`[A-Z][\w']*",
                },

                // Module or constructor.
                new Mode
                {
                    Scope = "type",
                    Begin = @"\b[A-Z][\w']*",
                },

                // Don't color identifiers, but safely catch all identifiers with '. `[a-z_]\w*'[\w']*` in highlight.js,
                // which is quadratic on a long run of word characters that is not followed by an apostrophe.
                new Mode
                {
                    Begin = CommonModes.RunStart(@"\w", "a-z_") + @"[a-z_]\w*'[\w']*",
                },

                // Deviation from highlight.js, which does not support quoted strings (`{|...|}`, `{id|...|id}`, OCaml
                // 4.02): their content is raw text, which can contain quotes, apostrophes and comment delimiters.
                new Mode
                {
                    Scope = "string",
                    Begin = @"\{([a-z_]*)\|",
                    End = @"\|([a-z_]*)\}",
                    EndSameAsBegin = true,
                },
                CommonModes.AposStringMode,
                new Mode(CommonModes.QuoteStringMode) { Illegal = null },
                new Mode
                {
                    Scope = "number",
                    Begin = @"\b(0[xX][a-fA-F0-9_]+[Lln]?|0[oO][0-7_]+[Lln]?|0[bB][01_]+[Lln]?|[0-9][0-9_]*([Lln]|(\.[0-9_]*)?([eE][-+]?[0-9_]+)?)?)",
                },

                // Relevance booster.
                new Mode { Begin = "->" },
            ],
        };
    }
}
