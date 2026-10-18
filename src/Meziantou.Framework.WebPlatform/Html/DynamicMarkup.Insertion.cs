using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Dom;

// The HTML insertion methods proposed by https://github.com/whatwg/html/pull/12753: they parse the HTML as setHTML() and
// setHTMLUnsafe() do, and insert the result at a position instead of replacing the children. The context element of the
// parsing is the element (the host of a shadow root), or the parent of the node for the methods of NonDocumentTypeChildNode.

public partial class Element
{
    /// <summary>Parses the HTML and inserts the result after the last child (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("appendHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-element-appendhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AppendHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPreInsertHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("appendHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-element-appendhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AppendHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("appendHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-element-appendhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AppendHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("prependHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-element-prependhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void PrependHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPrependHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("prependHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-element-prependhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void PrependHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("prependHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-element-prependhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void PrependHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before this node (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("beforeHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeforeHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("beforeHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeforeHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("beforeHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeforeHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("afterHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AfterHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("afterHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AfterHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("afterHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AfterHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("replaceWithHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void ReplaceWithHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("replaceWithHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void ReplaceWithHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node replaceWithHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("replaceWithHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
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
    /// <summary>Parses the HTML and inserts the result after the last child (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("appendHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-shadowroot-appendhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AppendHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPreInsertHtml(this, null, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("appendHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-shadowroot-appendhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AppendHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(this, null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result after the last child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("appendHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-shadowroot-appendhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AppendHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot appendHTMLUnsafe");
        DynamicMarkup.FilterAndPreInsertHtml(this, null, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("prependHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-shadowroot-prependhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void PrependHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.FilterAndPrependHtml(this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("prependHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-shadowroot-prependhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void PrependHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>Parses the HTML and inserts the result before the first child, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("prependHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-shadowroot-prependhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void PrependHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot prependHTMLUnsafe");
        DynamicMarkup.FilterAndPrependHtml(this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }
}

public abstract partial class CharacterData
{
    /// <summary>Parses the HTML and inserts the result before this node (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("beforeHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeforeHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("beforeHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeforeHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result before this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("beforeHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-beforehtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void BeforeHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node beforeHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("afterHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AfterHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("afterHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AfterHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and inserts the result after this node, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("afterHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-afterhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void AfterHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node afterHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndPreInsertHtml(parent, NextSibling, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("replaceWithHTML", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtml")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void ReplaceWithHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    [IdlMember("replaceWithHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
    public void ReplaceWithHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Node replaceWithHTMLUnsafe");
        if (DynamicMarkup.GetParentForHtmlInsertion(this) is { } parent)
        {
            DynamicMarkup.FilterAndReplaceWithHtml(parent, this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
        }
    }

    /// <summary>Parses the HTML and replaces this node with the result, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12753).</summary>
    [IdlMember("replaceWithHTMLUnsafe", "https://whatpr.org/html/12753/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-replacewithhtmlunsafe")]
    [Experimental(ExperimentalApis.HtmlInsertionMethods, UrlFormat = ExperimentalApis.UrlFormat)]
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
