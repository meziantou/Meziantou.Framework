using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <remarks>
/// HTML with VBScript blocks between <c>&lt;%</c> and <c>%&gt;</c> (classic ASP pages). The HTML resumes the state the
/// previous HTML part ended in, so a block can be used inside an attribute value.
/// </remarks>
internal static class VbScriptHtml
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            // Deviation from highlight.js: the HTML is highlighted with the html grammar (`language-html`). Upstream
            // used its xml grammar, which also highlights the content of `<script>` and `<style>` elements; in this
            // library, that part of the highlight.js xml grammar is the html grammar.
            SubLanguage = "html",
            Contains =
            [
                new Mode
                {
                    Begin = "<%",
                    End = "%>",
                    SubLanguage = "vbscript",

                    // Deviation from highlight.js: each block starts from the root of the VBScript grammar. Upstream,
                    // it resumed the state the previous block ended in, so after an unterminated string, the next
                    // block started in the string.
                    RestartsSubLanguage = true,
                },
            ],
        };
    }
}
