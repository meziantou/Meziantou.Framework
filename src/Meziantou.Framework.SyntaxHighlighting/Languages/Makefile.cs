using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Makefile
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // Deviations from highlight.js, which only knows `$(name)` and `$@`-like variables:
        // - `${name}`, `$(@D)`-like automatic variables, and substitution references (`$(SRCS:.c=.o)`);
        // - names can contain `-` and `.`;
        // - `$$` is an escaped dollar, so `$$@` and `$$(pwd)` are not variables.
        const string VariableName = @"[a-zA-Z_][\w.-]*";
        var escapedDollar = new Mode { Begin = @"\$\$" };
        var variable = new Mode { Scope = "variable" };
        var substitutionReference = new Mode { Begin = @"\$\(" + VariableName + ":", End = @"\)" };
        var bracedSubstitutionReference = new Mode { Begin = @"\$\{" + VariableName + ":", End = @"\}" };
        variable.Variants =
        [
            new Mode { Begin = @"\$\(" + VariableName + @"\)", Contains = [CommonModes.BackslashEscape] },
            new Mode { Begin = @"\$\{" + VariableName + @"\}" },
            new Mode { Begin = @"\$\([@%<?\^\+\*][DF]\)" },
            substitutionReference,
            bracedSubstitutionReference,
            new Mode { Begin = @"\$[@%<?\^\+\*]" },
        ];

        // Function: $(func arg,...)
        // Deviation from highlight.js: a function can contain another one (`$(notdir $(basename $(SRCS)))`), and
        // `words`, `info`, `let` and `intcmp` are functions.
        var function = new Mode
        {
            Scope = "variable",
            Begin = @"\$\([\w-]+\s",
            End = @"\)",
            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["built_in"] = "subst patsubst strip findstring filter filter-out sort word words wordlist firstword lastword dir notdir suffix basename addsuffix addprefix join wildcard realpath abspath error warning info shell origin flavor foreach if or and call eval file value let intcmp",
            }),
        };

        // Quoted string with variables inside
        // Deviation from highlight.js: a string can contain a function call (`"$(shell date)"`).
        var quoteString = new Mode
        {
            Scope = "string",
            Begin = "\"",
            End = "\"",
            Contains = [CommonModes.BackslashEscape, escapedDollar, variable, function],
        };

        function.Contains = [escapedDollar, variable, quoteString, Mode.Self];
        substitutionReference.Contains = [escapedDollar, variable, function];
        bracedSubstitutionReference.Contains = [escapedDollar, variable, function];

        // Variable assignment
        var assignment = new Mode
        {
            // Deviation from highlight.js: `::=`, `:::=` and `!=` are assignments too.
            Begin = "^" + CommonModes.UnderscoreIdentRe + @"\s*(?=(?::{1,3}|[+?!])?=)",
        };

        // Meta targets (.PHONY)
        var meta = new Mode
        {
            Scope = "meta",
            Begin = @"^\.PHONY:",
            End = "$",
            KeywordPattern = @"[\.\w]+",
            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = ".PHONY",
            }),
        };

        // Targets
        var target = new Mode
        {
            Scope = "section",
            Begin = @"^[^\s]+:",
            End = "$",
            Contains = [escapedDollar, variable, function],
        };

        return new Mode
        {
            KeywordPattern = @"[\w-]+",
            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = "define endef undefine ifdef ifndef ifeq ifneq else endif include -include sinclude override export unexport private vpath",
            }),
            Contains =
            [
                // Deviation from highlight.js: `\#` is an escaped hash, not a comment.
                CommonModes.Comment(@"(?<!\\)#", "$"),
                escapedDollar,
                variable,
                quoteString,
                function,
                assignment,
                meta,
                target,
            ],
        };
    }
}
