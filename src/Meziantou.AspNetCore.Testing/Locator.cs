using System.Collections.Immutable;
using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// Finds elements of a page, as the locators of Playwright: a locator is a query, evaluated again each time it is used,
/// so it finds the new elements when the page renders again. The actions wait until the locator finds exactly one element
/// and the element is actionable; the assertions of <see cref="Expectations.Expect(Locator)"/> wait until they pass. A
/// locator that finds more than one element where one is needed throws a <see cref="StrictModeViolationException"/>.
/// </summary>
public sealed class Locator : LocatorScope
{
    private readonly Func<IReadOnlyList<Node>> _roots;

    internal Locator(TestPage page, Func<IReadOnlyList<Node>> roots, ImmutableArray<LocatorStep> steps)
    {
        Page = page;
        _roots = roots;
        Steps = steps;
    }

    /// <summary>Gets the page of the locator.</summary>
    public TestPage Page { get; }

    internal ImmutableArray<LocatorStep> Steps { get; }

    private protected override string TestIdAttributeCore => Page.TestIdAttribute;

    /// <summary>Gets a locator of the first element of this locator.</summary>
    public Locator First => CreateLocator(new NthStep(0));

    /// <summary>Gets a locator of the last element of this locator.</summary>
    public Locator Last => CreateLocator(new NthStep(-1));

    /// <summary>Gets the number of elements that the locator finds now.</summary>
    public int Count => Evaluate().Count;

    /// <summary>Gets the text content of the element.</summary>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public string TextContent => GetElement().TextContent ?? "";

    /// <summary>Gets the value of the field (<c>input</c>, <c>textarea</c> or <c>select</c>, or the control of a <c>label</c>).</summary>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    /// <exception cref="InvalidOperationException">The element is not a field.</exception>
    public string InputValue => ElementStates.GetInputValue(GetElement()) ?? throw new InvalidOperationException($"The element of {this} is not an <input>, a <textarea> or a <select> element");

    /// <summary>Gets whether the locator finds a visible element now: a non-empty box, and not <c>visibility: hidden</c>. False when it finds no element.</summary>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public bool IsVisible
    {
        get
        {
            using (Page.EnterEventLoop())
            {
                return ResolveSingle() is { } element && ElementStates.IsVisible(element);
            }
        }
    }

    /// <summary>Gets whether the element is enabled: not a disabled form control, and no <c>aria-disabled="true"</c>.</summary>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public bool IsEnabled => !ElementStates.IsDisabled(GetElement());

    /// <summary>Gets whether the check box, the radio button, or the element with a checked state is checked.</summary>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    /// <exception cref="InvalidOperationException">The element has no checked state.</exception>
    public bool IsChecked => ElementStates.GetChecked(GetElement()) switch
    {
        AccessibilityCheckedState.True => true,
        AccessibilityCheckedState.False or AccessibilityCheckedState.Mixed => false,
        _ => throw new InvalidOperationException($"The element of {this} is not a check box or a radio button"),
    };

    /// <summary>Gets whether the field is editable: enabled and not read-only.</summary>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    /// <exception cref="InvalidOperationException">The element is not a field.</exception>
    public bool IsEditable => ElementStates.IsEditable(GetElement()) ?? throw new InvalidOperationException($"The element of {this} is not an <input>, a <textarea>, a <select> or a [contenteditable] element, and its role does not allow aria-readonly");

    /// <summary>Gets a locator of the element at an index of the elements of this locator.</summary>
    /// <param name="index">The index, starting at 0. A negative index counts from the end (-1 is the last element).</param>
    /// <returns>The locator.</returns>
    public Locator Nth(int index) => CreateLocator(new NthStep(index));

    /// <summary>Gets a locator of the elements of this locator that satisfy conditions.</summary>
    /// <param name="options">The conditions.</param>
    /// <returns>The locator.</returns>
    public Locator Filter(LocatorFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfOtherPage(options.Has);
        ThrowIfOtherPage(options.HasNot);
        return CreateLocator(new FilterStep(options));
    }

    /// <summary>Gets a locator of the elements that this locator and another locator both find (<c>page.GetByRole("button").And(page.GetByTitle("Subscribe"))</c>).</summary>
    /// <param name="locator">The other locator, evaluated from the same scope as this locator.</param>
    /// <returns>The locator.</returns>
    public Locator And(Locator locator)
    {
        ThrowIfOtherPage(locator);
        return CreateLocator(new CombineStep(locator, union: false));
    }

