using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Wasm
{
    private const string KeywordList =
        "anyfunc block br br_if br_table call call_indirect data drop elem else end export func global.get global.set " +
        "local.get local.set local.tee get_global get_local global if import local loop memory memory.grow memory.size " +
        "module mut nop offset param result return select set_global set_local start table tee_local then type unreachable";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var blockComment = CommonModes.Comment(@"\(;", ";\\)");
        blockComment.Contains.Add(Mode.Self);

        return new Mode
        {
            KeywordPattern = @"[\w.]+",
            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = KeywordList,
            }),
            Contains =
            [
                CommonModes.Comment(";;", "$"),
                blockComment,
                new Mode
                {
                    BeginParts = ["(?:offset|align)", @"\s*", "="],
                    BeginScope = new Dictionary<int, string> { [1] = "keyword", [3] = "operator" },
                },
                new Mode { Scope = "variable", Begin = @"\$[\w_]+" },
                new Mode { Scope = "punctuation", Begin = @"(\((?!;)|\))+" },
                new Mode
                {
                    BeginParts = ["(?:func|call|call_indirect)", @"\s+", @"\$[^\s)]+"],
                    BeginScope = new Dictionary<int, string> { [1] = "keyword", [3] = "title.function" },
                },
                CommonModes.QuoteStringMode,

                // The lookahead keeps the type of an instruction (`i32.add`) from being matched alone.
                new Mode { Scope = "type", Begin = @"(i32|i64|f32|f64)(?!\.)" },
                new Mode
                {
                    Scope = "keyword",
                    Begin = @"\b(f32|f64|i32|i64)(?:\.(?:abs|add|and|ceil|clz|const|convert_[su]/i(?:32|64)|copysign|ctz|demote/f64|div(?:_[su])?|eqz?|extend_[su]/i32|floor|ge(?:_[su])?|gt(?:_[su])?|le(?:_[su])?|load(?:(?:8|16|32)_[su])?|lt(?:_[su])?|max|min|mul|nearest|neg?|or|popcnt|promote/f32|reinterpret/[fi](?:32|64)|rem_[su]|rot[lr]|shl|shr_[su]|store(?:8|16|32)?|sqrt|sub|trunc(?:_[su]/f(?:32|64))?|wrap/i64|xor))\b",
                },

                // Deviation from highlight.js, whose hexadecimal fraction and NaN payload only accept `A-D` after their first
                // digit (a typo for `A-F`), so `0x1.EFp1` or `nan:0x7F` were only partly highlighted as a number.
                new Mode
                {
                    Scope = "number",
                    Begin = @"[+-]?\b(?:\d(?:_?\d)*(?:\.\d(?:_?\d)*)?(?:[eE][+-]?\d(?:_?\d)*)?|0x[\da-fA-F](?:_?[\da-fA-F])*(?:\.[\da-fA-F](?:_?[\da-fA-F])*)?(?:[pP][+-]?\d(?:_?\d)*)?)\b|\binf\b|\bnan(?::0x[\da-fA-F](?:_?[\da-fA-F])*)?\b",
                },
            ],
        };
    }
}
