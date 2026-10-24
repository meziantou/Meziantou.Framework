using System.Text;

namespace ChromiumDiff;

/// <summary>
/// Generates random markup around the areas of the parser that the tree construction tests cover least: foreign content
/// (the integration points, the breakout tags, the adjusted names), the template insertion modes, the adoption agency
/// algorithm and the reconstruction of the active formatting elements, the tables (foster parenting), select, noscript,
/// the raw text elements, and the serialization (attribute values and texts that must be escaped). The same seed gives
/// the same inputs.
/// </summary>
internal static class InputGenerator
{
    private static readonly string[] HtmlTags =
    [
        "html", "head", "body", "title", "style", "script", "noscript", "template", "base", "link", "meta", "div", "p", "span",
        "a", "b", "i", "u", "s", "em", "strong", "small", "big", "code", "tt", "font", "nobr", "strike", "table", "caption",
        "colgroup", "col", "tbody", "thead", "tfoot", "tr", "td", "th", "select", "option", "optgroup", "hr", "datalist",
        "input", "textarea", "button", "form", "label", "fieldset", "legend", "li", "ul", "ol", "dl", "dd", "dt", "h1", "h2",
        "pre", "listing", "xmp", "iframe", "noembed", "noframes", "plaintext", "frameset", "frame", "ruby", "rb", "rt", "rtc",
        "rp", "object", "applet", "marquee", "img", "image", "br", "wbr", "embed", "area", "param", "source", "track", "keygen",
        "math", "svg", "center", "address", "blockquote", "menu", "search", "details", "summary", "dialog", "main", "section",
        "figure", "selectedcontent", "sarcasm", "custom-element", "isindex", "basefont", "bgsound", "dir", "slot",
    ];

    private static readonly string[] FormattingTags = ["a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u"];

    private static readonly string[] SvgTags =
    [
        "svg", "foreignObject", "foreignobject", "desc", "title", "path", "g", "text", "clipPath", "linearGradient", "feBlend",
        "altGlyph", "use", "font", "script", "style",
    ];

    private static readonly string[] MathTags = ["math", "mi", "mo", "mn", "ms", "mtext", "annotation-xml", "mglyph", "malignmark", "mrow", "semantics"];

    private static readonly string[] Attributes =
    [
        "id=a", "class=\"x y\"", "color=red", "face=x", "size=3", "type=hidden", "type=text", "type=HIDDEN", "encoding=text/html",
        "encoding=\"application/xhtml+xml\"", "encoding=TEXT/HTML", "definitionURL=x", "xlink:href=#a", "xml:lang=en",
        "xmlns=http://www.w3.org/2000/svg", "xmlns:xlink=http://www.w3.org/1999/xlink", "viewBox=\"0 0 1 1\"", "clipPathUnits=x",
        "shadowrootmode=open", "shadowrootmode=closed", "shadowrootserializable", "shadowrootdelegatesfocus", "shadowrootclonable",
        "href=\"a&amp;b\"", "title='a\"b'", "title=\"a&b<c>\u00A0\"", "value=&quot;", "x=\"&notin;&noti\"", "x", "X=1", "x=2",
        "selected", "multiple", "name=n", "lang=en", "dir=rtl", "is=x-y", "ATTR=Value", "data-x=\"a\nb\"", "nonce=n",
    ];

    private static readonly string[] Texts =
    [
        "x", "y z", " ", "  ", "\n", "\t", "\r\n", "\u00A0", "&amp;", "&nbsp;", "&lt;", "&gt;", "&quot;", "&noti", "&notit;",
        "&amp", "&#0;", "&#x80;", "&#xD800;", "&#x110000;", "&#9;", "&#x2603;", "&", "<", ">", "\"", "'", "<3", "</", "a&b",
        "é", "\U0001F600", "-->", "]]>", "=",
    ];

    private static readonly string[] Others =
    [
        "<!-- c -->", "<!---->", "<!-->", "<!--->", "<!-- -- -->", "<!--!>", "<!--x--!>", "<!-- <!-- -->", "<?pi x?>", "</ >",
        "<!x>", "</3>", "<![CDATA[x]]>", "<![CDATA[]]>", "<![CDATA[a]]b]]>", "<![CDATA[x", "<!DOCTYPE html>", "<!doctype x>",
        "</br>", "</p>", "</html>", "</body>", "</head>", "</template>", "</table>", "</select>", "</svg>", "</math>",
        "</foreignObject>", "</annotation-xml>", "<br/>", "<p/>", "<svg/>", "<math/>", "<div/>",
    ];