    /// <summary>Gets a locator of the elements that this locator or another locator finds, in document order (<c>page.GetByRole("button", "New").Or(page.GetByText("Confirm"))</c>).</summary>
    /// <param name="locator">The other locator, evaluated from the same scope as this locator.</param>
    /// <returns>The locator.</returns>
    public Locator Or(Locator locator)
    {
        ThrowIfOtherPage(locator);
        return CreateLocator(new CombineStep(locator, union: true));
    }

    /// <summary>Gets the element that the locator finds now.</summary>
    /// <returns>The element.</returns>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public Element GetElement() => ResolveSingle() ?? throw new ElementNotFoundException($"No element matches the locator {this}");

    /// <summary>Gets the element that the locator finds now, or null.</summary>
    /// <returns>The element, or null.</returns>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public Element? GetElementOrDefault() => ResolveSingle();

    /// <summary>Gets the elements that the locator finds now, in document order.</summary>
    /// <returns>The elements.</returns>
    public IReadOnlyList<Element> GetElements() => Evaluate();

    /// <summary>Gets a locator for each element that the locator finds now (<c>Nth(0)</c>, <c>Nth(1)</c>...).</summary>
    /// <returns>The locators.</returns>
    public IReadOnlyList<Locator> GetAll() => [.. Enumerable.Range(0, Count).Select(Nth)];

    /// <summary>Gets the value of an attribute of the element, or null when it has no such attribute.</summary>
    /// <param name="name">The name of the attribute.</param>
    /// <returns>The value, or null.</returns>
    /// <exception cref="ElementNotFoundException">The locator finds no element.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public string? GetAttribute(string name) => GetElement().GetAttribute(name);

    /// <summary>Gets the HTML of the element, formatted for snapshots.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The HTML.</returns>
    public string GetMarkup(MarkupOptions? options = null) => DomSnapshot.GetMarkup([GetElement()], options);

    /// <summary>Gets the accessibility tree of the element, as a YAML snapshot (the format of the ARIA snapshots of Playwright).</summary>
    /// <returns>The snapshot.</returns>
    public string GetAccessibilitySnapshot() => DomSnapshot.GetAccessibilitySnapshot([GetElement()]);

    /// <summary>Takes a screenshot of the element, encoded as PNG.</summary>
    /// <returns>The PNG image.</returns>
    public byte[] TakeScreenshotAsPng()
    {
        using (Page.EnterEventLoop())
        {
            return ImagingScreenshotExtensions.TakeScreenshotAsPng(GetElement());
        }
    }

