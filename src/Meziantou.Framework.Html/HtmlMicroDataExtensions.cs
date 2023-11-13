#nullable disable

namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
static class HtmlMicroDataExtensions
{
    // https://developers.google.com/structured-data/schema-org?hl=en&rd=1
    //private static readonly Func<string, string> s_schemasOrgParser = (type) =>
    //{
    //    if (type != null &&
    //        (type.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
    //        type.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
    //    {
    //        const string tok = ".org/"; // works for schema.org, auto.schema.org, data-vocabulary.org etc.
    //        var pos = type.LastIndexOf(tok);
    //        if (pos >= 0)
    //            return type.Substring(pos + tok.Length);
    //    }
    //    return type;
    //};

    public static string GetItemScopePath(this HtmlNode node, string separator)
    {
        return GetItemScopePath(node, separator, typeParser: null);
    }

    public static string GetItemScopePath(this HtmlNode node, string separator, Func<string, string> typeParser)
    {
        var path = GetItemProp(node);
        if (path is null)
            return null;

        var current = node;
        while (true)
        {
            var scope = GetItemScope(current);
            if (scope is null)
                break;

            var type = GetItemType(scope);
            if (type is not null)
            {
                if (typeParser is not null)
                {
                    type = typeParser(type);
                }
                path = type + separator + path;
            }

            current = scope.ParentNode;
        }

        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path;
    }

    public static string GetItemScopeType(this HtmlNode node)
    {
        return GetItemType(GetItemScope(node));
    }

    public static HtmlNode GetItemScope(this HtmlNode node)
    {
        if (node is null)
            return null;

        if (IsItemScope(node))
            return node;

        return GetItemScope(node.ParentNode);
    }

    public static bool IsItemScope(this HtmlNode node)
    {
        if (node is null)
            return false;

        return node.HasAttribute("itemscope");
    }

    public static string GetItemType(this HtmlNode node)
    {
        return node?.GetNullifiedAttributeValue("itemtype");
    }

    public static string GetItemProp(this HtmlNode node)
    {
        return node?.GetNullifiedAttributeValue("itemprop");
    }

    public static string GetItemRef(this HtmlNode node)
    {
        return node?.GetNullifiedAttributeValue("itemref");
    }

    public static string GetItemId(this HtmlNode node)
    {
        return node?.GetNullifiedAttributeValue("itemid");
    }

    // check http://www.w3.org/TR/microdata/#the-microdata-model 5.4 Values
    [SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "Better readability")]
    public static string GetItemValue(this HtmlNode node)
    {
        if (node is null)
            return string.Empty;

        string value;
        var name = node.Name.ToUpperInvariant();
        switch (name)
        {
            case "META":
                value = node.GetAttributeValue("content");
                break;

            case "AUDIO":
            case "EMBED":
            case "IFRAME":
            case "IMG":
            case "SOURCE":
            case "TRACK":
            case "VIDEO":
                value = node.GetAttributeValue("src");
                break;

            case "A":
            case "AREA":
            case "LINK":
                value = node.GetAttributeValue("href");
                break;

            case "OBJECT":
                value = node.GetAttributeValue("data");
                break;

            case "DATA":
            case "METER":
                value = node.GetAttributeValue("value");
                break;

            case "TIME":
                value = node.GetNullifiedAttributeValue("datetime") ?? node.InnerText;
                break;

            default:
                value = node.InnerText;
                break;
        }

        return value ?? string.Empty;
    }
}
