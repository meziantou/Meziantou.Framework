namespace Meziantou.Framework.WebPlatform.Html;

internal static class CustomElements
{
    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#valid-custom-element-name</summary>
    public static bool IsValidCustomElementName(string name)
    {
        // A valid element local name whose first code point is an ASCII lower alpha, without ASCII upper alphas, that contains a hyphen
        if (name.Length == 0 || !char.IsAsciiLetterLower(name[0]) || !Infra.XmlNames.IsValidElementLocalName(name))
            return false;

        var hasHyphen = false;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterUpper(c))
                return false;

            if (c == '-')
            {
                hasHyphen = true;
            }
        }

        if (!hasHyphen)
            return false;

        return name is not ("annotation-xml" or "color-profile" or "font-face" or "font-face-src" or "font-face-uri" or "font-face-format" or "font-face-name" or "missing-glyph");
    }

    /// <summary>https://dom.spec.whatwg.org/#valid-shadow-host-name</summary>
    public static bool IsValidShadowHostName(string name) =>
        name is "article" or "aside" or "blockquote" or "body" or "div" or "footer" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "header" or "main" or "nav" or "p" or "section" or "span"
        || IsValidCustomElementName(name);
}
