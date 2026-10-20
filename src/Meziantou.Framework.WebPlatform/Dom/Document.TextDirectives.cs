namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>
    /// Gets the fragment directive of the document (https://wicg.github.io/scroll-to-text-fragment/#dom-document-fragmentdirective):
    /// an object without members, which tells the scripts that the text directives are supported. The fragment directive of
    /// the URL is not exposed.
    /// </summary>
    /// <seealso href="https://wicg.github.io/scroll-to-text-fragment/#dom-document-fragmentdirective"/>
    [IdlMember("fragmentDirective", "https://wicg.github.io/scroll-to-text-fragment/#dom-document-fragmentdirective")]
    public FragmentDirective FragmentDirective => field ??= new FragmentDirective();

    /// <summary>
    /// https://wicg.github.io/scroll-to-text-fragment/#document-pending-text-directives: the text directives of the session
    /// history entry of the document that are not searched yet, or null.
    /// </summary>
    internal List<TextDirective>? PendingTextDirectives { get; set; }

    /// <summary>
    /// https://wicg.github.io/scroll-to-text-fragment/#document-text-directive-user-activation: set when the document is
    /// created by a navigation the user is involved in, and consumed by the scroll to a text directive, or else by the
    /// next navigation of the navigable (a client-side redirect).
    /// </summary>
    internal bool TextDirectiveUserActivation { get; set; }

    /// <summary>
    /// The "allow text directive scroll" flag of the navigation that created the document
    /// (https://wicg.github.io/scroll-to-text-fragment/#check-if-a-text-directive-can-be-scrolled), used when the document
    /// tries to scroll to its fragment once it is parsed.
    /// </summary>
    internal bool AllowTextDirectiveScroll { get; set; }

    /// <summary>
    /// Whether the policy value of the <c>force-load-at-top</c> document policy feature is true
    /// (https://wicg.github.io/scroll-to-text-fragment/#restricting-scroll-on-load): the document does not scroll to its
    /// fragment nor restores a scroll position when it is loaded.
    /// </summary>
    internal bool ForceLoadAtTop { get; set; }

    /// <summary>
    /// The ranges of the text directives of the session history entry of the document that are found in the document
    /// (https://wicg.github.io/scroll-to-text-fragment/#invoking-text-directives): the target text, which the
    /// <c>::target-text</c> pseudo-element styles. The ranges are live: they follow the mutations of the document. Null when
    /// the document has no target text.
    /// </summary>
    internal List<Range>? TargetTextRanges { get; private set; }

    /// <summary>
    /// Whether the indicated part of the document is the first range of <see cref="TargetTextRanges"/>, and the document was
    /// allowed to scroll to it: its target element (<c>:target</c>) is then the first common ancestor of the start and the
    /// end of the range that is an element.
    /// </summary>
    internal bool IsTargetTextIndicated
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                IncrementElementStateVersion();
            }
        }
    }

    private void SetTargetTextRanges(List<Range>? ranges)
    {
        // The highlighted text changes, which is not a mutation of the tree
        if (TargetTextRanges is not null || ranges is not null)
        {
            IncrementElementStateVersion();
        }

        TargetTextRanges = ranges;
    }

    /// <summary>
    /// https://wicg.github.io/scroll-to-text-fragment/#invoke-text-directives: searches the pending text directives of the
    /// document in its text. The ranges found are its target text; the first one, which is returned, is the indicated part
    /// of the document. Returns null when the document has no pending text directives or when none is found.
    /// </summary>
    internal Range? InvokePendingTextDirectives()
    {
        if (PendingTextDirectives is not { } directives)
            return null;

        var ranges = TextDirectives.FindRanges(this, directives);
        SetTargetTextRanges(ranges.Count > 0 ? ranges : null);
        return ranges.Count > 0 ? ranges[0] : null;
    }

    /// <summary>
    /// https://wicg.github.io/scroll-to-text-fragment/#check-if-a-text-directive-can-be-scrolled: consumes the text directive
    /// user activation of the document.
    /// </summary>
    /// <param name="initiatorOrigin">The origin of the initiator of the navigation, or null.</param>
    /// <param name="userInvolvement">The user navigation involvement.</param>
    /// <param name="isSameDocumentNavigation">Whether the navigation is a fragment navigation of the document.</param>
    /// <remarks>
    /// As in the browsers (https://github.com/WICG/scroll-to-text-fragment/issues/240, and the web-platform-tests), a
    /// same-document navigation initiated by a same-origin document needs neither a user activation nor a top-level navigable
    /// (the initiator can already read the document and scroll it).
    /// </remarks>
    internal bool CheckIfTextDirectiveCanBeScrolled(OriginRecord? initiatorOrigin, UserNavigationInvolvement userInvolvement, bool isSameDocumentNavigation)
    {
        if (PendingTextDirectives is not { Count: > 0 })
            return false;

        var isUserInvolved = TextDirectiveUserActivation || userInvolvement is UserNavigationInvolvement.Activation or UserNavigationInvolvement.BrowserUI;
        TextDirectiveUserActivation = false;

        // https://wicg.github.io/scroll-to-text-fragment/#text-directive-allowing-mime-type
        if (ContentType is not ("text/html" or "text/plain"))
            return false;

        if (userInvolvement == UserNavigationInvolvement.BrowserUI)
            return true;

        if (!isSameDocumentNavigation && (!isUserInvolved || BrowsingContext?.Parent is not null))
            return false;

        if (initiatorOrigin is not null && OriginRecord.IsSameOrigin(Origin, initiatorOrigin))
            return true;

        // "If document's browsing context's group's browsing context set has length 1, return true": the navigator has no
        // script access to the document
        return !isSameDocumentNavigation && BrowsingContext is { Parent: null } navigable && navigable.IsAloneInItsGroup;
    }
}

/// <summary>
/// https://wicg.github.io/scroll-to-text-fragment/#fragmentdirective: the object returned by <see cref="Document.FragmentDirective"/>,
/// which has no members (it is there for the feature detection of the text directives).
/// </summary>
/// <seealso href="https://wicg.github.io/scroll-to-text-fragment/#fragmentdirective"/>
[IdlInterface("FragmentDirective", "https://wicg.github.io/scroll-to-text-fragment/#fragmentdirective")]
public sealed class FragmentDirective
{
    internal FragmentDirective()
    {
    }
}