    private static readonly string[] Snippets =
    [
        "<table><tr><td>", "<table>x<tr>y", "<table><caption>", "<table><colgroup><col>", "<table><td>", "<tr><td>", "<td>",
        "<table><form><input type=hidden><input>", "<table><select><option>", "<table><template><tr>",
        "<svg><foreignObject><p>", "<svg><desc><div>", "<svg><title><table>", "<math><mi><svg>", "<math><mtext><b>",
        "<math><annotation-xml encoding=text/html><div>", "<math><annotation-xml><svg>", "<math><mi><mglyph>",
        "<math><mo><malignmark>", "<svg><font color=x>", "<svg><font size=1>", "<svg><font>", "<svg><p>", "<math><table>",
        "<template><tr>", "<template><td>", "<template><col>", "<template><caption>", "<template><template><tr>",
        "<template><frame>", "<template><html>", "<select><option><optgroup>", "<select><hr>", "<select><div><option>",
        "<select><input>", "<select><textarea>", "<select><button>", "<select><svg>", "<option><optgroup>", "<optgroup><option>",
        "<noscript><link>", "<noscript><p>", "<head><noscript><meta>", "<head><noscript><style>", "<a><p><a>", "<b><p><i>",
        "<a><div><a>", "<b><b><b><b>", "<font color=x><font color=x><font color=x><font color=x>", "<nobr><nobr>",
        "<a><table><a>", "<b><table><td></b>", "<i><button><i>", "<p><table>", "<frameset><frame>", "<plaintext>",
        "<textarea>\n", "<pre>\n\n", "<listing>\n", "<image>", "<isindex>", "<keygen>", "<form><form>", "<ruby><rb><rt>",
        "<ruby><rtc><rp>", "<li><li>", "<dd><dt>", "<h1><h2>", "<button><button>", "<template shadowrootmode=open>",
        "<div><template shadowrootmode=open shadowrootserializable>", "<div><template shadowrootmode=closed>",
        "<selectedcontent>", "<select><button><selectedcontent>", "<body><body>", "<html><html>", "<head><head>",
        "<title>a<b>", "<style><!--", "<script><!--<script>", "<script><!--<script></script>", "<xmp></xmp>",
        "<iframe><p>", "<noembed><a>", "<noframes><b>", "<object><param>", "<applet><a>", "<marquee><b>",
        "<svg><![CDATA[x]]>", "<math><![CDATA[x]]>", "<svg><script>x</script>", "<svg><style><p>", "<math><mi></mi><p>",
    ];

    public static IEnumerable<Input> Generate(int count, int seed)
    {
        var random = new Random(seed);
        for (var i = 1; i <= count; i++)
        {
            yield return new Input($"generated-{seed}#{i}", GenerateOne(random));
        }
    }

    private static string GenerateOne(Random random)
    {
        var builder = new StringBuilder();
        var openElements = new List<string>();
        if (random.Next(5) == 0)
        {
            builder.Append(random.Next(3) == 0 ? "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\">" : "<!DOCTYPE html>");
        }

        var tokens = random.Next(1, 25);
        for (var i = 0; i < tokens; i++)
        {
            var kind = random.Next(100);
            if (kind < 40)
            {
                var name = PickTagName(random);
                AppendStartTag(builder, random, name);
                openElements.Add(name);
            }
            else if (kind < 55)
            {
                // Mostly the end tag of an open element (misnested or not)
                string name;
                if (openElements.Count > 0 && random.Next(4) != 0)
                {
                    var index = random.Next(2) == 0 ? openElements.Count - 1 : random.Next(openElements.Count);
                    name = openElements[index];
                    openElements.RemoveAt(index);
                }
                else
                {
                    name = PickTagName(random);
                }

                builder.Append("</").Append(ChangeCase(random, name)).Append('>');
            }
            else if (kind < 75)
            {
                builder.Append(Pick(random, Texts));
            }
            else if (kind < 85)
            {
                builder.Append(Pick(random, Others));
            }
            else
            {
                builder.Append(Pick(random, Snippets));
            }
        }

        return builder.ToString();
    }

    private static string PickTagName(Random random)
    {
        return random.Next(10) switch
        {
            < 5 => Pick(random, HtmlTags),
            < 7 => Pick(random, FormattingTags),
            < 9 => Pick(random, SvgTags),
            _ => Pick(random, MathTags),
        };
    }

    private static void AppendStartTag(StringBuilder builder, Random random, string name)
    {
        builder.Append('<').Append(ChangeCase(random, name));
        var attributes = random.Next(4) == 0 ? random.Next(1, 4) : 0;
        for (var i = 0; i < attributes; i++)
        {
            builder.Append(' ').Append(Pick(random, Attributes));
        }

        builder.Append(random.Next(15) == 0 ? "/>" : ">");
    }

    // The tag names are case-insensitive, except for the adjustment of the SVG names (foreignObject...)
    private static string ChangeCase(Random random, string name) => random.Next(10) == 0 ? name.ToUpperInvariant() : name;

    private static string Pick(Random random, string[] values) => values[random.Next(values.Length)];
}
