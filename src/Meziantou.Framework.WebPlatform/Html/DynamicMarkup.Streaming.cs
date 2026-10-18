namespace Meziantou.Framework.WebPlatform.Dom;

// The streaming HTML insertion methods proposed by https://github.com/whatwg/html/pull/12758: they return a writer whose chunks
// are parsed by a fragment parser that inserts the nodes directly at a position of the target (see HtmlStreamWriter).

public partial class Element
{
    /// <summary>Returns a writer that parses HTML and replaces the children with the result as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamHtml(SetHtmlOptions? options = null)
    {
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        var stream = DynamicMarkup.StreamHtml(target, null, FragmentParserOptions.FromSafe(options), safe: true);
        target.ReplaceAll(null);
        return stream;
    }

    /// <summary>Returns a writer that parses HTML and replaces the children with the result as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Element streamHTMLUnsafe");
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        var stream = DynamicMarkup.StreamHtml(target, null, compliantOptions, safe: false);
        target.ReplaceAll(null);
        return stream;
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after the last child as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamappendhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAppendHtml(SetHtmlOptions? options = null)
    {
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after the last child as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamappendhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAppendHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Element streamAppendHTMLUnsafe");
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before the first child as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamprependhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamPrependHtml(SetHtmlOptions? options = null)
    {
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        return DynamicMarkup.StreamHtml(target, target.FirstChild, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before the first child as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamprependhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamPrependHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Element streamPrependHTMLUnsafe");
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        return DynamicMarkup.StreamHtml(target, target.FirstChild, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before this node as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streambeforehtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamBeforeHtml(SetHtmlOptions? options = null)
    {
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetParentForHtmlStreaming(this), this, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before this node as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streambeforehtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamBeforeHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Node streamBeforeHTMLUnsafe");
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetParentForHtmlStreaming(this), this, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after this node as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamafterhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAfterHtml(SetHtmlOptions? options = null)
    {
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        return DynamicMarkup.StreamHtml(parent, NextSibling, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after this node as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamafterhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAfterHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Node streamAfterHTMLUnsafe");
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        return DynamicMarkup.StreamHtml(parent, NextSibling, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and replaces this node with the result as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamreplacewithhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamReplaceWithHtml(SetHtmlOptions? options = null)
    {
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        var stream = DynamicMarkup.StreamHtml(parent, NextSibling, FragmentParserOptions.FromSafe(options), safe: true);
        RemoveFromParent();
        return stream;
    }

    /// <summary>Returns a writer that parses HTML and replaces this node with the result as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamreplacewithhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamReplaceWithHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Node streamReplaceWithHTMLUnsafe");
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        var stream = DynamicMarkup.StreamHtml(parent, NextSibling, compliantOptions, safe: false);
        RemoveFromParent();
        return stream;
    }
}

public sealed partial class ShadowRoot
{
    /// <summary>Returns a writer that parses HTML and replaces the children with the result as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-shadowroot-streamhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamHtml(SetHtmlOptions? options = null)
    {
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        var stream = DynamicMarkup.StreamHtml(target, null, FragmentParserOptions.FromSafe(options), safe: true);
        target.ReplaceAll(null);
        return stream;
    }

    /// <summary>Returns a writer that parses HTML and replaces the children with the result as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-shadowroot-streamhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "ShadowRoot streamHTMLUnsafe");
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        var stream = DynamicMarkup.StreamHtml(target, null, compliantOptions, safe: false);
        target.ReplaceAll(null);
        return stream;
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after the last child as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-shadowroot-streamappendhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAppendHtml(SetHtmlOptions? options = null)
    {
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after the last child as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-shadowroot-streamappendhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAppendHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "ShadowRoot streamAppendHTMLUnsafe");
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), null, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before the first child as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-shadowroot-streamprependhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamPrependHtml(SetHtmlOptions? options = null)
    {
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        return DynamicMarkup.StreamHtml(target, target.FirstChild, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before the first child as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-shadowroot-streamprependhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamPrependHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "ShadowRoot streamPrependHTMLUnsafe");
        var target = DynamicMarkup.GetTargetForHtmlInsertion(this);
        return DynamicMarkup.StreamHtml(target, target.FirstChild, compliantOptions, safe: false);
    }
}

public abstract partial class CharacterData
{
    /// <summary>Returns a writer that parses HTML and inserts the result before this node as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streambeforehtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamBeforeHtml(SetHtmlOptions? options = null)
    {
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetParentForHtmlStreaming(this), this, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result before this node as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streambeforehtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamBeforeHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Node streamBeforeHTMLUnsafe");
        return DynamicMarkup.StreamHtml(DynamicMarkup.GetParentForHtmlStreaming(this), this, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after this node as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamafterhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAfterHtml(SetHtmlOptions? options = null)
    {
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        return DynamicMarkup.StreamHtml(parent, NextSibling, FragmentParserOptions.FromSafe(options), safe: true);
    }

    /// <summary>Returns a writer that parses HTML and inserts the result after this node as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamafterhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamAfterHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Node streamAfterHTMLUnsafe");
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        return DynamicMarkup.StreamHtml(parent, NextSibling, compliantOptions, safe: false);
    }

    /// <summary>Returns a writer that parses HTML and replaces this node with the result as it is parsed (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamreplacewithhtml)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamReplaceWithHtml(SetHtmlOptions? options = null)
    {
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        var stream = DynamicMarkup.StreamHtml(parent, NextSibling, FragmentParserOptions.FromSafe(options), safe: true);
        RemoveFromParent();
        return stream;
    }

    /// <summary>Returns a writer that parses HTML and replaces this node with the result as it is parsed, without the default sanitizer (proposed by https://github.com/whatwg/html/pull/12758).</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced, the options are not <see cref="TrustedHtmlParserOptions"/> and the default policy did not create them.</exception>
    [NotInIdl("Proposed by https://github.com/whatwg/html/pull/12758: the binding returns a WritableStream (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-nondocumenttypechildnode-streamreplacewithhtmlunsafe)")]
    [Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
    public HtmlStreamWriter StreamReplaceWithHtmlUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        var compliantOptions = DynamicMarkup.GetCompliantStreamingOptions(this, options, "Node streamReplaceWithHTMLUnsafe");
        var parent = DynamicMarkup.GetParentForHtmlStreaming(this);
        var stream = DynamicMarkup.StreamHtml(parent, NextSibling, compliantOptions, safe: false);
        RemoveFromParent();
        return stream;
    }
}
