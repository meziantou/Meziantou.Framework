using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages.Common;

/// <summary>Modes shared by the grammars of component frameworks built on top of HTML (Vue, Svelte).</summary>
internal static class HtmlComponents
{
    /// <summary>
    /// A <c>script</c>, <c>style</c> or <c>template</c> element whose <c>lang</c> attribute is one of
    /// <paramref name="languages"/> (regex alternatives), and whose content is highlighted as
    /// <paramref name="subLanguage"/>, or is plain text when it is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Its open tag is highlighted like the HTML <c>script</c> and <c>style</c> tags. <paramref name="contentModes"/> are
    /// the modes recognized in its content, between the fragments highlighted as <paramref name="subLanguage"/>. Like the
    /// content of the HTML <c>script</c> and <c>style</c> elements, its content is highlighted from the root of
    /// <paramref name="subLanguage"/>, so a string that the previous element leaves open does not leak into it.
    /// </remarks>
    public static Mode CreateTagWithLang(Html.HtmlModes html, string tagName, string languages, string? subLanguage, IList<Mode>? contentModes = null)
    {
        return new Mode
        {
            Scope = "tag",

            // The lookahead skips the other attributes, including quoted values that contain a `>`. It stops at a `<` too,
            // so that it does not scan the rest of the document from each tag that is not closed.
            Begin = "<" + tagName + @"(?=\s(?:[^<>""']|""[^""]*""|'[^']*')*?(?<=\s)lang\s*=\s*([""']?)(?:" + languages + @")\1(?=[\s/>]))",
            End = ">",
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = [tagName] }),
            Contains = [html.Attributes],
            Starts = new Mode
            {
                End = @"<\/" + tagName + ">",
                ReturnEnd = true,
                SubLanguage = subLanguage,
                RestartsSubLanguage = subLanguage is not null,
                Contains = contentModes ?? [],
            },
        };
    }
}
