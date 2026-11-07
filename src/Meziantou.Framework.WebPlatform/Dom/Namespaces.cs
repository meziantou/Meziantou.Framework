namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Namespaces defined by https://infra.spec.whatwg.org/#namespaces</summary>
internal static class Namespaces
{
    public const string Html = "http://www.w3.org/1999/xhtml";
    public const string MathML = "http://www.w3.org/1998/Math/MathML";
    public const string Svg = "http://www.w3.org/2000/svg";
    public const string XLink = "http://www.w3.org/1999/xlink";
    public const string Xml = "http://www.w3.org/XML/1998/namespace";
    public const string Xmlns = "http://www.w3.org/2000/xmlns/";

    /// <summary>Returns the interned namespace string when the namespace is well-known, so namespaces can be compared by reference.</summary>
    public static string? Intern(string? value) => value switch
    {
        null => null,
        Html => Html,
        Svg => Svg,
        MathML => MathML,
        XLink => XLink,
        Xml => Xml,
        Xmlns => Xmlns,
        _ => value,
    };
}
