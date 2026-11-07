namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The history of the visited links of a user, which the <c>:visited</c> pseudo-class matches against
/// (https://drafts.csswg.org/selectors-4/#link): see <see cref="BrowsingContextOptions.VisitedLinks"/>. An implementation
/// must be thread-safe when the store is shared by browsing contexts that run on several threads.
/// </summary>
/// <remarks>
/// The entries are the triples of the partitioned history that Selectors describes
/// (https://drafts.csswg.org/selectors-4/#visited-partitioning, "Example Privacy-Preserving :visited Restrictions"): a store
/// decides how it uses the partition (see <see cref="InMemoryVisitedLinkStore"/>).
/// </remarks>
public abstract class VisitedLinkStore
{
    /// <summary>
    /// Gets a value that changes when the answers of <see cref="IsVisited"/> may have changed (e.g. a counter incremented
    /// when a link is added or when the history is cleared): the styles of the documents are computed again.
    /// </summary>
    public abstract long Version { get; }

    /// <summary>Returns whether a link of a document was visited.</summary>
    /// <param name="link">The URL of the link, and the document that contains the link.</param>
    public abstract bool IsVisited(VisitedLink link);

    /// <summary>
    /// Records a navigation of a browsing context that uses the store: the navigations to a document, the fragment
    /// navigations, and the URL changes of <c>history.pushState()</c> and <c>history.replaceState()</c>. A navigation is
    /// recorded for its destination (its URL, with the origin of the new document and the site of its top-level document,
    /// when the document is a top-level document or has the origin of its top-level document) and, when a document started
    /// it (a link, a form or a script), for that document (the same URL, with the origin of the document and the site of its
    /// top-level document).
    /// </summary>
    /// <param name="link">The URL of the navigation, and the document that observes it.</param>
    public abstract void Add(VisitedLink link);
}

/// <summary>
/// An entry of a <see cref="VisitedLinkStore"/> (https://drafts.csswg.org/selectors-4/#visited-partitioning): the URL of a
/// link and the partition of the document that contains the link.
/// </summary>
/// <param name="Url">The absolute URL of the link, serialized with its fragment.</param>
/// <param name="FrameOrigin">The serialized origin of the document that contains the link, e.g. <c>https://www.example.com</c>.</param>
/// <param name="TopLevelSite">
/// The serialized site of the top-level document of the document that contains the link
/// (https://html.spec.whatwg.org/multipage/browsers.html#obtain-a-site): the scheme and the registrable domain, e.g.
/// <c>https://example.com</c>; the serialized origin when the host has no registrable domain (e.g. an IP address).
/// </param>
public readonly record struct VisitedLink(string Url, string FrameOrigin, string TopLevelSite);

/// <summary>
/// A <see cref="VisitedLinkStore"/> that keeps the visited links in memory. It is thread-safe, and can be shared by several
/// browsing contexts.
/// </summary>
public sealed class InMemoryVisitedLinkStore : VisitedLinkStore
{
    private readonly Lock _lock = new();
    private readonly HashSet<VisitedLink> _links = [];
    private readonly bool _partitioned;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="InMemoryVisitedLinkStore"/> class, partitioned by the origin of the document of the link and by the site of its top-level document.</summary>
    public InMemoryVisitedLinkStore()
        : this(partitioned: true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InMemoryVisitedLinkStore"/> class.</summary>
    /// <param name="partitioned">
    /// Whether a link is only visited in the documents that could observe its navigation: the documents of the same origin, in
    /// a top-level document of the same site (https://drafts.csswg.org/selectors-4/#visited-partitioning). When false, the
    /// URL alone is the key: a visited link is visited in every document.
    /// </param>
    public InMemoryVisitedLinkStore(bool partitioned)
    {
        _partitioned = partitioned;
    }

    /// <inheritdoc/>
    public override long Version => Interlocked.Read(ref _version);

    /// <inheritdoc/>
    public override bool IsVisited(VisitedLink link)
    {
        lock (_lock)
        {
            return _links.Contains(GetKey(link));
        }
    }

    /// <inheritdoc/>
    public override void Add(VisitedLink link)
    {
        ArgumentNullException.ThrowIfNull(link.Url);
        lock (_lock)
        {
            if (_links.Add(GetKey(link)))
            {
                _version++;
            }
        }
    }

    /// <summary>Removes all the visited links.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_links.Count > 0)
            {
                _links.Clear();
                _version++;
            }
        }
    }

    private VisitedLink GetKey(VisitedLink link) => _partitioned ? link : new VisitedLink(link.Url, "", "");
}
