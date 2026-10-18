using System.Text;
using System.Xml;
using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Xml;

/// <summary>
/// Resolves external entities for the XML parser. Only the DTD of the XHTML public identifiers listed by HTML is
/// provided (it declares the named character references); nothing is ever fetched, and other external entities are empty
/// (https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents).
/// </summary>
internal sealed class XhtmlEntityResolver : XmlResolver
{
    private static readonly Uri EntitiesUri = new("urn:meziantou-html:xhtml-entities");
    private static readonly Uri EmptyUri = new("urn:meziantou-html:empty");

    /// <summary>The declarations of the named character references (the names ending with a semicolon), by name.</summary>
    private static readonly Lazy<Dictionary<string, string>> EntityDeclarations = new(CreateEntityDeclarations);

    /// <summary>https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents</summary>
    private static readonly HashSet<string> PublicIdentifiers = new(StringComparer.Ordinal)
    {
        "-//W3C//DTD XHTML 1.0 Transitional//EN",
        "-//W3C//DTD XHTML 1.1//EN",
        "-//W3C//DTD XHTML 1.0 Strict//EN",
        "-//W3C//DTD XHTML 1.0 Frameset//EN",
        "-//W3C//DTD XHTML Basic 1.0//EN",
        "-//W3C//DTD XHTML 1.1 plus MathML 2.0//EN",
        "-//W3C//DTD XHTML 1.1 plus MathML 2.0 plus SVG 1.1//EN",
        "-//W3C//DTD MathML 2.0//EN",
        "-//WAPFORUM//DTD XHTML Mobile 1.0//EN",
        "-//WAPFORUM//DTD XHTML Mobile 1.1//EN",
        "-//WAPFORUM//DTD XHTML Mobile 1.2//EN",
    };

    private readonly string _input;

    /// <param name="input">The document being parsed: the DTD only declares the entities that it references, as parsing the
    /// declarations of all the named character references is much slower than parsing most documents.</param>
    public XhtmlEntityResolver(string input)
    {
        _input = input;
    }

    // The XML reader first resolves the public identifier of an external subset, then its system identifier
    public override Uri ResolveUri(Uri? baseUri, string? relativeUri) => relativeUri is not null && PublicIdentifiers.Contains(relativeUri) ? EntitiesUri : EmptyUri;

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        var content = absoluteUri == EntitiesUri ? CreateEntitiesDtd() : [];
        return new MemoryStream(content, writable: false);
    }

    /// <summary>
    /// Creates a DTD declaring the named character references referenced by the input (the declarations of the internal subset
    /// and the replacement text of the internal entities are part of the input; the other external entities are empty).
    /// </summary>
    private byte[] CreateEntitiesDtd()
    {
        var declarations = EntityDeclarations.Value;
        var dtd = new StringBuilder();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var input = _input.AsSpan();
        for (var position = input.IndexOf('&'); position >= 0;)
        {
            var nameStart = position + 1;
            var nameEnd = nameStart;
            while (nameEnd < input.Length && char.IsAsciiLetterOrDigit(input[nameEnd]))
            {
                nameEnd++;
            }

            if (nameEnd > nameStart && nameEnd < input.Length && input[nameEnd] == ';')
            {
                var name = input[nameStart..nameEnd].ToString();
                if (declarations.TryGetValue(name, out var declaration) && declared.Add(name))
                {
                    dtd.Append(declaration);
                }
            }

            var next = input[nameEnd..].IndexOf('&');
            position = next < 0 ? -1 : nameEnd + next;
        }

        return Encoding.UTF8.GetBytes(dtd.ToString());
    }

    /// <summary>Creates the declarations of the named character references (the names ending with a semicolon).</summary>
    private static Dictionary<string, string> CreateEntityDeclarations()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var declaration = new StringBuilder();
        var name = new StringBuilder();
        void Visit(int node)
        {
            var valueLength = NamedCharacterReferences.NodeValueLength[node];
            if (valueLength > 0 && name.Length > 1 && name[^1] == ';')
            {
                var entityName = name.ToString(0, name.Length - 1);
                declaration.Clear();
                declaration.Append("<!ENTITY ").Append(entityName).Append(" \"");
                var value = NamedCharacterReferences.Values.AsSpan(NamedCharacterReferences.NodeValueOffset[node], valueLength);
                foreach (var rune in value.EnumerateRunes())
                {
                    // The character references of an entity value are expanded when the declaration is parsed, and the
                    // replacement text is parsed as content: "&" and "<" are escaped twice (https://www.w3.org/TR/xml/#sec-entexpand)
                    declaration.Append(rune.Value is '&' or '<' ? "&#38;#x" : "&#x").Append(rune.Value.ToString("X", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                }

                declaration.Append("\">\n");
                result[entityName] = declaration.ToString();
            }

            var first = NamedCharacterReferences.NodeFirstChild[node];
            var count = NamedCharacterReferences.NodeChildCount[node];
            for (var i = 0; i < count; i++)
            {
                var child = first + i;
                name.Append((char)NamedCharacterReferences.NodeCharacter[child]);
                Visit(child);
                name.Length--;
            }
        }

        Visit(0);
        return result;
    }
}
