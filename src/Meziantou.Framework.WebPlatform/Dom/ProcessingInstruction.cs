using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-processinginstruction</summary>
/// <seealso href="https://dom.spec.whatwg.org/#processinginstruction"/>
[IdlInterface("ProcessingInstruction", "https://dom.spec.whatwg.org/#processinginstruction")]
public sealed partial class ProcessingInstruction : CharacterData
{
    // The attribute map is an ordered map
    private List<KeyValuePair<string, string>>? _attributes;

    /// <summary>Creates a processing instruction with the given target and data.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-processinginstruction"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-processinginstruction-processinginstruction")]
    public ProcessingInstruction(string target, string data)
        : this(Document.CurrentGlobalDocument, target, data, validate: true)
    {
    }

    internal ProcessingInstruction(Document document, string target, string data, bool validate)
        : base(document, "")
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(data);

        // https://dom.spec.whatwg.org/#concept-pi-initialize
        if (validate)
        {
            if (!XmlNames.IsName(target))
                throw DomException.InvalidCharacter("The target is not a valid XML name");

            if (data.Contains("?>", StringComparison.Ordinal))
                throw DomException.InvalidCharacter("The data cannot contain '?>'");
        }

        Target = target;
        SetDataCore(data);
        UpdateAttributesFromData();
    }

    /// <summary>Gets the node type of a processing instruction.</summary>
    public override ushort NodeType => ProcessingInstructionNode;

    /// <summary>Gets the target of the processing instruction.</summary>
    public override string NodeName => Target;

    /// <summary>Gets the target of the processing instruction.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-target"/>
    [IdlMember("target", "https://dom.spec.whatwg.org/#dom-processinginstruction-target")]
    public string Target { get; }

    /// <summary>Returns whether the data contains at least one pseudo-attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-hasattributes"/>
    [IdlMember("hasAttributes", "https://dom.spec.whatwg.org/#dom-processinginstruction-hasattributes")]
    public bool HasAttributes() => _attributes is { Count: > 0 };

    /// <summary>Returns the names of the pseudo-attributes of the data, in order.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-getattributenames"/>
    [IdlMember("getAttributeNames", "https://dom.spec.whatwg.org/#dom-processinginstruction-getattributenames")]
    public IReadOnlyList<string> GetAttributeNames() => _attributes is null ? [] : _attributes.Select(a => a.Key).ToArray();

    /// <summary>https://dom.spec.whatwg.org/#get-a-processing-instruction-attribute</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-getattribute"/>
    [IdlMember("getAttribute", "https://dom.spec.whatwg.org/#dom-processinginstruction-getattribute")]
    public string? GetAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_attributes is null)
            return null;

        foreach (var (key, value) in _attributes)
        {
            if (key == name)
                return value;
        }

        return null;
    }

    /// <summary>Sets a pseudo-attribute and updates the data accordingly.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-setattribute"/>
    [IdlMember("setAttribute", "https://dom.spec.whatwg.org/#dom-processinginstruction-setattribute")]
    public void SetAttribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (!XmlNames.IsValidAttributeLocalName(name))
            throw DomException.InvalidCharacter("The name is not a valid attribute local name");

        _attributes ??= [];
        var index = _attributes.FindIndex(a => a.Key == name);
        if (index >= 0)
        {
            _attributes[index] = new(name, value);
        }
        else
        {
            _attributes.Add(new(name, value));
        }

        UpdateDataFromAttributes();
    }

    /// <summary>Removes a pseudo-attribute and updates the data accordingly.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-removeattribute"/>
    [IdlMember("removeAttribute", "https://dom.spec.whatwg.org/#dom-processinginstruction-removeattribute")]
    public void RemoveAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_attributes is null)
            return;

        if (_attributes.RemoveAll(a => a.Key == name) > 0)
        {
            UpdateDataFromAttributes();
        }
    }

    /// <summary>
    /// Adds or removes a pseudo-attribute, optionally forced, and returns whether it is present afterwards.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-toggleattribute"/>
    [IdlMember("toggleAttribute", "https://dom.spec.whatwg.org/#dom-processinginstruction-toggleattribute")]
    public bool ToggleAttribute(string name, bool? force = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!XmlNames.IsValidAttributeLocalName(name))
            throw DomException.InvalidCharacter("The name is not a valid attribute local name");

        if (!HasAttribute(name))
        {
            if (force is null or true)
            {
                SetAttribute(name, "");
                return true;
            }

            return false;
        }

        if (force is null or false)
        {
            RemoveAttribute(name);
            return false;
        }

        return true;
    }

    /// <summary>Returns whether the data contains the pseudo-attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-processinginstruction-hasattribute"/>
    [IdlMember("hasAttribute", "https://dom.spec.whatwg.org/#dom-processinginstruction-hasattribute")]
    public bool HasAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _attributes is not null && _attributes.Exists(a => a.Key == name);
    }

    internal override Node CloneSingleNode(Document document) => new ProcessingInstruction(document, Target, Data, validate: false);

    private protected override bool IsEqualCharacterDataCore(CharacterData other) => other is ProcessingInstruction pi && pi.Target == Target;

    private protected override void DataChangedSteps()
    {
        // Setting the data updates the attributes (https://dom.spec.whatwg.org/#concept-cd-replace)
        UpdateAttributesFromData();
        UpdateXmlStyleSheet();
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-pi-update-attributes-from-data</summary>
    private void UpdateAttributesFromData()
    {
        _attributes = PseudoAttributes.Parse(Data);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-pi-update-data-from-attributes</summary>
    private void UpdateDataFromAttributes()
    {
        var data = new StringBuilder();
        if (_attributes is not null)
        {
            foreach (var (name, value) in _attributes)
            {
                if (data.Length > 0)
                {
                    data.Append(' ');
                }

                data.Append(name).Append("=\"");
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '&': data.Append("&amp;"); break;
                        case '<': data.Append("&lt;"); break;
                        case '>': data.Append("&gt;"); break;
                        case '"': data.Append("&quot;"); break;
                        default: data.Append(c); break;
                    }
                }

                data.Append('"');
            }
        }

        // "Replace data" updates the attributes from the data again, which yields the same map
        var attributes = _attributes;
        ReplaceData(0, Length, data.ToString());
        _attributes = attributes;
    }
}

