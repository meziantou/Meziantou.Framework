using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class HtmlElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-translate</summary>
    [IdlMember("translate", "https://html.spec.whatwg.org/multipage/dom.html#dom-translate")]
    public bool Translate
    {
        get
        {
            // The translation mode: the nearest ancestor (inclusive) in the Yes or No state, translate-enabled by default
            var table = EnumeratedAttributes.Translate;
            for (Element? element = this; element is not null; element = element.ParentElement)
            {
                if (!element.IsHtmlNamespace)
                    continue;

                var state = table.GetState(element.GetAttributeValue("translate"));
                if (state == table.Keywords["yes"])
                    return true;

                if (state == table.Keywords["no"])
                    return false;
            }

            return true;
        }
        set => SetAttributeValue("translate", value ? "yes" : "no");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-draggable</summary>
    [IdlMember("draggable", "https://html.spec.whatwg.org/multipage/dnd.html#dom-draggable")]
    public bool Draggable
    {
        get
        {
            var table = EnumeratedAttributes.Draggable;
            var state = table.GetState(GetAttributeValue("draggable"));
            if (state == table.Keywords["true"])
                return true;

            if (state == table.Keywords["false"])
                return false;

            // Auto: images and links are draggable
            return TagId == TagId.Img || (TagId == TagId.A && HasAttributeWithLocalName("href"));
        }
        set => SetAttributeValue("draggable", value ? "true" : "false");
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-spellcheck: the default behavior of elements is to
    /// inherit the state of the nearest ancestor, and to check the spelling when no ancestor specifies it.
    /// </summary>
    [IdlMember("spellcheck", "https://html.spec.whatwg.org/multipage/interaction.html#dom-spellcheck")]
    public bool Spellcheck
    {
        get
        {
            var table = EnumeratedAttributes.Spellcheck;
            for (Element? element = this; element is not null; element = element.ParentElement)
            {
                var state = table.GetState(element.GetAttributeValue("spellcheck"));
                if (state == table.Keywords["true"])
                    return true;

                if (state == table.Keywords["false"])
                    return false;
            }

            return true;
        }
        set => SetAttributeValue("spellcheck", value ? "true" : "false");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-autocorrect</summary>
    [IdlMember("autocorrect", "https://html.spec.whatwg.org/multipage/interaction.html#dom-autocorrect")]
    public bool Autocorrect
    {
        get
        {
            // The used autocorrection state
            var table = EnumeratedAttributes.Autocorrect;
            if (IsHtml(TagId.Input) && InfraAscii.ToAsciiLowercase(GetAttributeValue("type") ?? "") is "email" or "url" or "password")
                return false;

            var value = GetAttributeValue("autocorrect");
            if (value is null && TagId is TagId.Button or TagId.Fieldset or TagId.Input or TagId.Output or TagId.Select or TagId.Textarea && FormOwner is { } form)
            {
                value = form.GetAttributeValue("autocorrect");
            }

            return table.GetState(value) != table.Keywords["off"];
        }
        set => SetAttributeValue("autocorrect", value ? "on" : "off");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-contenteditable</summary>
    [IdlMember("contentEditable", "https://html.spec.whatwg.org/multipage/interaction.html#dom-contenteditable")]
    public string ContentEditable
    {
        get
        {
            var table = EnumeratedAttributes.Contenteditable;
            var state = table.GetState(GetAttributeValue("contenteditable"));
            return table.CanonicalKeywords[state] ?? "inherit";
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var keyword = InfraAscii.ToAsciiLowercase(value);
            switch (keyword)
            {
                case "inherit":
                    RemoveAttributeByLocalName("contenteditable");
                    break;
                case "true" or "false" or "plaintext-only":
                    SetAttributeValue("contenteditable", keyword);
                    break;
                default:
                    throw DomException.Syntax($"'{value}' is not a valid contentEditable value");
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-iscontenteditable</summary>
    [IdlMember("isContentEditable", "https://html.spec.whatwg.org/multipage/interaction.html#dom-iscontenteditable")]
    public bool IsContentEditable => Css.Selectors.HtmlElementStates.IsEditingHostOrEditable(this);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-accesskeylabel: the key combination of the assigned access
    /// key of the element (e.g. <c>Alt+Shift+B</c>), or the empty string.
    /// </summary>
    [IdlMember("accessKeyLabel", "https://html.spec.whatwg.org/multipage/interaction.html#dom-accesskeylabel")]
    public string AccessKeyLabel => AccessKeys.GetLabel(this);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#dom-innertext: the rendered text of the element (see
    /// <see cref="RenderedText"/>), or its descendant text content when it is not being rendered.
    /// </summary>
    [IdlMember("innerText", "https://html.spec.whatwg.org/multipage/dom.html#dom-innertext")]
    public string InnerText
    {
        get => RenderedText.GetInnerText(this) ?? GetDescendantTextContent();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ReplaceAll(CreateRenderedTextFragment(value));
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-outertext</summary>
    [IdlMember("outerText", "https://html.spec.whatwg.org/multipage/dom.html#dom-outertext")]
    public string OuterText
    {
        get => InnerText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var parent = ParentNode ?? throw DomException.NoModificationAllowed("The element has no parent");
            var next = NextSibling;
            var previous = PreviousSibling;
            var fragment = CreateRenderedTextFragment(value);
            if (fragment.FirstChild is null)
            {
                fragment.AppendChild(NodeDocument.CreateTextNode(""));
            }

            parent.ReplaceChild(fragment, this);

            // Merge the text nodes with the adjacent text nodes
            if (next?.PreviousSibling is Text nextSibling)
            {
                MergeWithNextText(nextSibling);
            }

            if (previous is Text previousText)
            {
                MergeWithNextText(previousText);
            }
        }
    }

    // https://html.spec.whatwg.org/multipage/dom.html#merge-with-the-next-text-node
    private static void MergeWithNextText(Text node)
    {
        if (node.NextSibling is not Text next)
            return;

        node.AppendData(next.Data);
        next.RemoveFromParent();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#rendered-text-fragment</summary>
    private DocumentFragment CreateRenderedTextFragment(string input)
    {
        var document = NodeDocument;
        var fragment = document.CreateDocumentFragment();
        var position = 0;
        while (position < input.Length)
        {
            var end = input.AsSpan(position).IndexOfAny('\n', '\r');
            var length = end < 0 ? input.Length - position : end;
            if (length > 0)
            {
                fragment.AppendChild(document.CreateTextNode(input.Substring(position, length)));
            }

            position += length;
            if (position >= input.Length)
                break;

            // A CR LF pair is a single line break
            if (input[position] == '\r' && position + 1 < input.Length && input[position + 1] == '\n')
            {
                position++;
            }

            position++;
            fragment.AppendChild(ElementFactory.CreateElement(document, "br", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false));
        }

        return fragment;
    }
}
