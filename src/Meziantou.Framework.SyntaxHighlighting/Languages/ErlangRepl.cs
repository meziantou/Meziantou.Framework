using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <remarks>
/// As in highlight.js, this is not the Erlang grammar behind a prompt: the whole session, output included, is
/// highlighted by a small grammar of its own (prompts, comments, numbers, strings and keywords), because the output of
/// the Erlang shell is made of Erlang terms and an input can span several lines without a continuation prompt.
/// </remarks>
internal static class ErlangRepl
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["built_in"] = "spawn spawn_link self",
            ["keyword"] = "after and andalso|10 band begin bnot bor bsl bsr bxor case catch cond div end fun if let not of or orelse|10 query receive rem try when xor",
        });

        return new Mode
        {
            Keywords = keywords,
            Contains =
            [
                new Mode
                {
                    Scope = "meta.prompt",
                    Begin = "^[0-9]+> ",
                },
                CommonModes.Comment("%", "$"),
                new Mode
                {
                    Scope = "number",
                    Begin = @"\b(\d+(_\d+)*#[a-fA-F0-9]+(_[a-fA-F0-9]+)*|\d+(_\d+)*(\.\d+(_\d+)*)?([eE][-+]?\d+)?)",
                },
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
                new Mode { Begin = @"\?(::)?([A-Z]\w*)((::)[A-Z]\w*)*" },
                new Mode { Begin = "->" },

                // Deviation from highlight.js: `ok` must be a whole word. Upstream, it also matched the start of an atom
                // such as `okend`, and the rest of the atom was highlighted as a keyword.
                new Mode { Begin = @"\bok\b" },
                new Mode { Begin = "!" },
                new Mode
                {
                    Begin = @"(\b[a-z'][a-zA-Z0-9_']*:[a-z'][a-zA-Z0-9_']*)|(\b[a-z'][a-zA-Z0-9_']*)",

                    // Deviation from highlight.js: the keywords are highlighted in an atom. Upstream, this mode had
                    // none, and as every keyword is an atom, the keywords and built-ins of the grammar were never
                    // highlighted.
                    Keywords = keywords,
                },
                new Mode { Begin = "[A-Z][a-zA-Z0-9_']*" },
            ],
        };
    }
}
