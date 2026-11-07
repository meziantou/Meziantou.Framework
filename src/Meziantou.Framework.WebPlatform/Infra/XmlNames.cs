using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>Name validation (https://dom.spec.whatwg.org/#namespaces) and the XML Name production (https://www.w3.org/TR/xml/#NT-Name).</summary>
internal static class XmlNames
{
    private static bool IsAsciiWhitespaceOrNull(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ' or '\0';

    /// <summary>https://dom.spec.whatwg.org/#valid-namespace-prefix</summary>
    public static bool IsValidNamespacePrefix(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            if (IsAsciiWhitespaceOrNull(c) || c is '/' or '>')
                return false;
        }

        return true;
    }

    /// <summary>https://dom.spec.whatwg.org/#valid-attribute-local-name</summary>
    public static bool IsValidAttributeLocalName(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            if (IsAsciiWhitespaceOrNull(c) || c is '/' or '=' or '>')
                return false;
        }

        return true;
    }

    /// <summary>https://dom.spec.whatwg.org/#valid-element-local-name</summary>
    public static bool IsValidElementLocalName(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        if (char.IsAsciiLetter(value[0]))
        {
            foreach (var c in value)
            {
                if (IsAsciiWhitespaceOrNull(c) || c is '/' or '>')
                    return false;
            }

            return true;
        }

        // Code points in the range U+0080 to U+10FFFF include surrogate pairs and lone surrogates (JavaScript strings)
        if (!(value[0] is ':' or '_' || value[0] >= 0x80))
            return false;

        foreach (var c in value[1..])
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or ':' or '_' || c >= 0x80))
                return false;
        }

        return true;
    }

    /// <summary>https://dom.spec.whatwg.org/#valid-doctype-name</summary>
    public static bool IsValidDoctypeName(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (IsAsciiWhitespaceOrNull(c) || c == '>')
                return false;
        }

        return true;
    }

    /// <summary>https://dom.spec.whatwg.org/#validate-and-extract</summary>
    public static (string? Namespace, string? Prefix, string LocalName) ValidateAndExtract(string? @namespace, string qualifiedName, bool isElement)
    {
        if (@namespace is { Length: 0 })
        {
            @namespace = null;
        }

        string? prefix = null;
        var localName = qualifiedName;
        var colon = qualifiedName.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            prefix = qualifiedName[..colon];
            localName = qualifiedName[(colon + 1)..];
            if (!IsValidNamespacePrefix(prefix))
                throw DomException.InvalidCharacter($"'{prefix}' is not a valid namespace prefix");
        }

        if (isElement ? !IsValidElementLocalName(localName) : !IsValidAttributeLocalName(localName))
            throw DomException.InvalidCharacter($"'{localName}' is not a valid local name");

        if (prefix is not null && @namespace is null)
            throw DomException.Namespace("A prefix requires a namespace");

        if (prefix == "xml" && @namespace != Namespaces.Xml)
            throw DomException.Namespace("The 'xml' prefix is reserved for the XML namespace");

        if ((qualifiedName == "xmlns" || prefix == "xmlns") && @namespace != Namespaces.Xmlns)
            throw DomException.Namespace("The 'xmlns' prefix is reserved for the XMLNS namespace");

        if (@namespace == Namespaces.Xmlns && qualifiedName != "xmlns" && prefix != "xmlns")
            throw DomException.Namespace("The XMLNS namespace requires the 'xmlns' prefix");

        return (Namespaces.Intern(@namespace), prefix, localName);
    }

    /// <summary>https://www.w3.org/TR/xml/#NT-Name</summary>
    public static bool IsName(string value)
    {
        if (value.Length == 0)
            return false;

        for (var i = 0; i < value.Length;)
        {
            if (!IsNameChar(value, i, isFirst: i == 0, out var length))
                return false;

            i += length;
        }

        return true;
    }

    /// <summary>Returns whether the code point at <paramref name="index"/> is a NameStartChar (or NameChar when not first).</summary>
    public static bool IsNameChar(string value, int index, bool isFirst, out int length)
    {
        int codePoint;
        if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        {
            codePoint = char.ConvertToUtf32(value[index], value[index + 1]);
            length = 2;
        }
        else
        {
            codePoint = value[index];
            length = 1;
        }

        return isFirst ? IsNameStartCodePoint(codePoint) : IsNameCodePoint(codePoint);
    }

    public static bool IsNameStartCodePoint(int c) =>
        c is ':' or '_' or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 0xC0 and <= 0xD6) or (>= 0xD8 and <= 0xF6) or (>= 0xF8 and <= 0x2FF) or
        (>= 0x370 and <= 0x37D) or (>= 0x37F and <= 0x1FFF) or (>= 0x200C and <= 0x200D) or (>= 0x2070 and <= 0x218F) or
        (>= 0x2C00 and <= 0x2FEF) or (>= 0x3001 and <= 0xD7FF) or (>= 0xF900 and <= 0xFDCF) or (>= 0xFDF0 and <= 0xFFFD) or (>= 0x10000 and <= 0xEFFFF);

    public static bool IsNameCodePoint(int c) =>
        IsNameStartCodePoint(c) || c is '-' or '.' or (>= '0' and <= '9') or 0xB7 or (>= 0x300 and <= 0x36F) or (>= 0x203F and <= 0x2040);

    /// <summary>https://www.w3.org/TR/xml/#NT-Char</summary>
    public static bool IsXmlChar(int c) => c is 0x9 or 0xA or 0xD or (>= 0x20 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF);
}
