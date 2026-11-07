using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://drafts.csswg.org/css-pseudo-4/#window-interface
public partial class Element
{
    /// <summary>
    /// Returns the pseudo-element of the element designated by a pseudo-element selector (e.g. <c>"::before"</c>), or null when
    /// the selector is invalid or does not designate a tree-abiding pseudo-element that is not element-backed
    /// (https://drafts.csswg.org/css-pseudo-4/#dom-element-pseudo). The pseudo-element is returned even when it is not
    /// generated, and the same object is returned for the same pseudo-element.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-pseudo-4/#dom-element-pseudo"/>
    [IdlMember("pseudo", "https://drafts.csswg.org/css-pseudo-4/#dom-element-pseudo")]
    public CssPseudoElement? Pseudo(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return CssPseudoElement.Get(this, type);
    }
}
