using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <remarks>
/// A "bridge" language: fragments of Ruby within <c>&lt;% … %&gt;</c> in HTML. As in highlight.js, the Ruby code of a
/// tag resumes the state the previous tag ended in, since the tags form one Ruby program.
/// </remarks>
internal static class Erb
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            // Deviation from highlight.js, which embeds `xml`: its xml grammar is this library's `html` grammar (the
            // `xml` one does not highlight <script> and <style> contents), so the markup is wrapped in `language-html`.
            SubLanguage = "html",
            Contains =
            [
                CommonModes.Comment("<%#", "%>"),
                new Mode
                {
                    Begin = "<%[%=-]?",
                    End = "[%-]?%>",
                    SubLanguage = "ruby",
                    ExcludeBegin = true,
                    ExcludeEnd = true,
                },
            ],
        };
    }
}
