namespace Meziantou.Framework.WebPlatform.Dom;

// Scoped view transitions (https://drafts.csswg.org/css-view-transitions-2/#scoped-view-transitions): the view transitions
// hosted by an element, which animate its subtree.
public partial class Element
{
    /// <summary>
    /// Gets the active view transition of the element, or null (https://drafts.csswg.org/css-view-transitions-2/#dom-element-activeviewtransition):
    /// the scoped view transition started on the element, or the view transition of the document for the document element.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-element-activeviewtransition"/>
    [IdlMember("activeViewTransition", "https://drafts.csswg.org/css-view-transitions-2/#dom-element-activeviewtransition")]
    public ViewTransition? ActiveViewTransition => NodeDocument.GetActiveViewTransition(this);

    /// <summary>
    /// Starts a view transition whose root element is the element (https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition):
    /// the old state of the element and of its subtree is captured at the next rendering opportunity, then the callback updates
    /// the document, and the transition between the two states is animated by a tree of pseudo-elements that originate from the
    /// element and are painted above its contents. The active view transition of the element, if any, is skipped. On the
    /// document element, it is the same as <see cref="Document.StartViewTransition(ViewTransitionUpdateCallback)"/>.
    /// </summary>
    /// <param name="callback">The callback that updates the document. It is always called, even when the transition cannot happen.</param>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition"/>
    [IdlMember("startViewTransition", "https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition")]
    public ViewTransition StartViewTransition(ViewTransitionUpdateCallback callback) => NodeDocument.StartViewTransitionCore(this, callback, types: null);

    /// <summary>
    /// Starts a view transition whose root element is the element, with options (https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition):
    /// the update callback and the types of the transition.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition"/>
    [IdlMember("startViewTransition", "https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition")]
    public ViewTransition StartViewTransition(StartViewTransitionOptions? options = null) => NodeDocument.StartViewTransitionCore(this, options?.Update, options?.Types);
}
