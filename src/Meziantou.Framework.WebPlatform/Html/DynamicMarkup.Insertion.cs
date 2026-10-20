using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Dom;

// The HTML insertion methods (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#html-insertion-methods): they parse the HTML as setHTML() and
// setHTMLUnsafe() do, and insert the result at a position instead of replacing the children. The context element of the
// parsing is the element (the host of a shadow root), or the parent of the node for the methods of NonDocumentTypeChildNode.

public partial class Element
{
    /// <summary>Parses the HTML and inserts the result after the last child.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-appendhtml"/>
    [IdlMember("appendHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-appendhtml")]
    public void AppendHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPreInsertHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-appendhtmlunsafe"/>
    [IdlMember("appendHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-appendhtmlunsafe")]
    public void AppendHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-appendhtmlunsafe"/>
    [IdlMember("appendHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-appendhtmlunsafe")]
    public void AppendHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-prependhtml"/>
    [IdlMember("prependHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-prependhtml")]
    public void PrependHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPrependHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-prependhtmlunsafe"/>
    [IdlMember("prependHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-prependhtmlunsafe")]
    public void PrependHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-prependhtmlunsafe"/>
    [IdlMember("prependHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-prependhtmlunsafe")]
    public void PrependHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before this node.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtml"/>
    [IdlMember("beforeHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtml")]
    public void BeforeHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe"/>
    [IdlMember("beforeHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    public void BeforeHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe"/>
    [IdlMember("beforeHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    public void BeforeHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtml"/>
    [IdlMember("afterHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtml")]
    public void AfterHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe"/>
    [IdlMember("afterHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    public void AfterHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe"/>
    [IdlMember("afterHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    public void AfterHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtml"/>
    [IdlMember("replaceWithHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtml")]
    public void ReplaceWithHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe"/>
    [IdlMember("replaceWithHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    public void ReplaceWithHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node replaceWithHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe"/>
    [IdlMember("replaceWithHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    public void ReplaceWithHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node replaceWithHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }
}

public sealed partial class ShadowRoot
{
    /// <summary>Parses the HTML and inserts the result after the last child.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-appendhtml"/>
    [IdlMember("appendHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-appendhtml")]
    public void AppendHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPreInsertHtml(this, null, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-appendhtmlunsafe"/>
    [IdlMember("appendHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-appendhtmlunsafe")]
    public void AppendHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(this, null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-appendhtmlunsafe"/>
    [IdlMember("appendHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-appendhtmlunsafe")]
    public void AppendHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(this, null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-prependhtml"/>
    [IdlMember("prependHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-prependhtml")]
    public void PrependHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPrependHtml(this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-prependhtmlunsafe"/>
    [IdlMember("prependHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-prependhtmlunsafe")]
    public void PrependHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-prependhtmlunsafe"/>
    [IdlMember("prependHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-prependhtmlunsafe")]
    public void PrependHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }
}

public abstract partial class CharacterData
{
    /// <summary>Parses the HTML and inserts the result before this node.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtml"/>
    [IdlMember("beforeHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtml")]
    public void BeforeHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe"/>
    [IdlMember("beforeHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    public void BeforeHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe"/>
    [IdlMember("beforeHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    public void BeforeHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtml"/>
    [IdlMember("afterHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtml")]
    public void AfterHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe"/>
    [IdlMember("afterHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    public void AfterHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe"/>
    [IdlMember("afterHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    public void AfterHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtml"/>
    [IdlMember("replaceWithHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtml")]
    public void ReplaceWithHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer.</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe"/>
    [IdlMember("replaceWithHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    public void ReplaceWithHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node replaceWithHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe"/>
    [IdlMember("replaceWithHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    public void ReplaceWithHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node replaceWithHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }
}
