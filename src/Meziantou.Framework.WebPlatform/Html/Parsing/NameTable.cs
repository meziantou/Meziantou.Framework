using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>
/// Interns tag and attribute names so that repeated names share the same string instance, and the short values that repeat
/// a lot in a document (e.g. the classes, and the whitespace-only texts such as "\n").
/// </summary>
internal sealed class NameTable
{
    /// <summary>The maximum length of the values that are shared.</summary>
    private const int MaxSharedValueLength = 32;

    /// <summary>The maximum number of shared values (the strings of the values that are not shared are allocated as usual).</summary>
    private const int MaxSharedValueCount = 16384;

    private static readonly System.Buffers.SearchValues<char> AsciiWhitespace = System.Buffers.SearchValues.Create(" \t\n\f\r");

    private static readonly FrozenSet<string> KnownAttributeNames = FrozenSet.Create(StringComparer.Ordinal,
    [
        "abbr", "accept", "accept-charset", "accesskey", "action", "align", "alink", "allow", "allowfullscreen", "alt", "archive",
        "as", "async", "autocapitalize", "autocomplete", "autocorrect", "autofocus", "autoplay", "axis", "background", "bgcolor", "blocking", "border",
        "cellpadding", "cellspacing", "char", "charoff", "charset", "checked", "cite", "class", "classid", "clear", "code", "codebase",
        "codetype", "color", "cols", "colspan", "command", "commandfor", "compact", "content", "contenteditable", "controls", "coords",
        "crossorigin", "d", "data", "datetime", "declare", "decoding", "default", "defer", "dir", "dirname", "disabled", "download",
        "draggable", "enctype", "enterkeyhint", "face", "fetchpriority", "fill", "for", "form", "formaction", "formenctype", "formmethod",
        "formnovalidate", "formtarget", "frame", "frameborder", "headers", "height", "hidden", "high", "href", "hreflang", "hspace",
        "http-equiv", "id", "inert", "inputmode", "integrity", "is", "ismap", "itemid", "itemprop", "itemref", "itemscope", "itemtype",
        "kind", "label", "lang", "language", "link", "list", "loading", "longdesc", "loop", "low", "marginheight", "marginwidth", "max",
        "maxlength", "media", "method", "min", "minlength", "multiple", "muted", "name", "nohref", "nomodule", "nonce", "noresize",
        "noshade", "novalidate", "nowrap", "onclick", "onload", "open", "optimum", "pattern", "ping", "placeholder", "playsinline",
        "popover", "popovertarget", "popovertargetaction", "poster", "preload", "profile", "readonly", "referrerpolicy", "rel", "required",
        "rev", "reversed", "role", "rows", "rowspan", "rules", "sandbox", "scheme", "scope", "scrolling", "selected", "shadowrootclonable",
        "shadowrootcustomelementregistry", "shadowrootdelegatesfocus", "shadowrootmode", "shadowrootserializable", "shadowrootslotassignment",
        "shape", "size", "sizes", "slot", "span", "spellcheck", "src", "srcdoc", "srclang", "srcset", "standby", "start", "step", "stroke",
        "style", "summary", "tabindex", "target", "text", "title", "translate", "transform", "type", "usemap", "valign", "value",
        "valuetype", "version", "viewbox", "vlink", "vspace", "width", "wrap", "x", "xmlns", "xmlns:xlink", "xlink:href", "y",
        "aria-hidden", "aria-label", "aria-labelledby", "aria-describedby", "aria-expanded", "aria-controls", "aria-live", "aria-current",
    ]);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> KnownAttributeLookup = KnownAttributeNames.GetAlternateLookup<ReadOnlySpan<char>>();

    // The names that are not known (most fragments have none): the set is created when the first one is interned
    private HashSet<string>? _names;
    private HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private HashSet<string>? _values;

    /// <summary>Returns the value of an attribute, shared with the other attributes with the same short value (to use for the attributes whose values repeat, as hashing the unique values costs more than it saves).</summary>
    public string GetAttributeValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return "";

        if (value.Length > MaxSharedValueLength)
            return value.ToString();

        return GetSharedValue(value);
    }

    /// <summary>Returns the data of a text node, shared with the other text nodes when it is short and only whitespace (e.g. the line breaks and the indentation between elements).</summary>
    public string GetText(ReadOnlySpan<char> data)
    {
        if (data.Length > MaxSharedValueLength || data.ContainsAnyExcept(AsciiWhitespace))
            return data.ToString();

        return GetSharedValue(data);
    }

    private string GetSharedValue(ReadOnlySpan<char> value)
    {
        var values = _values ??= new(StringComparer.Ordinal);
        var lookup = values.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(value, out var existing))
            return existing;

        var result = value.ToString();
        if (values.Count < MaxSharedValueCount)
        {
            values.Add(result);
        }

        return result;
    }

    public string GetTagName(ReadOnlySpan<char> name, out TagId tagId)
    {
        tagId = TagNames.GetTagId(name);
        return tagId == TagId.Unknown ? Intern(name) : TagNames.GetName(tagId);
    }

    public string GetAttributeName(ReadOnlySpan<char> name)
    {
        if (KnownAttributeLookup.TryGetValue(name, out var known))
            return known;

        return Intern(name);
    }

    public string Intern(ReadOnlySpan<char> value)
    {
        if (_names is null)
        {
            _names = new(StringComparer.Ordinal);
            _lookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
        }
        else if (_lookup.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var result = value.ToString();
        _names.Add(result);
        return result;
    }
}