/// <summary>
/// The rules for parsing pseudo-attributes from a string (https://www.w3.org/TR/xml-stylesheet/#dt-parsing).
/// </summary>
internal static class PseudoAttributes
{
    /// <summary>Returns the pseudo-attributes, or null when the string does not match the PseudoAtts production.</summary>
    public static List<KeyValuePair<string, string>>? Parse(string value)
    {
        var result = new List<KeyValuePair<string, string>>();
        var position = 0;
        SkipWhitespace(value, ref position);
        while (position < value.Length)
        {
            // PseudoAtt ::= Name S? "=" S? PseudoAttValue
            var nameStart = position;
            while (position < value.Length && XmlNames.IsNameChar(value, position, isFirst: position == nameStart, out var length))
            {
                position += length;
            }

            if (position == nameStart)
                return null;

            var name = value[nameStart..position];
            SkipWhitespace(value, ref position);
            if (position >= value.Length || value[position] != '=')
                return null;

            position++;
            SkipWhitespace(value, ref position);
            if (position >= value.Length || value[position] is not ('"' or '\''))
                return null;

            var quote = value[position++];
            var attributeValue = new StringBuilder();
            while (true)
            {
                if (position >= value.Length)
                    return null;

                var c = value[position];
                if (c == quote)
                {
                    position++;
                    break;
                }

                if (c == '<')
                    return null;

                if (c == '&')
                {
                    var end = value.IndexOf(';', position, StringComparison.Ordinal);
                    if (end < 0)
                        return null;

                    var reference = value.AsSpan(position + 1, end - position - 1);
                    switch (reference)
                    {
                        case "amp": attributeValue.Append('&'); break;
                        case "lt": attributeValue.Append('<'); break;
                        case "gt": attributeValue.Append('>'); break;
                        case "quot": attributeValue.Append('"'); break;
                        case "apos": attributeValue.Append('\''); break;
                        default:
                            if (!TryParseCharRef(reference, out var codePoint))
                                return null;

                            attributeValue.Append(char.ConvertFromUtf32(codePoint));
                            break;
                    }

                    position = end + 1;
                    continue;
                }

                attributeValue.Append(c);
                position++;
            }

            // "Set attribute map[name] to value": a later duplicate overwrites the value, keeping the original position
            var existingIndex = result.FindIndex(a => a.Key == name);
            if (existingIndex >= 0)
            {
                result[existingIndex] = new(name, attributeValue.ToString());
            }
            else
            {
                result.Add(new(name, attributeValue.ToString()));
            }

            var beforeWhitespace = position;
            SkipWhitespace(value, ref position);
            if (position < value.Length && position == beforeWhitespace)
                return null;
        }

        return result;
    }

    private static bool TryParseCharRef(ReadOnlySpan<char> reference, out int codePoint)
    {
        codePoint = 0;
        if (reference.Length < 2 || reference[0] != '#')
            return false;

        var isHex = reference[1] == 'x';
        var digits = isHex ? reference[2..] : reference[1..];
        if (digits.IsEmpty)
            return false;

        foreach (var c in digits)
        {
            int digit;
            if (char.IsAsciiDigit(c))
            {
                digit = c - '0';
            }
            else if (isHex && char.IsAsciiHexDigit(c))
            {
                digit = (c | 0x20) - 'a' + 10;
            }
            else
            {
                return false;
            }

            codePoint = (codePoint * (isHex ? 16 : 10)) + digit;
            if (codePoint > 0x10FFFF)
                return false;
        }

        return XmlNames.IsXmlChar(codePoint);
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t' or '\r' or '\n')
        {
            position++;
        }
    }
}
