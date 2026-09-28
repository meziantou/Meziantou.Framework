using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class AsciiDoc
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    // What may separate two candidates for FirstCandidate: any character of the line, or of the paragraph (where a line
    // break must be between two non-empty lines).
    private const string LineCharacter = @"[^\n]";
    private const string ParagraphCharacter = @"(?:[^\n]|(?<!\n\n)\n(?!\n))";

    /// <summary>
    /// A zero-width assertion that matches a <paramref name="candidate"/>, unless another one precedes it in the same line
    /// or paragraph (<paramref name="between"/>), at or after the scan start (<c>\G</c>).
    /// </summary>
    /// <remarks>
    /// Not in highlight.js. It is for patterns that scan the rest of the line or paragraph for a closing mark, and whose
    /// closing marks for a candidate are also closing marks for any earlier candidate: when the first candidate finds no
    /// closing mark, no later one does, and trying each of them is quadratic on a long line or paragraph with many
    /// unclosed marks. This does not change what matches: an earlier candidate is either the leftmost match or proves
    /// that there is none. The candidate is checked first, so that the scan back for an earlier one only runs from a
    /// candidate, and stops at the previous one.
    /// </remarks>
    private static string FirstCandidate(string candidate, string between) => "(?=" + candidate + @")(?:\G|(?<!" + candidate + @"(?:(?!\G)" + between + ")*?))";

    // Deviation from highlight.js, whose multi-line constrained strong and emphasis (`*[^\s]([^\n]+\n)+([^\n]+)*`) have
    // none of the word-boundary checks of their single-line forms, and end at the last mark of the paragraph: the
    // underscores of `snake_case` identifiers, or `2*3`, started a strong or emphasis span that ran over the following
    // lines to the last `_` or `*` of the paragraph. Like the single-line forms, the opening mark cannot follow a word
    // character, the closing one cannot be preceded by a space or followed by a word character, and the span ends at
    // the first closing mark, as in Asciidoctor (which also lets the first line end right after the opening mark and its
    // first character, where highlight.js wanted one more). The lines in between still cannot be blank, so a span never
    // leaves its paragraph.
    private static string ConstrainedMultiLine(string mark) =>
        FirstCandidate(@"(?<!\w)" + mark + @"(?=\S)", ParagraphCharacter)
        + mark + @"\S[^\n]*\n(?:[^\n]+\n)*?[^\n]*?\S" + mark + @"(?!\w)";

    private static Mode CreateMode()
    {
        Mode[] escapedFormatting =
        [
            // Escaped constrained formatting marks (`\*`, `\_` or `` \` ``).
            new Mode { Begin = @"\\[*_`]" },

            // Escaped unconstrained formatting marks (`\\**`, `\\__` or ``\\``) are ignored up to the next formatting marks.
            new Mode { Begin = @"\\\\\*{2}[^\n]*?\*{2}" },
            new Mode { Begin = @"\\\\_{2}[^\n]*_{2}" },
            new Mode { Begin = @"\\\\`{2}[^\n]*`{2}" },

            // A constrained formatting mark cannot follow `:`, `;` or `}`.
            new Mode { Begin = @"[:;}][*_`](?![*_`])" },
        ];

        Mode[] strong =
        [
            // Unconstrained, single line.
            new Mode { Scope = "strong", Begin = @"\*{2}([^\n]+?)\*{2}" },

            // Unconstrained, multi-line.
            new Mode { Scope = "strong", Begin = @"\*\*((\*(?!\*)|\\[^\n]|[^*\n\\])+\n)+(\*(?!\*)|\\[^\n]|[^*\n\\])*\*\*" },

            // Constrained, single line: cannot be preceded or followed by a word character.
            new Mode { Scope = "strong", Begin = FirstCandidate(@"\B\*(?=\S)", LineCharacter) + @"\*(\S|\S[^\n]*?\S)\*(?!\w)" },

            // Constrained, multi-line.
            new Mode { Scope = "strong", Begin = ConstrainedMultiLine(@"\*") },
        ];

        Mode[] emphasis =
        [
            // Unconstrained, single line.
            new Mode { Scope = "emphasis", Begin = @"_{2}([^\n]+?)_{2}" },

            // Unconstrained, multi-line.
            new Mode { Scope = "emphasis", Begin = @"__((_(?!_)|\\[^\n]|[^_\n\\])+\n)+(_(?!_)|\\[^\n]|[^_\n\\])*__" },

            // Constrained, single line: cannot be preceded or followed by a word character.
            new Mode { Scope = "emphasis", Begin = FirstCandidate(@"\b_(?=\S)", LineCharacter) + @"_(\S|\S[^\n]*?\S)_(?!\w)" },

            // Constrained, multi-line.
            new Mode { Scope = "emphasis", Begin = ConstrainedMultiLine("_") },

            // Constrained, with single quotes (legacy): cannot follow a word character or be followed by a quote or a space.
            new Mode
            {
                Scope = "emphasis",
                Begin = @"\B'(?!['\s])",
                End = @"(\n{2}|')",

                // An escaped quote followed by a word character.
                Contains = [new Mode { Begin = @"\\'\w" }],
            },
        ];

        return new Mode
        {
            Contains =
            [
                // Block comment.
                CommonModes.Comment(@"^/{4,}\n", @"\n/{4,}$"),

                // Line comment.
                CommonModes.Comment("^//", "$"),

                // Block title.
                new Mode { Scope = "title", Begin = @"^\.\w.*$" },

                // Example, admonition and sidebar blocks.
                new Mode { Begin = @"^[=\*]{4,}\n", End = @"\n^[=\*]{4,}$" },

                // Headings.
                new Mode
                {
                    Scope = "section",
                    Variants =
                    [
                        new Mode { Begin = @"^(={1,6})[ \t].+?([ \t]\1)?$" },
                        new Mode { Begin = @"^[^\[\]\n]+?\n[=\-~\^\+]{2,}$" },
                    ],
                },

                // Document attributes.
                new Mode { Scope = "meta", Begin = "^:.+?:", End = @"\s", ExcludeEnd = true },

                // Block attributes.
                new Mode { Scope = "meta", Begin = @"^\[.+?\]$" },

                // Quote blocks.
                new Mode { Scope = "quote", Begin = @"^_{4,}\n", End = @"\n_{4,}$" },

                // Listing and literal blocks.
                new Mode { Scope = "code", Begin = @"^[\-\.]{4,}\n", End = @"\n[\-\.]{4,}$" },

                // Passthrough blocks.
                new Mode
                {
                    Begin = @"^\+{4,}\n",
                    End = @"\n\+{4,}$",
                    Contains = [new Mode { Begin = "<", End = ">", SubLanguage = "xml" }],
                },

                // Bullet lists.
                new Mode { Scope = "bullet", Begin = @"^(\*+|-+|\.+|[^\n]+?::)\s+" },

                // Admonitions.
                new Mode { Scope = "symbol", Begin = @"^(NOTE|TIP|IMPORTANT|WARNING|CAUTION):\s+" },

                .. escapedFormatting,
                .. strong,
                .. emphasis,

                // Inline smart quotes.
                new Mode
                {
                    Scope = "string",
                    Variants =
                    [
                        new Mode { Begin = FirstCandidate("``", LineCharacter) + "``.+?''" },
                        new Mode { Begin = FirstCandidate("`", LineCharacter) + "`.+?'" },
                    ],
                },

                // Inline unconstrained code.
                new Mode { Scope = "code", Begin = "`{2}", End = @"(\n{2}|`{2})" },

                // Inline code.
                new Mode { Scope = "code", Begin = @"(`.+?`|\+.+?\+)" },

                // Indented literal block.
                new Mode { Scope = "code", Begin = @"^[ \t]", End = "$" },

                // Horizontal rule.
                new Mode { Begin = @"^'{3,}[ \t]*$" },

                // Images and links.
                // Deviation from highlight.js, whose target is `\S+?`: when the attribute list of the first `[` had no `]`
                // before the next `[`, the target went on through that `[` to try the next one, to the end of the line.
                // The target ends at the first `[`, as in AsciiDoc. Every later macro prefix of a run of target characters
                // reaches the same `[` (or the same whitespace), so only the first one is tried: trying each of them
                // scanned the rest of the run, which is quadratic on a long run of prefixes (`http://ahttp://a…`).
                new Mode
                {
                    Begin = FirstCandidate(@"(?:link:)?(?:http|https|ftp|file|irc|image:?):", @"[^\s\[]") + @"(link:)?(http|https|ftp|file|irc|image:?):[^\s\[]+\[[^\[]*?\]",
                    ReturnBegin = true,
                    Contains =
                    [
                        new Mode { Begin = "(link|image:?):" },
                        new Mode { Scope = "link", Begin = @"\w", End = @"[^\[]+" },
                        new Mode { Scope = "string", Begin = @"\[", End = @"\]", ExcludeBegin = true, ExcludeEnd = true },
                    ],
                },
            ],
        };
    }
}
