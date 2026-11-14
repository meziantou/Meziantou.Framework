using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// Plays the user of the page: the mouse, the keyboard and the touch screen. The input is dispatched as trusted events
/// with the default actions of a browser (focus, text input, clicks, form submission, scrolling, drag and drop); the
/// pointer targets the element under the pointer (hit testing), so an element covered by another one does not get the
/// clicks. As in Playwright, each action first waits until the element is actionable (attached, visible, stable, enabled,
/// editable, not covered, depending on the action), then waits until the page is idle.
/// </summary>
public sealed class PageInput
{
    private readonly TestPage _page;
    private PointerInput? _touch;

    internal PageInput(TestPage page) => _page = page;

    /// <summary>Gets the user input of WebPlatform, to send low-level input. Call <see cref="TestPage.WaitForIdleAsync"/> afterwards.</summary>
    public UserInput Raw => _page.BrowsingContext.Input;

    /// <summary>
    /// Clicks an element at its center (or at <see cref="ClickOptions.Position"/>), once it is visible, stable, enabled and
    /// not covered by another element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task ClickAsync(Element element, ClickOptions? options = null, CancellationToken cancellationToken = default) => ClickAsync(ActionTarget.From(element), options, cancellationToken);

    /// <summary>Clicks a position of the viewport, in CSS pixels.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task ClickAsync(double x, double y, ClickOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ClickOptions.Default;
        return RunAsync(input =>
        {
            var previousModifiers = input.Modifiers;
            input.Modifiers = options.Modifiers;
            try
            {
                input.Mouse.Move(x, y);
                for (var i = 0; i < options.ClickCount; i++)
                {
                    input.Mouse.Down((int)options.Button);
                    input.Mouse.Up((int)options.Button);
                }
            }
            finally
            {
                input.Modifiers = previousModifiers;
            }
        }, cancellationToken, waitAfter: !options.NoWaitAfter);
    }

    /// <summary>Double-clicks an element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task DoubleClickAsync(Element element, CancellationToken cancellationToken = default) => ClickAsync(element, new ClickOptions { ClickCount = 2 }, cancellationToken);

    /// <summary>Clicks an element with the secondary button (the <c>contextmenu</c> event).</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task RightClickAsync(Element element, CancellationToken cancellationToken = default) => ClickAsync(element, new ClickOptions { Button = MouseButton.Right }, cancellationToken);

    /// <summary>Moves the mouse over an element, once it is visible, stable and not covered by another element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task HoverAsync(Element element, CancellationToken cancellationToken = default) => HoverAsync(ActionTarget.From(element), cancellationToken);

    /// <summary>Taps an element with a finger (pointer, touch and mouse compatibility events, then <c>click</c>).</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task TapAsync(Element element, CancellationToken cancellationToken = default) => TapAsync(ActionTarget.From(element), cancellationToken);

    /// <summary>Swipes from the center of an element with a finger: a moving touch scrolls the page and fires the touch events.</summary>
    /// <param name="element">The element where the swipe starts.</param>
    /// <param name="deltaX">The horizontal distance, in CSS pixels.</param>
    /// <param name="deltaY">The vertical distance, in CSS pixels.</param>
    /// <param name="steps">The number of moves.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public async Task SwipeAsync(Element element, double deltaX, double deltaY, int steps = 10, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        var (_, x, y) = await WaitForActionableAsync(ActionTarget.From(element), Actionability.Pointer, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input =>
        {
            var touch = _touch ??= input.CreatePointer("touch");
            touch.Move(x, y);
            touch.Down();
            for (var i = 1; i <= steps; i++)
            {
                touch.Move(x + (deltaX * i / steps), y + (deltaY * i / steps));
            }

            touch.Up();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns the mouse wheel over an element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="deltaX">The horizontal delta, in CSS pixels.</param>
    /// <param name="deltaY">The vertical delta, in CSS pixels.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public async Task WheelAsync(Element element, double deltaX, double deltaY, CancellationToken cancellationToken = default)
    {
        var (_, x, y) = await WaitForActionableAsync(ActionTarget.From(element), Actionability.Pointer, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input => input.Wheel(x, y, deltaX, deltaY), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drags an element with the mouse, and drops it on another element (the HTML drag and drop events).</summary>
    /// <param name="source">The dragged element.</param>
    /// <param name="target">The element where the source is dropped.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task DragAndDropAsync(Element source, Element target, CancellationToken cancellationToken = default) => DragAndDropAsync(ActionTarget.From(source), ActionTarget.From(target), cancellationToken);

    /// <summary>Focuses an element, and types a text with the keyboard (the text is appended to the value of a field).</summary>
    /// <param name="element">The element.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task TypeAsync(Element element, string text, CancellationToken cancellationToken = default) => TypeAsync(ActionTarget.From(element), text, cancellationToken);

    /// <summary>Types a text with the keyboard in the focused element.</summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task TypeAsync(string text, CancellationToken cancellationToken = default) => RunAsync(input => input.Keyboard.Type(text), cancellationToken);

    /// <summary>
    /// Replaces the value of a text field with a text typed with the keyboard, then leaves the field (the <c>change</c>
    /// event, so <c>@bind</c> updates the value). Waits until the field is visible, enabled and editable. An element of a
    /// <c>label</c> fills the control of the label.
    /// </summary>
    /// <param name="element">The text field (<c>input</c>, <c>textarea</c> or <c>contenteditable</c> element).</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a text field.</exception>
    public Task FillAsync(Element element, string text, CancellationToken cancellationToken = default) => FillAsync(ActionTarget.From(element), text, cancellationToken);

    /// <summary>Clears the value of a text field, then leaves the field.</summary>
    /// <param name="element">The text field.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task ClearAsync(Element element, CancellationToken cancellationToken = default) => FillAsync(element, "", cancellationToken);

    /// <summary>Focuses an element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task FocusAsync(Element element, CancellationToken cancellationToken = default) => FocusAsync(ActionTarget.From(element), cancellationToken);

    /// <summary>Presses and releases a key in the focused element (a character or a key name such as <c>Enter</c>, <c>Tab</c>, <c>ArrowDown</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task PressAsync(string key, CancellationToken cancellationToken = default) => RunAsync(input => input.Keyboard.Press(key), cancellationToken);

    /// <summary>Presses a key while modifier keys are held (<c>PressAsync("a", "Control")</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys (<c>Shift</c>, <c>Control</c>, <c>Alt</c>, <c>Meta</c>).</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task PressAsync(string key, params string[] modifiers) => RunAsync(input => Press(input, key, modifiers), CancellationToken.None);

    /// <summary>
    /// Selects options of a <c>select</c> element by value or by label, and fires the <c>input</c> and <c>change</c>
    /// events. Waits until the element is visible and enabled, and until it has all the options.
    /// </summary>
    /// <param name="element">The <c>select</c> element.</param>
    /// <param name="values">The values or the labels of the options to select.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task SelectOptionAsync(Element element, params string[] values) => SelectOptionAsync(ActionTarget.From(element), values, CancellationToken.None);

    /// <summary>Checks a check box or a radio button with a click, when it is not checked.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a check box or a radio button, or the click does not check it.</exception>
    public Task CheckAsync(Element element, CancellationToken cancellationToken = default) => SetCheckedAsync(ActionTarget.From(element), isChecked: true, cancellationToken);

    /// <summary>Unchecks a check box with a click, when it is checked.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a check box, or the click does not uncheck it.</exception>
    public Task UncheckAsync(Element element, CancellationToken cancellationToken = default) => SetCheckedAsync(ActionTarget.From(element), isChecked: false, cancellationToken);

    internal async Task ClickAsync(ActionTarget target, ClickOptions? options, CancellationToken cancellationToken)
    {
        options ??= ClickOptions.Default;
        var (_, x, y) = await WaitForActionableAsync(target, Actionability.Click, options.Position, options.Force, options.Timeout, cancellationToken).ConfigureAwait(false);
        await ClickAsync(x, y, options, cancellationToken).ConfigureAwait(false);
    }

    internal async Task HoverAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var (_, x, y) = await WaitForActionableAsync(target, Actionability.Hover, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input => input.Mouse.Move(x, y), cancellationToken).ConfigureAwait(false);
    }

    internal async Task TapAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var (_, x, y) = await WaitForActionableAsync(target, Actionability.Click, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input =>
        {
            var touch = _touch ??= input.CreatePointer("touch");
            touch.Move(x, y);
            touch.Down();
            touch.Up();
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task DragAndDropAsync(ActionTarget source, ActionTarget target, CancellationToken cancellationToken)
    {
        var (_, sourceX, sourceY) = await WaitForActionableAsync(source, Actionability.Hover, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input =>
        {
            input.Mouse.Move(sourceX, sourceY);
            input.Mouse.Down();
        }, cancellationToken).ConfigureAwait(false);

        // The dragged element (or its drag image) can cover the target, so the target is not hit-tested
        var (_, targetX, targetY) = await WaitForActionableAsync(target, Actionability.Visible | Actionability.Stable | Actionability.Point, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input =>
        {
            const int Steps = 5;
            for (var i = 1; i <= Steps; i++)
            {
                input.Mouse.Move(sourceX + ((targetX - sourceX) * i / Steps), sourceY + ((targetY - sourceY) * i / Steps));
            }

            input.Mouse.Up();
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task TypeAsync(ActionTarget target, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        await FocusAsync(target, cancellationToken).ConfigureAwait(false);
        await TypeAsync(text, cancellationToken).ConfigureAwait(false);
    }

    internal async Task PressAsync(ActionTarget target, string key, string[] modifiers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await FocusAsync(target, cancellationToken).ConfigureAwait(false);
        await RunAsync(input => Press(input, key, modifiers), cancellationToken).ConfigureAwait(false);
    }

    internal async Task FocusAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var (element, _, _) = await WaitForActionableAsync(target, Actionability.None, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(_ => Focus(element), cancellationToken).ConfigureAwait(false);
    }

    internal async Task FillAsync(ActionTarget target, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (element, _, _) = await WaitForActionableAsync(target, Actionability.Fill, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        await RunAsync(input =>
        {
            Focus(element);
            switch (element)
            {
                case HtmlInputElement field:
                    field.Select();
                    break;

                case HtmlTextAreaElement field:
                    field.Select();
                    break;

                default:
                    if (element.OwnerDocument?.GetSelection() is { } selection)
                    {
                        selection.SelectAllChildren(element);
                    }

                    break;
            }

            if (text.Length == 0)
            {
                input.Keyboard.Press("Delete");
            }
            else
            {
                input.Keyboard.Type(text);
            }

            // Leave the field to commit the value (the change event)
            if (element is HtmlElement htmlElement)
            {
                htmlElement.Blur();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task SelectOptionAsync(ActionTarget target, string[] values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var (element, _, _) = await WaitForActionableAsync(target, Actionability.SelectOption, position: null, force: false, timeout: null, cancellationToken, values).ConfigureAwait(false);
        var select = (HtmlSelectElement)element;
        await RunAsync(_ =>
        {
            Focus(select);
            foreach (var option in select.QuerySelectorAll<HtmlOptionElement>("option"))
            {
                option.Selected = values.Any(value => IsOption(option, value));
            }

            select.DispatchEvent(new Event("input", new EventInit { Bubbles = true, Composed = true }));
            select.DispatchEvent(new Event("change", new EventInit { Bubbles = true }));
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task SetCheckedAsync(ActionTarget target, bool isChecked, CancellationToken cancellationToken)
    {
        var (element, _, _) = await WaitForActionableAsync(target, Actionability.None, position: null, force: false, timeout: null, cancellationToken).ConfigureAwait(false);
        var state = GetCheckedState(element);
        if (state == isChecked)
            return;

        if (!isChecked && ElementStates.RetargetToControl(element) is HtmlInputElement { Type: "radio" })
            throw new ElementNotInteractableException($"The radio button {ActionTarget.DescribeElement(element)} cannot be unchecked: check another radio button of its group");

        await ClickAsync(target, options: null, cancellationToken).ConfigureAwait(false);

        // The locator can find another element after the click: check the state of the clicked element
        if (GetCheckedState(element) != isChecked)
            throw new ElementNotInteractableException($"The click did not {(isChecked ? "check" : "uncheck")} the element {ActionTarget.DescribeElement(element)}");

        bool GetCheckedState(Element element)
        {
            using (_page.EnterEventLoop())
            {
                return ElementStates.GetChecked(element) switch
                {
                    AccessibilityCheckedState.True => true,
                    AccessibilityCheckedState.False or AccessibilityCheckedState.Mixed => false,
                    _ => throw new ElementNotInteractableException($"The element {ActionTarget.DescribeElement(element)} is not a check box or a radio button"),
                };
            }
        }
    }

    private static void Press(UserInput input, string key, string[] modifiers)
    {
        foreach (var modifier in modifiers)
        {
            input.Keyboard.Down(modifier);
        }

        input.Keyboard.Press(key);
        for (var i = modifiers.Length - 1; i >= 0; i--)
        {
            input.Keyboard.Up(modifiers[i]);
        }
    }

    private static void Focus(Element element)
    {
        switch (element)
        {
            case HtmlElement htmlElement:
                htmlElement.Focus();
                break;

            case SvgElement svgElement:
                svgElement.Focus();
                break;

            case MathMLElement mathElement:
                mathElement.Focus();
                break;
        }
    }

    private static bool IsOption(HtmlOptionElement option, string value) => string.Equals(option.Value, value, StringComparison.Ordinal) || string.Equals(TextMatcher.NormalizeWhiteSpace(option.Label), TextMatcher.NormalizeWhiteSpace(value), StringComparison.Ordinal);

    private async Task RunAsync(Action<UserInput> action, CancellationToken cancellationToken, bool waitAfter = true)
    {
        using (_page.EnterEventLoop())
        {
            action(Raw);
        }

        if (waitAfter)
        {
            await _page.WaitForIdleAsync(timeout: null, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _page.RunEventLoop();
        }
    }

    /// <summary>
    /// Runs the event loop until the target has an element that passes the checks, finding the element of a locator again
    /// at each attempt. Returns the element and the position to click.
    /// </summary>
    private async Task<(Element Element, double X, double Y)> WaitForActionableAsync(ActionTarget target, Actionability checks, ElementPosition? position, bool force, TimeSpan? timeout, CancellationToken cancellationToken, string[]? options = null)
    {
        Element? element = null;
        (double X, double Y) point = default;
        string? reason = null;
        ((double X, double Y, double Width, double Height) Box, CssNumberish? FrameTime, int StableFrames)? previousSample = null;
        try
        {
            await _page.WaitForAsync(() =>
            {
                element = target.Resolve(out reason);
                if (element is null)
                    return false;

                if (checks.HasFlag(Actionability.Editable) && !ElementStates.CanBeFilled(ElementStates.RetargetToControl(element)))
                    throw new ElementNotInteractableException($"The element {ActionTarget.DescribeElement(element)} is not an <input>, a <textarea> or a [contenteditable] element");

                if (options is not null && ElementStates.RetargetToControl(element) is not HtmlSelectElement)
                    throw new ElementNotInteractableException($"The element {ActionTarget.DescribeElement(element)} is not a <select> element");

                reason = GetNotActionableReason(element, checks, position, force, options, ref previousSample, out point);
                return reason is null;
            }, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            if (element is null)
                throw new ElementNotFoundException($"{target}: {reason}", ex);

            throw new ElementNotInteractableException($"The element {ActionTarget.DescribeElement(element)} cannot receive the input: {reason}", ex);
        }

        if (checks.HasFlag(Actionability.Editable) || options is not null)
        {
            element = ElementStates.RetargetToControl(element!);
        }

        return (element!, point.X, point.Y);
    }

    private string? GetNotActionableReason(Element element, Actionability checks, ElementPosition? position, bool force, string[]? options, ref ((double X, double Y, double Width, double Height) Box, CssNumberish? FrameTime, int StableFrames)? previousSample, out (double X, double Y) point)
    {
        point = default;
        if (element.OwnerDocument != _page.Document)
            return "it is not in the document of the page";

        if (!force)
        {
            if (checks.HasFlag(Actionability.Visible) && !ElementStates.IsVisible(element))
                return "it is not visible";

            if (checks.HasFlag(Actionability.Enabled) && ElementStates.IsDisabled(element))
                return "it is disabled";

            if (checks.HasFlag(Actionability.Editable) && ElementStates.IsEditable(element) is false)
                return "it is not editable";
        }

        if (options is not null)
        {
            var select = (HtmlSelectElement)ElementStates.RetargetToControl(element);
            var selectOptions = select.QuerySelectorAll<HtmlOptionElement>("option");
            foreach (var value in options)
            {
                if (!selectOptions.Any(option => IsOption(option, value)))
                    return $"it has no option whose value or label is \"{value}\"";
            }

            if (options.Length > 1 && !select.Multiple)
                throw new ElementNotInteractableException($"The element {ActionTarget.DescribeElement(element)} is not a <select multiple> element: it cannot select {options.Length} options");
        }

        if (!checks.HasFlag(Actionability.Point))
            return null;

        // GetAnimations applies the current frame of the animations to the styles, which the box of the element depends on
        var isAnimated = checks.HasFlag(Actionability.Stable) && !force && element.OwnerDocument is { } document && IsAnimated(element, document);
        var rect = element.GetBoundingClientRect();
        if (rect.Width <= 0 || rect.Height <= 0)
            return "it is not visible (its box is empty)";

        // Scroll the element into the viewport
        var viewport = _page.BrowsingContext.Window;
        if (rect.Bottom <= 0 || rect.Right <= 0 || rect.Top >= viewport.InnerHeight || rect.Left >= viewport.InnerWidth)
        {
            _ = element.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Center, Inline = ScrollLogicalPosition.Center });
            rect = element.GetBoundingClientRect();
        }

        point = position is { } offset ? (rect.X + offset.X, rect.Y + offset.Y) : (rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        if (force)
            return null;

        // As in Playwright, an animated element is stable once its box is the same in consecutive animation frames (the
        // animations advance once per frame of the event loop). An animation starts one frame after it is created, so the
        // box must be the same in three frames.
        if (isAnimated)
        {
            const int RequiredStableFrames = 2;
            var frameTime = element.OwnerDocument!.Timeline.CurrentTime;
            var box = (rect.X, rect.Y, rect.Width, rect.Height);
            if (previousSample is not { } previous || previous.Box != box)
            {
                previousSample = (box, frameTime, 0);
                return "it is not stable (it is animated)";
            }

            if (previous.FrameTime != frameTime)
            {
                previousSample = (box, frameTime, previous.StableFrames + 1);
            }

            if (previousSample.Value.StableFrames < RequiredStableFrames)
                return "it is not stable (it is animated)";
        }

        if (checks.HasFlag(Actionability.ReceivesEvents))
        {
            var hit = Raw.HitTest(point.X, point.Y);
            if (hit is null)
                return "it is outside of the viewport";

            if (!ComposedTree.IsInclusiveDescendant(hit, element))
                return $"{ActionTarget.DescribeElement(hit)} receives the input at ({point.X}, {point.Y})";
        }

        return null;
    }

    private static bool IsAnimated(Element element, Document document)
    {
        foreach (var animation in document.GetAnimations())
        {
            if (animation.PlayState == AnimationPlayState.Running && animation.Effect is KeyframeEffect { Target: { } target } && ComposedTree.IsInclusiveDescendant(element, target))
                return true;
        }

        return false;
    }

    [Flags]
    private enum Actionability
    {
        None = 0,
        Visible = 1,
        Stable = 2,
        Enabled = 4,
        ReceivesEvents = 8,
        Editable = 16,

        /// <summary>The action needs the position of the element (it is scrolled into the viewport).</summary>
        Point = 32,

        Pointer = Visible | Point | ReceivesEvents,
        Hover = Visible | Stable | Point | ReceivesEvents,
        Click = Hover | Enabled,
        Fill = Visible | Enabled | Editable,
        SelectOption = Visible | Enabled,
    }
}
