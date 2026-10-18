using System.Text;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Generates the user agent style sheet of HTML (Css/Cascade/HtmlUserAgentStyleSheet.g.cs) from the CSS blocks of the
/// rendering section of the HTML Standard (https://html.spec.whatwg.org/multipage/rendering.html). The blocks are
/// classified by the paragraph that introduces them: the rules that always apply, the presentational hints, the rules
/// that only apply in quirks mode or in HTML documents, and the rules of the select element that depend on its appearance.
/// </summary>
internal static partial class UserAgentStyleSheetGenerator
{
    private const string Namespace = "@namespace \"http://www.w3.org/1999/xhtml\";";

    public static void Generate(GeneratorContext context)
    {
        var source = File.ReadAllText(context.SpecData("html-source", "source"));
        var blocks = new Dictionary<string, StringBuilder>(StringComparer.Ordinal)
        {
            ["Default"] = new(),
            ["PresentationalHints"] = new(),
            ["Quirks"] = new(),
            ["HtmlDocuments"] = new(),
            ["SelectDropDown"] = new(),
            ["SelectNativeAppearance"] = new(),
            ["SelectDropDownNativeAppearance"] = new(),
            ["SelectBaseAppearance"] = new(),
            ["SelectDropDownBaseAppearance"] = new(),
            ["SelectListBoxBaseAppearance"] = new(),
        };

        // The blocks start with the @namespace rule of HTML (the code element may have other attributes, e.g. data-x="")
        foreach (Match match in BlockStartRegex().Matches(source))
        {
            var index = match.Index;
            var end = source.IndexOf("</code></pre>", index, StringComparison.Ordinal);
            var css = source[(index + match.Length - Namespace.Length)..end];
            var category = Classify(GetIntroduction(source, index));
            if (category is null)
                continue;

            // The markup of the specification (links, comments) and the @namespace rule of each block are removed
            css = CommentRegex().Replace(css, "");
            css = HtmlSpecSource.StripTags(css);
            css = css.Replace(Namespace, "", StringComparison.Ordinal).Trim();
            css = WrapNonZeroBorderRules(css);
            blocks[category].Append(css).Append("\n\n");
        }

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/html-source/source (https://html.spec.whatwg.org/multipage/rendering.html)");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Cascade;");
        writer.Line();
        writer.Line("/// <summary>The user agent style sheet of HTML (https://html.spec.whatwg.org/multipage/rendering.html). The rules are in the HTML namespace.</summary>");
        using (writer.Block("internal static class HtmlUserAgentStyleSheet"))
        {
            WriteConstant(writer, "Default", "The rules that apply to all the documents.", blocks["Default"]);
            writer.Line();
            WriteConstant(writer, "PresentationalHints", "The rules that apply as presentational hints (author-level rules with a zero specificity).", blocks["PresentationalHints"]);
            writer.Line();
            WriteConstant(writer, "Quirks", "The rules that apply in quirks mode.", blocks["Quirks"]);
            writer.Line();
            WriteConstant(writer, "HtmlDocuments", "The rules that apply in HTML documents.", blocks["HtmlDocuments"]);
            writer.Line();
            WriteConstant(writer, "SelectDropDown", "The rules that apply to the select elements rendered as a drop-down box.", blocks["SelectDropDown"]);
            writer.Line();
            WriteConstant(writer, "SelectNativeAppearance", "The rules that apply to the select elements rendered with native appearance or primitive appearance.", blocks["SelectNativeAppearance"]);
            writer.Line();
            WriteConstant(writer, "SelectDropDownNativeAppearance", "The rules that apply to the select elements rendered as a drop-down box with native appearance or primitive appearance.", blocks["SelectDropDownNativeAppearance"]);
            writer.Line();
            WriteConstant(writer, "SelectBaseAppearance", "The rules that apply to the select elements rendered with base appearance.", blocks["SelectBaseAppearance"]);
            writer.Line();
            WriteConstant(writer, "SelectDropDownBaseAppearance", "The rules that apply to the select elements rendered as a drop-down box with base appearance.", blocks["SelectDropDownBaseAppearance"]);
            writer.Line();
            WriteConstant(writer, "SelectListBoxBaseAppearance", "The rules that apply to the select elements rendered as a list box with base appearance.", blocks["SelectListBoxBaseAppearance"]);
            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// The name of the layer of the rules marked \"only if border is not equivalent to zero\"");
            writer.Line("/// (https://html.spec.whatwg.org/multipage/rendering.html#magic-border-selector): they only apply when the border");
            writer.Line("/// attribute of the table of their selector is not zero (the rules keep their place in the style sheet).");
            writer.Line("/// </summary>");
            writer.Line($"public const string NonZeroBorderLayer = {CodeWriter.Literal(NonZeroBorderLayer)};");
        }

        context.WriteFile(context.Source("Css", "Cascade", "HtmlUserAgentStyleSheet.g.cs"), writer.ToString());
        GenerateMathML(context);
    }

    /// <summary>
    /// Generates the user agent style sheet of MathML (Css/Cascade/MathMLUserAgentStyleSheet.g.cs) from the appendix of
    /// MathML Core (https://w3c.github.io/mathml-core/#user-agent-stylesheet), whose rules are included from the files of the
    /// user-agent-stylesheet directory of the specification repository.
    /// </summary>
    private static void GenerateMathML(GeneratorContext context)
    {
        var source = File.ReadAllText(context.SpecData("mathml-core", "spec.html"));
        var section = source.IndexOf("id=\"user-agent-stylesheet\"", StringComparison.Ordinal);
        var start = source.IndexOf("<pre class=\"css\">", section, StringComparison.Ordinal) + "<pre class=\"css\">".Length;
        var end = source.IndexOf("</pre>", start, StringComparison.Ordinal);
        if (section < 0 || start < section || end < 0)
            throw new InvalidOperationException("The user agent style sheet of MathML Core is not found");

        // The included files replace their placeholder; the @namespace rule is added by the style engine
        var css = IncludeRegex().Replace(source[start..end], match => File.ReadAllText(context.SpecData("mathml-core", match.Groups["path"].Value)).Trim() + "\n");
        css = HtmlSpecSource.StripTags(css);
        css = css.Replace("@namespace url(http://www.w3.org/1998/Math/MathML);", "", StringComparison.Ordinal).Trim();

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/mathml-core (https://w3c.github.io/mathml-core/#user-agent-stylesheet)");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Cascade;");
        writer.Line();
        writer.Line("/// <summary>The user agent style sheet of MathML Core (https://w3c.github.io/mathml-core/#user-agent-stylesheet). The rules are in the MathML namespace.</summary>");
        using (writer.Block("internal static class MathMLUserAgentStyleSheet"))
        {
            WriteConstant(writer, "Default", "The rules that apply to all the documents.", new StringBuilder(css));
        }

        context.WriteFile(context.Source("Css", "Cascade", "MathMLUserAgentStyleSheet.g.cs"), writer.ToString());
    }

    private static void WriteConstant(CodeWriter writer, string name, string summary, StringBuilder css)
    {
        writer.Line($"/// <summary>{summary}</summary>");
        writer.Line($"public const string {name} = {CodeWriter.Literal(css.ToString().TrimEnd() + "\n")};");
    }

    private const string NonZeroBorderLayer = "only-if-border-is-not-equivalent-to-zero";
    private const string NonZeroBorderComment = "/* only if border is not equivalent to zero */";

    /// <summary>
    /// Wraps the rules marked "only if border is not equivalent to zero" (a comment in their block or after it) in a layer
    /// named <see cref="NonZeroBorderLayer"/>: a selector cannot express the condition, which the cascade checks.
    /// </summary>
    private static string WrapNonZeroBorderRules(string css)
    {
        var marked = 0;
        var result = NonZeroBorderRuleRegex().Replace(css, match =>
        {
            var rule = match.Groups["rule"].Value;
            if (!match.Groups["after"].Success && !rule.Contains(NonZeroBorderComment, StringComparison.Ordinal))
                return match.Value;

            marked++;
            var start = rule.Length - rule.TrimStart().Length;
            return rule[..start] + "@layer " + NonZeroBorderLayer + " {\n" + rule[start..] + "\n}";
        });

        // Every mark must be attached to a rule: otherwise the format of the specification changed
        var comments = css.Split(NonZeroBorderComment).Length - 1;
        if (marked != comments)
            throw new InvalidOperationException($"{comments} rules are marked '{NonZeroBorderComment}' in the user agent style sheet of HTML, but {marked} were found");

        return result;
    }

    /// <summary>Returns the text of the paragraph that introduces the block, or an empty string when the block follows a heading or another block.</summary>
    private static string GetIntroduction(string source, int blockIndex)
    {
        var paragraph = source.LastIndexOf("<p", blockIndex, StringComparison.Ordinal);
        var previousBlock = source.LastIndexOf("</pre>", blockIndex, StringComparison.Ordinal);
        var heading = blockIndex;
        do
        {
            heading = source.LastIndexOf("<h", heading - 1, StringComparison.Ordinal);
        }
        while (heading > 0 && source[heading + 2] is < '1' or > '6');

        if (paragraph < 0 || paragraph < previousBlock || paragraph < heading)
            return "";

        return Regex.Replace(HtmlSpecSource.StripTags(source[paragraph..blockIndex]), @"\s+", " ");
    }

    /// <summary>Returns the constant the block belongs to, or null when the block only applies in specific conditions that are not modeled.</summary>
    private static string? Classify(string introduction)
    {
        if (introduction.Length == 0)
            return "Default";

        // The rules of a specific encoding are not modeled
        if (introduction.Contains("ISO-8859-8", StringComparison.Ordinal))
            return null;

        // The rules of the select element that depend on its appearance and on its rendering as a drop-down box or a list box
        // (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2): the cascade checks the condition
        var selectRules = introduction.Contains("expected to apply to select elements", StringComparison.Ordinal);
        if (selectRules && introduction.Contains("base appearance", StringComparison.Ordinal))
        {
            if (introduction.Contains("drop-down box", StringComparison.Ordinal))
                return "SelectDropDownBaseAppearance";

            return introduction.Contains("list box", StringComparison.Ordinal) ? "SelectListBoxBaseAppearance" : "SelectBaseAppearance";
        }

        if (selectRules && introduction.Contains("native appearance", StringComparison.Ordinal))
            return introduction.Contains("drop-down box", StringComparison.Ordinal) ? "SelectDropDownNativeAppearance" : "SelectNativeAppearance";

        if (selectRules && introduction.Contains("drop-down box", StringComparison.Ordinal))
            return "SelectDropDown";

        if (introduction.Contains("base appearance", StringComparison.Ordinal) || introduction.Contains("list box", StringComparison.Ordinal))
            return null;

        if (introduction.Contains("presentational hint", StringComparison.Ordinal))
            return "PresentationalHints";

        if (introduction.Contains("quirks mode", StringComparison.Ordinal))
            return "Quirks";

        if (introduction.Contains("In HTML documents", StringComparison.Ordinal))
            return "HtmlDocuments";

        return "Default";
    }

    [GeneratedRegex("<pre><code class=\"css\"[^>]*>@namespace \"http://www\\.w3\\.org/1999/xhtml\";")]
    private static partial Regex BlockStartRegex();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    // A style rule (the rules of the specification are not nested), and the mark that follows its block on the same line
    [GeneratedRegex(@"(?<rule>[^{}]+\{[^{}]*\})(?<after>[ \t]*/\* only if border is not equivalent to zero \*/)?", RegexOptions.ExplicitCapture)]
    private static partial Regex NonZeroBorderRuleRegex();

    [GeneratedRegex("<span[^>]*data-include=\"(?<path>[^\"]+)\"[^>]*></span>", RegexOptions.ExplicitCapture)]
    private static partial Regex IncludeRegex();
}
