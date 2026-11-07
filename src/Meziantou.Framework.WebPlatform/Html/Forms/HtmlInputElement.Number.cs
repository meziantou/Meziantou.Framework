using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

// The spin buttons and the arrow keys of <input type=number> (https://html.spec.whatwg.org/multipage/input.html#number-state-(type=number)):
// as in the browsers, the up and down arrow keys and a press of the primary button of a pointer on a spin button step the
// value up or down by one step. The change is a user edit (https://w3c.github.io/input-events/): beforeinput is fired first
// and can cancel it, then input and change are fired.
public partial class HtmlInputElement
{
    /// <summary>The width of the spin buttons, at the inline-end edge of the padding box of the control (the one of the browsers).</summary>
    private const double SpinButtonsWidth = 15;

    /// <summary>The default action of the trusted <c>keydown</c> event of the up and down arrow keys in a number control. Returns false when the key does not step the value.</summary>
    internal static bool RunNumberKeyDefaultAction(KeyboardEvent @event, Node target)
    {
        if (target is not HtmlInputElement { _type: InputType.Number, IsMutable: true } input || @event.Key is not ("ArrowUp" or "ArrowDown") || @event.CtrlKey || @event.AltKey || @event.MetaKey)
            return false;

        input.StepByUser(up: @event.Key == "ArrowUp");
        return true;
    }

    /// <summary>The default action of the trusted <c>pointerdown</c> event of the primary button on a spin button of a number control.</summary>
    internal static void RunSpinButtonDefaultAction(PointerEvent @event, Node target, bool canceled)
    {
        if (canceled || @event.Type != "pointerdown" || @event.Button != 0 || target is not HtmlInputElement { _type: InputType.Number, IsMutable: true } input)
            return;

        if (CssomViewGeometry.GetLayoutEngine(input) is not { } engine || CssomViewGeometry.GetFirstBox(engine, input) is not { } fragment)
            return;

        if (!ReferenceEquals((@event.View as Window)?.Document, input.NodeDocument))
            return;

        if (!LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(new PhysicalOffset(@event.ClientX, @event.ClientY)), out var point))
            return;

        // The spin buttons are stacked at the inline-end edge of the padding box: the upper one steps up
        var box = fragment.PaddingBoxRect;
        var direction = fragment.WritingDirection;
        if (!direction.IsHorizontal || point.Y < box.Top || point.Y >= box.Bottom)
            return;

        var inSpinButtons = direction.IsRightToLeft
            ? point.X >= box.Left && point.X < box.Left + SpinButtonsWidth
            : point.X >= box.Right - SpinButtonsWidth && point.X < box.Right;
        if (inSpinButtons)
        {
            input.StepByUser(up: point.Y < box.Top + (box.Height / 2));
        }
    }

    /// <summary>
    /// Steps the value up or down by one step for the user: <c>beforeinput</c> (cancelable, <c>insertReplacementText</c>), then
    /// the value changes and <c>input</c> and <c>change</c> are fired.
    /// </summary>
    private void StepByUser(bool up)
    {
        if (!TryGetSteppedValue(1, up, out var value) || value == _value)
            return;

        var document = NodeDocument;
        var view = document.DefaultView;
        if (!FireEvent(new InputEvent("beforeinput", new InputEventInit { Bubbles = true, Cancelable = true, Composed = true, Data = value, InputType = "insertReplacementText", View = view })))
            return;

        // A listener may have removed the control or its document
        if (!IsConnected || !ReferenceEquals(NodeDocument, document) || !document.IsFullyActive || !IsMutable || _type != InputType.Number)
            return;

        _value = value;
        _dirtyValue = true;
        _userInput = null;
        SanitizeValue();
        document.IncrementElementStateVersion();
        FireEvent(new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, Data = value, InputType = "insertReplacementText", View = view }));
        if (!IsConnected || !document.IsFullyActive)
            return;

        UserValidity = true;
        FireEvent(new Event("change", new EventInit { Bubbles = true }));
    }
}
