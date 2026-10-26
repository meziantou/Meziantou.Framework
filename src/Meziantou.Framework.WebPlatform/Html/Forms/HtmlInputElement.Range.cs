using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The slider of <input type=range> (https://html.spec.whatwg.org/multipage/input.html#range-state-(type=range)): the user
// changes the value by pressing the primary button of a pointer on the control and dragging it along the track, as in the
// browsers. The track is the content box of the control along its inline axis, and the value starts at the inline-start edge.
public partial class HtmlInputElement
{
    /// <summary>The length of the thumb of the slider along the track (the one of the browsers).</summary>
    private const double SliderThumbLength = 16;

    /// <summary>The value of the slider when the drag of the user started (a <c>change</c> event is fired at the end of the drag when it changed).</summary>
    private string? _sliderDragInitialValue;

    /// <summary>
    /// Returns the positions of the tick marks of a slider, from 0 (the minimum) to 1 (the maximum), or null when it has none:
    /// "the user agent could use the suggestion values as tick marks" (the values of the options of its <c>datalist</c>
    /// that are not disabled), as in the browsers only the valid floating-point numbers in the range that are not step
    /// mismatches (https://html.spec.whatwg.org/multipage/input.html#range-state-(type=range)).
    /// </summary>
    internal List<double>? GetSliderTickMarks()
    {
        if (_type != InputType.Range || List is not { } list)
            return null;

        var minimum = GetMinimum() ?? 0;
        var maximum = GetMaximum() ?? 100;
        if (maximum <= minimum)
            return null;

        var step = GetAllowedValueStep();
        var stepBase = GetStepBase();
        List<double>? result = null;
        foreach (var element in list.Options)
        {
            if (element is not HtmlOptionElement option || option.HasAttribute("disabled"))
                continue;

            var text = option.Value;
            if (Microsyntaxes.IsValidFloatingPointNumber(text) && Microsyntaxes.TryParseFloat(text, out var value) && value >= minimum && value <= maximum
                && (step is not { } allowedStep || IsIntegralMultiple(value, stepBase, allowedStep)))
            {
                (result ??= []).Add((value - minimum) / (maximum - minimum));
            }
        }

        return result;
    }

    /// <summary>
    /// The default action of the trusted pointer events for a slider: <c>pointerdown</c> of the primary button on the control
    /// starts a drag and sets the value from the position of the pointer, the <c>pointermove</c> events of the pointer update
    /// it (an <c>input</c> event is fired when it changes), and <c>pointerup</c> or <c>pointercancel</c> end the drag.
    /// </summary>
    internal static void RunSliderDefaultAction(PointerEvent @event, Node target, bool canceled, Document topDocument)
    {
        switch (@event.Type)
        {
            case "pointerdown":
                if (!canceled && @event.Button == 0 && target is HtmlInputElement { _type: InputType.Range, IsMutable: true } slider)
                {
                    topDocument.DraggedSlider = (slider, @event.PointerId);
                    slider._sliderDragInitialValue = slider._value;
                    slider.SetSliderValueFromPointer(@event);
                }

                break;

            case "pointermove":
                if (topDocument.DraggedSlider is var (dragged, pointerId) && pointerId == @event.PointerId)
                {
                    if (dragged.IsConnected && dragged.IsSlider && dragged.IsMutable)
                    {
                        dragged.SetSliderValueFromPointer(@event);
                    }
                    else
                    {
                        topDocument.DraggedSlider = null;
                    }
                }

                break;

            case "pointerup" or "pointercancel":
                if (topDocument.DraggedSlider is var (released, releasedPointerId) && releasedPointerId == @event.PointerId)
                {
                    topDocument.DraggedSlider = null;
                    var initialValue = released._sliderDragInitialValue;
                    released._sliderDragInitialValue = null;
                    if (released.IsConnected && initialValue is not null && initialValue != released._value)
                    {
                        released.UserValidity = true;
                        released.FireEvent(new Event("change", new EventInit { Bubbles = true }));
                    }
                }

                break;
        }
    }

    internal bool IsSlider => _type == InputType.Range;

    private void SetSliderValueFromPointer(PointerEvent @event)
    {
        if (CssomViewGeometry.GetLayoutEngine(this) is not { } engine || CssomViewGeometry.GetFirstBox(engine, this) is not { } fragment)
            return;

        // The event may be targeted at another document (the pointer left the frame of the control): its position is the one of
        // the viewport of its view
        if (!ReferenceEquals((@event.View as Window)?.Document, NodeDocument))
            return;

        if (!LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(new PhysicalOffset(@event.ClientX, @event.ClientY)), out var point))
            return;

        var track = fragment.ContentBoxRect;
        var direction = fragment.WritingDirection;
        var (position, start, length) = direction.IsHorizontal ? (point.X, track.Left, track.Width) : (point.Y, track.Top, track.Height);
        var available = length - SliderThumbLength;
        var ratio = available <= 0 ? 0 : Math.Clamp((position - start - (SliderThumbLength / 2)) / available, 0, 1);
        if (direction.IsRightToLeft)
        {
            ratio = 1 - ratio;
        }

        var minimum = GetMinimum() ?? 0;
        var maximum = GetMaximum() ?? 100;
        if (maximum < minimum)
            return;

        var previous = _value;
        _value = Microsyntaxes.ToBestFloatingPointRepresentation(minimum + (ratio * (maximum - minimum)));
        _dirtyValue = true;
        SanitizeValue();
        if (_value != previous)
        {
            NodeDocument.IncrementElementStateVersion();
            FireEvent(new Event("input", new EventInit { Bubbles = true, Composed = true }));
        }
    }
}
