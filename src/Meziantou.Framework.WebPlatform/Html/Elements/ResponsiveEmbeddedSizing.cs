using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

// Responsively-sized iframes (https://drafts.csswg.org/css-sizing-4/#responsive-iframes): the embedded document opts in with
// <meta name=responsive-embedded-sizing> before its body is opened, and the iframe whose frame-sizing is not auto takes the
// size of its contents in that axis (its internal layout intrinsic size, see ReplacedContent).
public partial class Document
{
    /// <summary>
    /// The responsive embedded sizing flag (https://drafts.csswg.org/css-sizing-4/#responsive-iframes): null while it is unset,
    /// true when a <c>&lt;meta name=responsive-embedded-sizing&gt;</c> element was encountered before the body element was opened.
    /// </summary>
    internal bool? ResponsiveEmbeddedSizingFlag { get; private set; }

    /// <summary>The <c>content</c> attribute of the meta element that set <see cref="ResponsiveEmbeddedSizingFlag"/> (its <c>allow-origins=</c> list).</summary>
    internal string? ResponsiveEmbeddedSizingContent { get; private set; }

    /// <summary>
    /// The locked embedded ICB size (https://drafts.csswg.org/css-sizing-4/#responsive-iframes): the size of the initial
    /// containing block of the first layout once the flag is true, used by the next layouts.
    /// </summary>
    internal PhysicalSize? LockedEmbeddedIcbSize { get; set; }

    /// <summary>
    /// Sets the responsive embedded sizing flag when it is unset: true when a meta element named <c>responsive-embedded-sizing</c>
    /// is encountered (inserted in the document, or renamed), false when the parser opens the body element. The flag does not
    /// change once set. Setting it forgets the internal layout intrinsic size that the container kept during the navigation.
    /// </summary>
    internal void SetResponsiveEmbeddedSizingFlag(bool value, string? content)
    {
        if (ResponsiveEmbeddedSizingFlag is not null)
            return;

        ResponsiveEmbeddedSizingFlag = value;
        ResponsiveEmbeddedSizingContent = content;
        if (BrowsingContext is { Container: HtmlIFrameElement iframe } browsingContext && ReferenceEquals(browsingContext.Document, this))
        {
            iframe.SetInternalLayoutIntrinsicSize(null);
        }
    }

    /// <summary>
    /// Sets the internal layout intrinsic size of the container of the document (after its DOMContentLoaded event, its load
    /// event and <c>requestResize()</c>): the size of its contents, when it opted in and its embedder is allowed.
    /// </summary>
    internal void UpdateInternalLayoutIntrinsicSize()
    {
        if (ResponsiveEmbeddedSizingFlag != true || BrowsingContext is not { Container: HtmlIFrameElement iframe } browsingContext || !ReferenceEquals(browsingContext.Document, this))
            return;

        if (!ResponsiveEmbeddedSizing.AllowsEmbedder(ResponsiveEmbeddedSizingContent, iframe.NodeDocument.Origin, Origin))
            return;

        if (GetLayoutEngine().EnsureLayout() is not { } viewport)
            return;

        iframe.SetInternalLayoutIntrinsicSize(ResponsiveEmbeddedSizing.MeasureContents(viewport, GetLayoutEngine().InitialContainingBlockSize));
    }
}

public partial class HtmlIFrameElement
{
    /// <summary>
    /// The internal layout intrinsic size of the element (https://drafts.csswg.org/css-sizing-4/#responsive-iframes), in the CSS
    /// pixels of its embedded document, or null: its natural size in the axis given by <c>frame-sizing</c>.
    /// </summary>
    internal PhysicalSize? InternalLayoutIntrinsicSize { get; private set; }

    internal void SetInternalLayoutIntrinsicSize(PhysicalSize? size)
    {
        if (InternalLayoutIntrinsicSize == size)
            return;

        InternalLayoutIntrinsicSize = size;
        NodeDocument.InvalidateLayout();
    }
}

public sealed partial class Window
{
    /// <summary>
    /// https://drafts.csswg.org/css-sizing-4/#dom-window-requestresize: updates the internal layout intrinsic size of the iframe
    /// that contains the document from the size of its contents.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-sizing-4/#dom-window-requestresize"/>
    [IdlMember("requestResize", "https://drafts.csswg.org/css-sizing-4/#dom-window-requestresize")]
    public void RequestResize()
    {
        if (BrowsingContext is not { Parent: not null } browsingContext)
            throw DomException.NotAllowed("The window is not the window of a child navigable.");

        if (browsingContext.Container is not HtmlIFrameElement)
            throw DomException.NotAllowed("The container of the navigable is not an iframe element.");

        var document = Document;
        if (document.ResponsiveEmbeddedSizingFlag != true)
            throw DomException.NotAllowed("The document did not opt into responsive embedded sizing.");

        document.UpdateInternalLayoutIntrinsicSize();
    }
}

internal static class ResponsiveEmbeddedSizing
{
    public const string MetaName = "responsive-embedded-sizing";

    /// <summary>
    /// Returns the size of the contents of a document from its viewport fragment: the extent of the root element and its
    /// scrollable overflow from the start of the initial containing block (the initial containing block itself is not
    /// included: a root element smaller than the iframe makes it smaller).
    /// </summary>
    public static PhysicalSize MeasureContents(BoxFragment viewport, PhysicalSize initialContainingBlock)
    {
        var contents = viewport.GetContentsOverflow();
        var width = viewport.WritingDirection.IsFlippedBlocks ? initialContainingBlock.Width - contents.Left : contents.Right;
        return new PhysicalSize(Math.Max(0, width), Math.Max(0, contents.Bottom));
    }

    /// <summary>
    /// Returns whether the content of the meta element allows the origin of the embedder, as in Chrome (the
    /// <c>allow-origins=</c> prefix followed by a list of source expressions separated by whitespace: <c>*</c>, <c>'self'</c>
    /// (the origin of the embedded document), a scheme (<c>https:</c>), an origin, or an origin whose host starts with
    /// <c>*.</c> (its subdomains)). A meta element without this list does not allow any embedder.
    /// </summary>
    public static bool AllowsEmbedder(string? content, OriginRecord embedder, OriginRecord embedded)
    {
        const string Prefix = "allow-origins=";
        if (content is null || !content.StartsWith(Prefix, StringComparison.Ordinal) || embedder.IsOpaque)
            return false;

        var serialized = embedder.Serialize();
        foreach (var token in content[Prefix.Length..].Split(['\t', '\n', '\f', '\r', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*")
                return true;

            if (token == "'self'")
            {
                if (OriginRecord.IsSameOrigin(embedder, embedded))
                    return true;

                continue;
            }

            if (token.EndsWith(':', StringComparison.Ordinal) && !token.Contains('/', StringComparison.Ordinal))
            {
                if (string.Equals(token[..^1], embedder.Scheme, StringComparison.OrdinalIgnoreCase))
                    return true;

                continue;
            }

            var wildcard = token.IndexOf("://*.", StringComparison.Ordinal);
            if (wildcard > 0)
            {
                // scheme://*.host[:port]: the hosts that end with .host
                var suffix = token[(wildcard + "://*".Length)..];
                var scheme = token[..wildcard];
                var hostAndPort = serialized[(serialized.IndexOf("://", StringComparison.Ordinal) + 3)..];
                if (string.Equals(scheme, embedder.Scheme, StringComparison.OrdinalIgnoreCase) && hostAndPort.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && hostAndPort.Length > suffix.Length)
                    return true;

                continue;
            }

            if (string.Equals(token.TrimEnd('/'), serialized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
