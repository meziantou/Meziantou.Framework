using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An origin (https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-interface), for the same origin and same site
/// comparisons.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-interface"/>
[IdlInterface("Origin", "https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-interface")]
public sealed class Origin
{
    /// <summary>Creates a new opaque origin (https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-constructor).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-constructor"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-constructor")]
    public Origin()
    {
        Record = OriginRecord.CreateOpaque();
    }

    internal Origin(OriginRecord record)
    {
        Record = record;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#concept-origin-origin</summary>
    internal OriginRecord Record { get; }

    /// <summary>
    /// Returns the origin of a value (https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-from): the origin of a
    /// platform object that has one (<see cref="Origin"/>, a same origin-domain <see cref="Window"/>, a <see cref="WorkerGlobalScope"/>, a <see cref="MessageEvent"/>
    /// fired by the user agent, <see cref="Url"/>, the <c>a</c> and <c>area</c> elements with a URL), or the origin of a
    /// URL string.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="Origin"/> object.</returns>
    /// <exception cref="TypeErrorException">The value has no origin.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-from"/>
    [IdlMember("from", "https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-from")]
    public static Origin From(object? value)
    {
        if (ExtractOrigin(value) is { } origin)
            return new Origin(origin);

        if (value is string text && UrlParser.Parse(text, baseUrl: null) is { } url)
            return new Origin(OriginRecord.FromUrl(url));

        throw new TypeErrorException("The value has no origin");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#extract-an-origin</summary>
    private static OriginRecord? ExtractOrigin(object? value)
    {
        switch (value)
        {
            case Origin origin:
                return origin.Record;

            case Window window:
            {
                // https://html.spec.whatwg.org/multipage/webappapis.html#windoworworkerglobalscope-mixin: only a same
                // origin-domain window exposes its origin. The blank document of the code that runs without global object
                // (e.g. .NET code) has no browsing context: it does not restrict the access.
                var entryDocument = Document.EntryGlobalDocument ?? Document.CurrentGlobalDocumentOrNull;
                if (entryDocument?.BrowsingContext is not null && !Document.IsSameOriginDomain(window.Document, entryDocument))
                    return null;

                return window.Document.Origin;
            }

            case WorkerGlobalScope worker:
                return worker.OriginRecord;

            case MessageEvent messageEvent:
                return messageEvent.OriginRecord;

            case Url url:
                return OriginRecord.FromUrl(url.Record);

            // https://html.spec.whatwg.org/multipage/links.html#hyperlink-element: the origin of the url, if any
            case HtmlAnchorElement or HtmlAreaElement:
                return HyperlinkElementUtils.GetUrl((Element)value) is { } hyperlinkUrl ? OriginRecord.FromUrl(hyperlinkUrl) : null;

            default:
                return null;
        }
    }

    /// <summary>Gets whether the origin is opaque (https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-opaque).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-opaque"/>
    [IdlMember("opaque", "https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-opaque")]
    public bool Opaque => Record.IsOpaque;

    /// <summary>Returns whether the origin is same origin with another one (https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-issameorigin).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-issameorigin"/>
    [IdlMember("isSameOrigin", "https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-issameorigin")]
    public bool IsSameOrigin(Origin other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OriginRecord.IsSameOrigin(Record, other.Record);
    }

    /// <summary>Returns whether the origin is same site with another one (https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-issamesite).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-issamesite"/>
    [IdlMember("isSameSite", "https://html.spec.whatwg.org/multipage/browsers.html#dom-origin-issamesite")]
    public bool IsSameSite(Origin other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OriginRecord.IsSameSite(Record, other.Record);
    }

    /// <inheritdoc/>
    public override string ToString() => Record.Serialize();
}
