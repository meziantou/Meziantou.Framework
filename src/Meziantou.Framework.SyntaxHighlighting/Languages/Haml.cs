using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Haml
{
    // The lines nested under the current line: blank lines, and lines indented deeper than the current line, whose
    // indentation must be captured in the group `i` (the indentation of a line can only be deeper by starting with it).
    private const string NestedLinesRe = @"(?:\n(?:[ \t]*\n)*\k<i>[ \t]+\S[^\n]*)*";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var attributeValue = new Mode { Begin = @"\w+" };

        // Deviation from highlight.js, whose attribute name pattern also matched the value (`:class => :active`,
        // `(data=value)` highlighted `:active` and `value` as attributes): only a name followed by its operator is one.
        var hashAttributes = new Mode
        {
            Begin = @"\{\s*",

            // Deviation from highlight.js, whose attribute hash only ends at `}`, even lines later: a hash that is not
            // closed turned the rest of the document into attributes. As in Haml, a hash can only continue on the next
            // line after a comma. The closing brace is only looked for from the start of a run of whitespace, and the line
            // end is tested before the comma, so a long run of whitespace is not scanned from each of its positions.
            End = CommonModes.RunStart(@"\s") + @"\s*\}|$(?<!,[ \t]*)",
            Contains =
            [
                new Mode
                {
                    Begin = @":\w+\s*=>",
                    End = @",\s+",
                    ReturnBegin = true,
                    EndsWithParent = true,
                    Contains = [new Mode { Scope = "attr", Begin = @":\w+(?=\s*=>)" }, CommonModes.AposStringMode, CommonModes.QuoteStringMode, attributeValue],
                },

                // Deviation from highlight.js, which only knows the `:name => value` syntax: the `name: value` syntax of
                // Ruby 1.9 hashes has attributes too.
                new Mode
                {
                    Begin = @"\w+:(?!:)",
                    End = @",\s+",
                    ReturnBegin = true,
                    EndsWithParent = true,
                    Contains = [new Mode { Scope = "attr", Begin = @"\w+(?=:(?!:))" }, CommonModes.AposStringMode, CommonModes.QuoteStringMode, attributeValue],
                },
            ],
        };

        var htmlAttributes = new Mode
        {
            Begin = @"\(\s*",
            End = @"\s*\)",
            ExcludeEnd = true,
            Contains =
            [
                new Mode
                {
                    Begin = @"\w+\s*=",
                    End = @"\s+",
                    ReturnBegin = true,
                    EndsWithParent = true,
                    Contains = [new Mode { Scope = "attr", Begin = @"\w+(?=\s*=)" }, CommonModes.AposStringMode, CommonModes.QuoteStringMode, attributeValue],
                },
            ],
        };

        // Deviation from highlight.js: Ruby code can follow a tag on the same line (`%h1= @title`, `%p!= html`).
        var tagRuby = new Mode
        {
            Begin = @"[<>]{0,2}(?:!=|&=|[=~])",
            End = "$",
            SubLanguage = "ruby",
            ExcludeBegin = true,
            ExcludeEnd = true,
        };

        var tagContains = new List<Mode>
        {
            new() { Scope = "selector-tag", Begin = @"\w+" },
            new() { Scope = "selector-id", Begin = @"#[\w-]+" },
            new() { Scope = "selector-class", Begin = @"\.[\w-]+" },
            hashAttributes,
            htmlAttributes,
            tagRuby,
        };

        // Braces and strings are skipped, so the interpolation ends at its own `}`, not at the first one of its code.
        var skippedString = new Mode { Skip = true };
        skippedString.Variants =
        [
            new Mode { Begin = "\"", End = "\"|$" },
            new Mode { Begin = "'", End = "'|$" },
        ];
        skippedString.Contains = [new Mode { Begin = @"\\.", Skip = true }];
        var skippedBraces = new Mode { Begin = @"\{", End = @"\}|$", Skip = true };
        skippedBraces.Contains = [skippedBraces, skippedString];

        return new Mode
        {
            CaseInsensitive = true,
            Contains =
            [
                new Mode { Scope = "meta", Begin = @"^!!!( (5|1\.1|Strict|Frameset|Basic|Mobile|RDFa|XML\b.*))?$" },

                // Deviation from highlight.js, whose comments are one line long (a FIXME upstream): the lines nested under a
                // silent comment (`-#`) or an HTML comment (`/`) are part of it, as in Haml.
                CommonModes.Comment(CommonModes.IndentedLineStartRe + @"(?=-#|/)(?<=^(?<i>[ \t]*))[^\n]*" + NestedLinesRe, @"\B|\b"),
                CommonModes.Comment(CommonModes.IndentedLineStartRe + "(!=#|=#).*$", @"\B|\b"),
                new Mode
                {
                    Begin = CommonModes.IndentedLineStartRe + "(-|=|!=)(?!#)",
                    End = "$",
                    SubLanguage = "ruby",
                    ExcludeBegin = true,
                    ExcludeEnd = true,
                },
                new Mode
                {
                    Scope = "tag",
                    Contains = tagContains,
                    Variants =
                    [
                        new Mode { Begin = CommonModes.IndentedLineStartRe + "%" },

                        // Deviation from highlight.js: a line starting with a class or an id (`.item`, `#main`) is a `div`.
                        new Mode { Begin = CommonModes.IndentedLineStartRe + @"(?=[.#][\w-])" },
                    ],
                },
                new Mode { Begin = CommonModes.IndentedLineStartRe + @"[=~]\s*" },

                // Deviation from highlight.js, whose interpolation ends at the first `}`, even lines later: a nested
                // interpolation (`#{"a #{b}"}`) or a hash ended it early, and a `#{` without a `}` turned the rest of the
                // document into Ruby. It ends at its own `}` or with its line.
                new Mode
                {
                    Begin = @"#\{",
                    End = @"\}|$",
                    SubLanguage = "ruby",
                    ExcludeBegin = true,
                    ExcludeEnd = true,
                    Contains = [skippedBraces, skippedString],
                },

                // Deviation from highlight.js, which does not support filters (a TODO upstream): the lines nested under a
                // filter (`:javascript`, `:css`, …) are highlighted in its language, or are plain text for the other filters
                // (`:plain`, `:markdown`, whose content Haml unindents first, …). Interpolations in a filter are not
                // highlighted.
                new Mode { Scope = "meta", Begin = @":(?<=^[ \t]*:)[\w-]+(?=[ \t]*$)" },
                CreateFilterContent("javascript", "javascript"),
                CreateFilterContent("css", "css"),
                CreateFilterContent("scss", "scss"),
                CreateFilterContent("less", "less"),
                CreateFilterContent("ruby", "ruby"),
                CreateFilterContent(@"[\w-]+", subLanguage: null),
            ],
        };
    }

    /// <summary>The content of a filter, matched as a whole: the lines nested under the filter line.</summary>
    private static Mode CreateFilterContent(string names, string? subLanguage)
    {
        return new Mode
        {
            // The content starts at the line feed of the filter line, whose indentation is captured by the lookbehind.
            Begin = @"\n(?<=^(?<i>[ \t]*):(?:" + names + @")[ \t]*\n)(?:[ \t]*\n)*\k<i>[ \t]+\S[^\n]*" + NestedLinesRe,
            SubLanguage = subLanguage,
            RestartsSubLanguage = subLanguage is not null,
        };
    }
}
