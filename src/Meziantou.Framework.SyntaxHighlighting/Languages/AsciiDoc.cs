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

    // Deviation from highlight.js, whose delimited blocks begin on a delimiter followed by a line break and end on any
    // delimiter of the same family (`\n[\-\.]{4,}$`): a literal block (`....`) ended on a `----` line and an example
    // block (`====`) on a `****` line, a closing delimiter followed by a space did not end the block, and neither did the
    // delimiter of an empty block, whose line break was consumed by the opening one. As in Asciidoctor, a delimiter is
    // a whole line (trailing whitespace is ignored), and a block ends only on the same delimiter (EndSameAsBegin
    // compares the captured delimiters, so a `------` block does not end on a `----` line).
    private static string DelimiterLine(string delimiter) => "^(" + delimiter + @")[ \t\r]*$";

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
                new Mode(CommonModes.Comment(DelimiterLine("/{4,}"), DelimiterLine("/{4,}"))) { EndSameAsBegin = true },

                // Line comment.
                CommonModes.Comment("^//", "$"),

                // Block title.
                new Mode { Scope = "title", Begin = @"^\.\w.*$" },

                // Example, admonition and sidebar blocks.
                new Mode { Begin = DelimiterLine(@"={4,}|\*{4,}"), End = DelimiterLine(@"={4,}|\*{4,}"), EndSameAsBegin = true },

                // Headings.
                new Mode
                {
                    Scope = "section",
                    Variants =
                    [
                        new Mode { Begin = @"^(={1,6})[ \t].+?([ \t]\1)?$" },

                        // Deviation from highlight.js, whose two-line (setext) title is any line followed by a line of at
                        // least two `=`, `-`, `~`, `^` or `+`: a list continuation (`+`) or a paragraph followed by a
                        // listing block delimiter (`----`) became a title, and so did the closing delimiter with the line
                        // before it. As in Asciidoctor, the title must have a letter or a digit and not start with `.`,
                        // and the underline repeats a single character, with a length within one of the title's (each
                        // title character is pushed on `c`, and each underline character pops one).
                        new Mode { Begin = @"^(?!\.)(?=[^\n]*?[\p{L}\p{N}])(?<c>[^\[\]\n])+\n(?=[=\-~\^\+]{2})(?<u>[=\-~\^\+])(?<-c>)(?:\k<u>(?<-c>))*\k<u>?(?<-c>)?(?(c)(?!))$" },
                    ],
                },

                // Document attributes.
                new Mode { Scope = "meta", Begin = "^:.+?:", End = @"\s", ExcludeEnd = true },

                // Block attributes.
                new Mode { Scope = "meta", Begin = @"^\[.+?\]$" },

                // Quote blocks.
                new Mode { Scope = "quote", Begin = DelimiterLine("_{4,}"), End = DelimiterLine("_{4,}"), EndSameAsBegin = true },

                // Listing and literal blocks.
                new Mode { Scope = "code", Begin = DelimiterLine(@"-{4,}|\.{4,}"), End = DelimiterLine(@"-{4,}|\.{4,}"), EndSameAsBegin = true },

                // Passthrough blocks.
                new Mode
                {
                    Begin = DelimiterLine(@"\+{4,}"),
                    End = DelimiterLine(@"\+{4,}"),
                    EndSameAsBegin = true,
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

                        // Deviation from highlight.js, where the macro does not end with its attribute list: a word character
                        // right after the `]` (`https://example.com[docs]s`) started another target, which ran to the next
                        // `[` of the document, across paragraphs.
                        new Mode { Scope = "string", Begin = @"\[", End = @"\]", ExcludeBegin = true, ExcludeEnd = true, EndsParent = true },
                    ],
                },
            ],
        };
    }
}
