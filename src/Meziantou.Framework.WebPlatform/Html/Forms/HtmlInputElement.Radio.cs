using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class HtmlInputElement
{
    /// <summary>
    /// The default action of a trusted <c>keydown</c> event of an arrow key on a radio button of a radio button group, as in the
    /// browsers (unspecified: https://html.spec.whatwg.org/multipage/input.html#radio-button-state-(type=radio) lets the user
    /// agent choose): the next (the down and right arrow keys) or the previous (the up and left arrow keys) radio button of
    /// the group in tree order that can be focused, wrapping around at its ends, is focused and checked by a synthetic click
    /// (its activation behavior fires the <c>input</c> and <c>change</c> events). The left and right arrow keys are reversed
    /// when the direction of the radio button is <c>rtl</c>. The radio button is scrolled into view (<c>nearest</c>). Returns
    /// whether the key was handled.
    /// </summary>
    internal static bool RunRadioKeyDefaultAction(KeyboardEvent @event, Node target)
    {
        if (target is not HtmlInputElement { _type: InputType.Radio } radio || @event.Key is not ("ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight") || @event.CtrlKey || @event.AltKey || @event.MetaKey)
            return false;

        var group = radio.GetRadioButtonGroup(includeSelf: true);
        var index = group.IndexOf(radio);
        if (index < 0 || group.Count < 2)
            return false;

        var forward = @event.Key is "ArrowDown" or "ArrowRight";
        if (@event.Key is "ArrowLeft" or "ArrowRight" && ComputedStyleDeclarations.IsRendered(radio) && radio.NodeDocument.GetStyleEngine().GetStyle(radio).GetComputedValue("direction") == "rtl")
        {
            forward = !forward;
        }

        for (var step = 1; step < group.Count; step++)
        {
            var candidate = group[(index + (forward ? step : group.Count - step)) % group.Count];
            if (!FocusModel.IsFocusableElement(candidate))
                continue;

            FocusModel.Focus(candidate, focusVisible: true);

            // As in the browsers, the focused radio button is scrolled into view when it is not visible
            if (candidate.NodeDocument is { BrowsingContext: not null } document && ComputedStyleDeclarations.IsBeingRendered(candidate))
            {
                document.GetLayoutEngine().ScrollIntoViewWhenLaidOut(candidate, ScrollLogicalPosition.Nearest, ScrollLogicalPosition.Nearest);
            }

            UserAgentDefaultActions.FireSyntheticClick(candidate);
            return true;
        }

        return true;
    }
}