    /// <summary>Runs the event loop of the page until the locator reaches a state.</summary>
    /// <param name="state">The state. The default is <see cref="LocatorState.Visible"/>.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the locator reaches the state.</returns>
    /// <exception cref="TimeoutException">The locator does not reach the state after <paramref name="timeout"/>.</exception>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public async Task WaitForAsync(LocatorState state = LocatorState.Visible, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await Page.WaitForAsync(() =>
            {
                var element = ResolveSingle();
                return state switch
                {
                    LocatorState.Attached => element is not null,
                    LocatorState.Detached => element is null,
                    LocatorState.Visible => element is not null && ElementStates.IsVisible(element),
                    LocatorState.Hidden => element is null || !ElementStates.IsVisible(element),
                    _ => throw new ArgumentOutOfRangeException(nameof(state)),
                };
            }, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"The locator {this} is not {state.ToString().ToLowerInvariant()} after {timeout ?? Page.DefaultTimeout}", ex);
        }
    }

    /// <summary>
    /// Clicks the element at its center (or at <see cref="ClickOptions.Position"/>), once the locator finds it and it is
    /// visible, stable, enabled and not covered by another element.
    /// </summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task ClickAsync(ClickOptions? options = null, CancellationToken cancellationToken = default) => Page.Input.ClickAsync(ActionTarget.From(this), options, cancellationToken);

    /// <summary>Double-clicks the element.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task DoubleClickAsync(CancellationToken cancellationToken = default) => ClickAsync(new ClickOptions { ClickCount = 2 }, cancellationToken);

    /// <summary>Moves the mouse over the element, once it is visible, stable and not covered by another element.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task HoverAsync(CancellationToken cancellationToken = default) => Page.Input.HoverAsync(ActionTarget.From(this), cancellationToken);

    /// <summary>Taps the element with a finger.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task TapAsync(CancellationToken cancellationToken = default) => Page.Input.TapAsync(ActionTarget.From(this), cancellationToken);

    /// <summary>Focuses the element.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task FocusAsync(CancellationToken cancellationToken = default) => Page.Input.FocusAsync(ActionTarget.From(this), cancellationToken);

    /// <summary>
    /// Replaces the value of the text field with a text typed with the keyboard, then leaves the field (the <c>change</c>
    /// event, so <c>@bind</c> updates the value). Waits until the field is visible, enabled and editable. An element of a
    /// <c>label</c> fills the control of the label.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a text field.</exception>
    public Task FillAsync(string text, CancellationToken cancellationToken = default) => Page.Input.FillAsync(ActionTarget.From(this), text, cancellationToken);

    /// <summary>Clears the value of the text field, then leaves the field.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default) => FillAsync("", cancellationToken);

    /// <summary>Focuses the element, and types a text with the keyboard, key by key (the text is appended to the value of a field).</summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task TypeAsync(string text, CancellationToken cancellationToken = default) => Page.Input.TypeAsync(ActionTarget.From(this), text, cancellationToken);

    /// <summary>Focuses the element, and presses a key (a character or a key name such as <c>Enter</c>, <c>Tab</c>, <c>ArrowDown</c>) while modifier keys are held.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys (<c>Shift</c>, <c>Control</c>, <c>Alt</c>, <c>Meta</c>).</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task PressAsync(string key, params string[] modifiers) => Page.Input.PressAsync(ActionTarget.From(this), key, modifiers, CancellationToken.None);

    /// <summary>
    /// Selects options of the <c>select</c> element by value or by label, and fires the <c>input</c> and <c>change</c>
    /// events. Waits until the element is visible and enabled, and until it has all the options.
    /// </summary>
    /// <param name="values">The values or the labels of the options to select.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task SelectOptionAsync(params string[] values) => Page.Input.SelectOptionAsync(ActionTarget.From(this), values, CancellationToken.None);

    /// <summary>Checks the check box or the radio button with a click, when it is not checked. An element of a <c>label</c> checks the control of the label.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a check box or a radio button, or the click does not check it.</exception>
    public Task CheckAsync(CancellationToken cancellationToken = default) => SetCheckedAsync(isChecked: true, cancellationToken);

    /// <summary>Unchecks the check box with a click, when it is checked.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a check box, or the click does not uncheck it.</exception>
    public Task UncheckAsync(CancellationToken cancellationToken = default) => SetCheckedAsync(isChecked: false, cancellationToken);

    /// <summary>Checks or unchecks the check box with a click, when its state is not <paramref name="isChecked"/>.</summary>
    /// <param name="isChecked">The state.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="ElementNotInteractableException">The element is not a check box or a radio button, or the click does not change its state.</exception>
    public Task SetCheckedAsync(bool isChecked, CancellationToken cancellationToken = default) => Page.Input.SetCheckedAsync(ActionTarget.From(this), isChecked, cancellationToken);

    /// <summary>Drags the element with the mouse, and drops it on the element of another locator (the HTML drag and drop events).</summary>
    /// <param name="target">The locator of the element where the element is dropped.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    public Task DragToAsync(Locator target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Page.Input.DragAndDropAsync(ActionTarget.From(this), ActionTarget.From(target), cancellationToken);
    }

    /// <summary>Gets the description of the locator, as C# code (<c>GetByRole("listitem").Filter(HasText = "Ada").First</c>).</summary>
    /// <returns>The description.</returns>
    public override string ToString() => string.Join('.', Steps);

    private protected override Locator CreateLocator(LocatorStep step) => new(Page, _roots, Steps.Add(step));

    internal static Locator Create(TestPage page, Func<IReadOnlyList<Node>> roots, LocatorStep step) => new(page, roots, [step]);

    /// <summary>Evaluates the locator now.</summary>
    internal List<Element> Evaluate() => LocatorEvaluator.Evaluate(Page, Steps, new SearchScope(_roots(), IncludeNodes: true));

    /// <summary>Evaluates the locator now, and returns its element, or null.</summary>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    internal Element? ResolveSingle()
    {
        var elements = Evaluate();
        return elements.Count switch
        {
            0 => null,
            1 => elements[0],
            _ => throw CreateStrictModeViolation(elements),
        };
    }

    private StrictModeViolationException CreateStrictModeViolation(List<Element> elements)
    {
        const int MaxElements = 10;
        var description = string.Join(", ", elements.Take(MaxElements).Select(ActionTarget.DescribeElement));
        if (elements.Count > MaxElements)
        {
            description += ", ...";
        }

        return new StrictModeViolationException($"Strict mode violation: {this} finds {elements.Count} elements: {description}");
    }

    private void ThrowIfOtherPage(Locator? locator)
    {
        if (locator is not null && locator.Page != Page)
            throw new ArgumentException("The locator belongs to another page", nameof(locator));
    }
}
