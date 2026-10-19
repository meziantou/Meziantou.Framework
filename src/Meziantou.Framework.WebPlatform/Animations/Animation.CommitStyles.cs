using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Animations;

public partial class Animation
{
    /// <summary>
    /// Writes the current values of the effect of the animation (and of the animations below it in the composite order) to
    /// the <c>style</c> attribute of its target (https://drafts.csswg.org/web-animations-1/#dom-animation-commitstyles).
    /// </summary>
    /// <exception cref="DomException">
    /// The target is a pseudo-element or an element without <c>style</c> attribute (NoModificationAllowedError), or it is not
    /// being rendered (InvalidStateError).
    /// </exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-commitstyles"/>
    [IdlMember("commitStyles", "https://drafts.csswg.org/web-animations-1/#dom-animation-commitstyles")]
    public void CommitStyles()
    {
        FlushPendingStyle();
        if (_effect is not KeyframeEffect { TargetElement: { } target } effect)
            return;

        if (effect.PseudoElementName is not null || target is not (HtmlElement or SvgElement or MathMLElement))
            throw new DomException("The target of the animation has no style attribute.", "NoModificationAllowedError");

        if (BoxGeneration.Get(target).IsDisplayNone)
            throw new DomException("The target of the animation is not being rendered.", "InvalidStateError");

        var style = target.NodeDocument.GetStyleEngine().GetStyle(target);
        var values = new List<(string Property, string Value)>();
        using var endpointInclusive = AnimationEffect.UseEndpointInclusiveActiveInterval();
        foreach (var property in effect.AnimatedProperties)
        {
            var physical = CssPropertyRegistry.IsCustomProperty(property) ? property : LogicalProperties.GetPhysicalProperty(property, style) ?? property;
            if (style.ComputeCommittedValue(physical, this) is { } value)
            {
                values.Add((physical, physical == "transform" ? Css.Animations.CssTransformInterpolation.SerializeCommitted(value) : value));
            }
        }

        using var scope = CEReactionsScope.Enter();
        var inlineStyle = target.GetInlineStyle();
        foreach (var (property, value) in values)
        {
            inlineStyle.SetComputedProperty(property, value);
        }
    }
}
