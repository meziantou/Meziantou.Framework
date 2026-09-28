using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <remarks>
/// Also registered as <c>jinja</c>, <c>jinja2</c> and <c>j2</c>: highlight.js uses the Django grammar for Jinja templates.
/// </remarks>
internal static class Django
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var filter = new Mode
        {
            // Deviation from highlight.js, whose `\|[A-Za-z]+:?` stops at an underscore: of `|default_if_none:"x"`, it
            // highlighted `default` and left the rest of the name and the argument plain, so the filters of its own
            // list that contain an underscore (`default_if_none`, `truncatewords_html`, `length_is`, …) never matched.
            Begin = @"\|[A-Za-z_]+:?",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] =
                    "truncatewords removetags linebreaksbr yesno get_digit timesince random striptags "
                    + "filesizeformat escape linebreaks length_is ljust rjust cut urlize fix_ampersands "
                    + "title floatformat capfirst pprint divisibleby add make_list unordered_list urlencode "
                    + "timeuntil urlizetrunc wordcount stringformat linenumbers slice date dictsort "
                    + "dictsortreversed default_if_none pluralize lower join center default "
                    + "truncatewords_html upper length phone2numeric wordwrap time addslashes slugify first "
                    + "escapejs force_escape iriencode last safe safeseq truncatechars localize unlocalize "
                    + "localtime utc timezone",
            }),
            Contains = [CommonModes.QuoteStringMode, CommonModes.AposStringMode],
        };

        return new Mode
        {
            CaseInsensitive = true,

            // Deviation from highlight.js, which embeds `xml`: its xml grammar is this library's `html` grammar (the
            // `xml` one does not highlight <script> and <style> contents), so the markup is wrapped in `language-html`.
            SubLanguage = "html",
            Contains =
            [
                CommonModes.Comment(@"\{%\s*comment\s*%\}", @"\{%\s*endcomment\s*%\}"),
                CommonModes.Comment(@"\{#", "#\\}"),
                new Mode
                {
                    Scope = "template-tag",
                    Begin = @"\{%",
                    End = @"%\}",
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "name",
                            Begin = @"\w+",
                            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["name"] =
                                    "comment endcomment load templatetag ifchanged endifchanged if endif firstof for "
                                    + "endfor ifnotequal endifnotequal widthratio extends include spaceless "
                                    + "endspaceless regroup ifequal endifequal ssi now with cycle url filter "
                                    + "endfilter debug block endblock else autoescape endautoescape csrf_token empty elif "
                                    + "endwith static trans blocktrans endblocktrans get_static_prefix get_media_prefix "
                                    + "plural get_current_language language get_available_languages "
                                    + "get_current_language_bidi get_language_info get_language_info_list localize "
                                    + "endlocalize localtime endlocaltime timezone endtimezone get_current_timezone "
                                    + "verbatim",
                            }),
                            Starts = new Mode
                            {
                                EndsWithParent = true,
                                Keywords = Engine.Keywords.FromWords(["in", "by", "as"]),
                                Contains = [filter],
                            },
                        },
                    ],
                },
                new Mode
                {
                    Scope = "template-variable",
                    Begin = @"\{\{",
                    End = @"\}\}",
                    Contains = [filter],
                },
            ],
        };
    }
}
