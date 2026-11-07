using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Latex
{
    // LaTeX3 (expl3) macro names, each followed by (?![a-zA-Z:_]):
    // - a function \module_function_name:signature or \__module_function_name:signature;
    // - a variable \scope_module_and_name_type or \scope__module_and_name_type (scope is l, g or c);
    // - a quark \q_the_name or \q__the_name, or a scan mark \s_the_name or \s__the_name;
    // - other LaTeX3 macro names that are not covered by the three rules above.
    // The mode starts right after the backslash, so the lookbehind (not in highlight.js) does not change what matches.
    // It keeps the scan for the next match from starting at every letter of the document: from each letter of a long
    // run such as `aa_aa_aa...`, the first alternative scans to the end of the run, which is quadratic.
    private const string L3Re =
        @"(?<=\\)(?:"
        + @"(?:__)?[a-zA-Z]{2,}_[a-zA-Z](?:_?[a-zA-Z])+:[a-zA-Z]*(?![a-zA-Z:_])"
        + @"|[lgc]__?[a-zA-Z](?:_?[a-zA-Z])*_[a-zA-Z]{2,}(?![a-zA-Z:_])"
        + @"|[qs]__?[a-zA-Z](?:_?[a-zA-Z])+(?![a-zA-Z:_])"
        + @"|use(?:_i)?:[a-zA-Z]*(?![a-zA-Z:_])"
        + @"|(?:else|fi|or):(?![a-zA-Z:_])"
        + @"|(?:if|cs|exp):w(?![a-zA-Z:_])"
        + @"|(?:hbox|vbox):n(?![a-zA-Z:_])"
        + @"|::[a-zA-Z]_unbraced(?![a-zA-Z:_])"
        + @"|::[a-zA-Z:](?![a-zA-Z:_])"
        + ")";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        Mode[] doubleCaretVariants =
        [
            new Mode { Begin = @"\^{6}[0-9a-f]{6}" },
            new Mode { Begin = @"\^{5}[0-9a-f]{5}" },
            new Mode { Begin = @"\^{4}[0-9a-f]{4}" },
            new Mode { Begin = @"\^{3}[0-9a-f]{3}" },
            new Mode { Begin = @"\^{2}[0-9a-f]{2}" },
            new Mode { Begin = @"\^{2}[\u0000-\u007f]" },
        ];

        var controlSequence = new Mode
        {
            Scope = "keyword",
            Begin = @"\\",
            Contains =
            [
                // highlight.js tries a list of well-known control words first, but only to raise the relevance: each of
                // them is also a control word (the last mode), which covers the same text.
                new Mode { EndsParent = true, Begin = L3Re },
                new Mode { EndsParent = true, Variants = doubleCaretVariants },
                new Mode
                {
                    EndsParent = true,
                    Variants =
                    [
                        new Mode { Begin = "[a-zA-Z@]+" }, // control word
                        new Mode { Begin = "[^a-zA-Z@]?" }, // control symbol
                    ],
                },
            ],
        };

        Mode[] everythingButVerbatim =
        [
            controlSequence,
            new Mode { Scope = "params", Begin = @"#+\d?" },
            new Mode { Variants = doubleCaretVariants },
            new Mode { Scope = "built_in", Begin = "[$&^_]" },
            new Mode { Scope = "meta", Begin = "% ?!(T[eE]X|tex|BIB|bib)", End = "$" },
            CommonModes.Comment("%", "$"),
        ];

        var braceGroupNoVerbatim = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Contains = [Mode.Self, .. everythingButVerbatim],
        };

        Mode[] argumentM =
        [
            new Mode
            {
                Begin = @"\{",
                End = @"\}",
                EndsParent = true,
                Contains = [braceGroupNoVerbatim, .. everythingButVerbatim],
            },
        ];

        Mode[] argumentO =
        [
            new Mode
            {
                Begin = @"\[",
                End = @"\]",
                EndsParent = true,
                Contains = [braceGroupNoVerbatim, .. everythingButVerbatim],
            },
        ];

        var spaceGobbler = new Mode { Begin = @"\s+" };

        Mode ArgumentAndThen(Mode[] argument, Mode startsMode) => new()
        {
            Contains = [spaceGobbler],
            Starts = new Mode
            {
                Contains = argument,
                Starts = startsMode,
            },
        };

        Mode CsName(string csname, Mode startsMode) => new()
        {
            Begin = @"\\" + csname + "(?![a-zA-Z@:_])",
            KeywordPattern = @"\\[a-zA-Z]+",
            Keywords = Engine.Keywords.FromWords([@"\" + csname]),
            Contains = [spaceGobbler],
            Starts = startsMode,
        };

        Mode BeginEnv(string envname, Mode startsMode)
        {
            var mode = ArgumentAndThen(argumentM, startsMode);
            return new Mode(mode)
            {
                Begin = @"\\begin(?=[ \t]*(\r?\n[ \t]*)?\{" + envname + @"\})",
                KeywordPattern = @"\\[a-zA-Z]+",
                Keywords = Engine.Keywords.FromWords([@"\begin"]),
            };
        }

        static Mode VerbatimDelimitedEqual() => new()
        {
            Scope = "string",
            Begin = @"(.|\r?\n)",
            End = @"(.|\r?\n)",
            EndSameAsBegin = true,
            ExcludeBegin = true,
            ExcludeEnd = true,
            EndsParent = true,
        };

        static Mode VerbatimDelimitedEnv(string envname) => new()
        {
            Scope = "string",
            End = @"(?=\\end\{" + envname + @"\})",
        };

        static Mode VerbatimDelimitedBraces(string innerName) => new()
        {
            Begin = @"\{",
            Starts = new Mode
            {
                EndsParent = true,
                Contains =
                [
                    new Mode
                    {
                        Scope = innerName,

                        // Deviation from highlight.js, whose inner mode starts even when the braces are empty: its
                        // zero-width begin and end then match at the same position, and the zero-width guard skips
                        // the closing brace, so `\url{}` highlights everything up to the next closing brace.
                        Begin = "(?=[^}])",
                        End = @"(?=\})",
                        EndsParent = true,
                        Contains =
                        [
                            new Mode
                            {
                                Begin = @"\{",
                                End = @"\}",
                                Contains = [Mode.Self],
                            },
                        ],
                    },
                ],
            },
        };

        List<Mode> verbatim =
        [
            // Deviation from highlight.js, which takes the star of `\verb*|a b|` for the delimiter (and then highlights
            // the rest of the document as a string): the star is part of the command.
            new Mode(CsName("verb", new Mode { Contains = [VerbatimDelimitedEqual()] }))
            {
                Begin = @"\\verb(?:\*|(?![a-zA-Z@:_]))",
            },

            // Deviation from highlight.js, which takes the first character after `\lstinline` for the delimiter: it
            // also accepts the optional argument and braces (`\lstinline[language=C]{x}`), like listings does.
            CsName("lstinline", ArgumentAndThen(argumentO, new Mode { Contains = [VerbatimDelimitedBraces("string"), VerbatimDelimitedEqual()] })),
            CsName("mint", ArgumentAndThen(argumentM, new Mode { Contains = [VerbatimDelimitedEqual()] })),
            CsName("mintinline", ArgumentAndThen(argumentM, new Mode { Contains = [VerbatimDelimitedBraces("string"), VerbatimDelimitedEqual()] })),

            // highlight.js lists this link mode twice; the second copy can never match, so it is omitted.
            CsName("url", new Mode { Contains = [VerbatimDelimitedBraces("link")] }),
            CsName("hyperref", new Mode { Contains = [VerbatimDelimitedBraces("link")] }),
            CsName("href", ArgumentAndThen(argumentO, new Mode { Contains = [VerbatimDelimitedBraces("link")] })),
        ];

        foreach (var suffix in (string[])["", @"\*"])
        {
            verbatim.Add(BeginEnv("verbatim" + suffix, VerbatimDelimitedEnv("verbatim" + suffix)));
            verbatim.Add(BeginEnv("filecontents" + suffix, ArgumentAndThen(argumentM, VerbatimDelimitedEnv("filecontents" + suffix))));
            foreach (var prefix in (string[])["", "B", "L"])
            {
                var envname = prefix + "Verbatim" + suffix;
                verbatim.Add(BeginEnv(envname, ArgumentAndThen(argumentO, VerbatimDelimitedEnv(envname))));
            }
        }

        verbatim.Add(BeginEnv("minted", ArgumentAndThen(argumentO, ArgumentAndThen(argumentM, VerbatimDelimitedEnv("minted")))));

        return new Mode
        {
            Contains = [.. verbatim, .. everythingButVerbatim],
        };
    }
}
