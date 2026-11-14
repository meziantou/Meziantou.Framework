using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// The assertions of a locator, as the web-first assertions of Playwright: each assertion runs the event loop of the page
/// until it passes, and throws an <see cref="ExpectationFailedException"/> when it still fails after the timeout. The
/// assertions on one element throw a <see cref="StrictModeViolationException"/> when the locator finds more than one.
/// </summary>
public sealed class LocatorAssertions
{
    private readonly Locator _locator;
    private readonly bool _isNot;

    internal LocatorAssertions(Locator locator, bool isNot)
    {
        _locator = locator;
        _isNot = isNot;
    }

    /// <summary>Gets the negated assertions (<c>Expect(locator).Not.ToBeVisibleAsync()</c>).</summary>
    public LocatorAssertions Not => new(_locator, !_isNot);

    /// <summary>Waits until the locator finds an element.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeAttachedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertAsync("to be attached", element => (element is not null, element is null ? "no element matches" : "an element matches"), timeout, cancellationToken);

    /// <summary>Waits until the locator finds a visible element: a non-empty box, and not <c>visibility: hidden</c>.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeVisibleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertAsync("to be visible", element => element is null ? (false, "no element matches") : ElementStates.IsVisible(element) ? (true, "it is visible") : (false, "it is hidden"), timeout, cancellationToken);

    /// <summary>Waits until the locator finds no element, or a hidden element.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeHiddenAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertAsync("to be hidden", element => element is null ? (true, "no element matches") : ElementStates.IsVisible(element) ? (false, "it is visible") : (true, "it is hidden"), timeout, cancellationToken);

    /// <summary>Waits until the element is enabled.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeEnabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertElementAsync("to be enabled", element => ElementStates.IsDisabled(element) ? (false, "it is disabled") : (true, "it is enabled"), timeout, cancellationToken);

    /// <summary>Waits until the element is disabled: a disabled form control, or <c>aria-disabled="true"</c>.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeDisabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertElementAsync("to be disabled", element => ElementStates.IsDisabled(element) ? (true, "it is disabled") : (false, "it is enabled"), timeout, cancellationToken);

    /// <summary>Waits until the check box, the radio button or the element with a checked state is checked.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeCheckedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertElementAsync("to be checked", element => ElementStates.GetChecked(element) switch
        {
            AccessibilityCheckedState.True => (true, "it is checked"),
            AccessibilityCheckedState.Mixed => (false, "it is in the mixed state"),
            AccessibilityCheckedState.False => (false, "it is not checked"),
            _ => throw new ExpectationFailedException($"The element {ActionTarget.DescribeElement(element)} of {_locator} is not a check box or a radio button"),
        }, timeout, cancellationToken);

    /// <summary>Waits until the field is editable: enabled and not read-only.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeEditableAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertElementAsync("to be editable", element => ElementStates.IsEditable(element) switch
        {
            true => (true, "it is editable"),
            false => (false, "it is read-only or disabled"),
            null => throw new ExpectationFailedException($"The element {ActionTarget.DescribeElement(element)} of {_locator} is not an <input>, a <textarea>, a <select> or a [contenteditable] element, and its role does not allow aria-readonly"),
        }, timeout, cancellationToken);

    /// <summary>Waits until the element has the focus.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeFocusedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertElementAsync("to be focused", element => ElementStates.IsFocused(element) ? (true, "it has the focus") : (false, "it does not have the focus"), timeout, cancellationToken);

    /// <summary>Waits until the element has no text, or, for a field, no value.</summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToBeEmptyAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => AssertElementAsync("to be empty", element => ElementStates.IsEmpty(element) ? (true, "it is empty") : (false, "it is not empty"), timeout, cancellationToken);

    /// <summary>Waits until the text of the element is a text, once the white space of both texts is collapsed.</summary>
    /// <param name="expected">The text.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var normalizedExpected = TextMatcher.NormalizeWhiteSpace(expected);
        return AssertElementAsync("to have the text " + TextMatcher.Quote(normalizedExpected), element => CompareText(GetText(element), text => string.Equals(text, normalizedExpected, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the text of the element, with collapsed white space, matches a regular expression.</summary>
    /// <param name="expected">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveTextAsync(Regex expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return AssertElementAsync("to have a text that matches /" + expected + "/", element => CompareText(GetText(element), expected.IsMatch), timeout, cancellationToken);
    }

    /// <summary>Waits until the text of the element contains a text, once the white space of both texts is collapsed.</summary>
    /// <param name="expected">The text.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToContainTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var normalizedExpected = TextMatcher.NormalizeWhiteSpace(expected);
        return AssertElementAsync("to contain the text " + TextMatcher.Quote(normalizedExpected), element => CompareText(GetText(element), text => text.Contains(normalizedExpected, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the value of the field (<c>input</c>, <c>textarea</c> or <c>select</c>) is a value.</summary>
    /// <param name="expected">The value.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveValueAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return AssertElementAsync("to have the value " + TextMatcher.Quote(expected), element => CompareText(GetValue(element), value => string.Equals(value, expected, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the value of the field matches a regular expression.</summary>
    /// <param name="expected">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveValueAsync(Regex expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return AssertElementAsync("to have a value that matches /" + expected + "/", element => CompareText(GetValue(element), expected.IsMatch), timeout, cancellationToken);
    }

    /// <summary>Waits until the element has an attribute.</summary>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveAttributeAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return AssertElementAsync("to have the attribute " + name, element => element.HasAttribute(name) ? (true, "it has the attribute") : (false, "it does not have the attribute"), timeout, cancellationToken);
    }

    /// <summary>Waits until an attribute of the element has a value.</summary>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="value">The value.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveAttributeAsync(string name, string value, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return AssertElementAsync("to have the attribute " + name + "=" + TextMatcher.Quote(value), element => CompareText(element.GetAttribute(name), actual => string.Equals(actual, value, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the value of an attribute of the element matches a regular expression.</summary>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="value">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveAttributeAsync(string name, Regex value, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return AssertElementAsync("to have an attribute " + name + " that matches /" + value + "/", element => CompareText(element.GetAttribute(name), value.IsMatch), timeout, cancellationToken);
    }

    /// <summary>Waits until the <c>class</c> attribute of the element is a list of classes, once the white space of both lists is collapsed.</summary>
    /// <param name="expected">The classes, separated by spaces.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveClassAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var normalizedExpected = TextMatcher.NormalizeWhiteSpace(expected);
        return AssertElementAsync("to have the classes " + TextMatcher.Quote(normalizedExpected), element => CompareText(TextMatcher.NormalizeWhiteSpace(element.GetAttribute("class") ?? ""), actual => string.Equals(actual, normalizedExpected, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the <c>class</c> attribute of the element matches a regular expression (<c>new Regex(@"\bactive\b")</c>).</summary>
    /// <param name="expected">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveClassAsync(Regex expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return AssertElementAsync("to have classes that match /" + expected + "/", element => CompareText(element.GetAttribute("class") ?? "", expected.IsMatch), timeout, cancellationToken);
    }

    /// <summary>Waits until a computed style property of the element has a value (<c>ToHaveCssAsync("display", "none")</c>).</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The computed value.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveCssAsync(string name, string value, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return AssertElementAsync("to have the style " + name + ": " + value, element => CompareText(_locator.Page.BrowsingContext.Window.GetComputedStyle(element).GetPropertyValue(name), actual => string.Equals(actual, value, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the computed ARIA role of the element is a role.</summary>
    /// <param name="role">The role.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveRoleAsync(string role, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        return AssertElementAsync("to have the role " + role, element => CompareText(element.ComputedRole, actual => string.Equals(actual, role, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the accessible name of the element is a text, once the white space of both texts is collapsed.</summary>
    /// <param name="name">The accessible name.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveAccessibleNameAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalizedName = TextMatcher.NormalizeWhiteSpace(name);
        return AssertElementAsync("to have the accessible name " + TextMatcher.Quote(normalizedName), element => CompareText(TextMatcher.NormalizeWhiteSpace(element.ComputedName), actual => string.Equals(actual, normalizedName, StringComparison.Ordinal)), timeout, cancellationToken);
    }

    /// <summary>Waits until the accessible name of the element matches a regular expression.</summary>
    /// <param name="name">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveAccessibleNameAsync(Regex name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return AssertElementAsync("to have an accessible name that matches /" + name + "/", element => CompareText(element.ComputedName, name.IsMatch), timeout, cancellationToken);
    }

    /// <summary>Waits until the accessibility tree of the element is a snapshot (see <see cref="Locator.GetAccessibilitySnapshot"/>), ignoring the line endings and the trailing white space.</summary>
    /// <param name="expected">The snapshot, in the YAML format of the ARIA snapshots of Playwright.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveAccessibilitySnapshotAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var normalizedExpected = NormalizeSnapshot(expected);
        return AssertElementAsync("to have the accessibility snapshot\n" + normalizedExpected + "\n", element =>
        {
            var actual = NormalizeSnapshot(DomSnapshot.GetAccessibilitySnapshot([element]));
            return string.Equals(actual, normalizedExpected, StringComparison.Ordinal) ? (true, "it has it") : (false, "it is\n" + actual);
        }, timeout, cancellationToken);

        static string NormalizeSnapshot(string value) => string.Join('\n', value.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd())).Trim('\n');
    }

    /// <summary>Waits until the locator finds a number of elements.</summary>
    /// <param name="count">The number of elements.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveCountAsync(int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return WaitAsync("to find " + TextMatcher.Format(count) + " elements", () =>
        {
            var actual = _locator.Evaluate().Count;
            return (actual == count, "it finds " + TextMatcher.Format(actual));
        }, timeout, cancellationToken);
    }

    private static string GetText(Element element) => TextMatcher.NormalizeWhiteSpace(element.TextContent ?? "");

    private string GetValue(Element element) => ElementStates.GetInputValue(element) ?? throw new ExpectationFailedException($"The element {ActionTarget.DescribeElement(element)} of {_locator} is not an <input>, a <textarea> or a <select> element");

    private static (bool Matches, string Actual) CompareText(string? actual, Func<string, bool> predicate)
        => actual is not null && predicate(actual) ? (true, "it is " + TextMatcher.Quote(actual)) : (false, actual is null ? "it is missing" : "it is " + TextMatcher.Quote(actual));

    /// <summary>Asserts a condition of the element; the assertion and its negation both fail while the locator finds no element.</summary>
    private Task AssertElementAsync(string expectation, Func<Element, (bool Matches, string Actual)> condition, TimeSpan? timeout, CancellationToken cancellationToken)
        => WaitAsync(expectation, () =>
        {
            var element = _locator.ResolveSingle();
            if (element is null)
                return (_isNot, "no element matches");

            return condition(element);
        }, timeout, cancellationToken);

    /// <summary>Asserts a condition of the element, or of the absence of element.</summary>
    private Task AssertAsync(string expectation, Func<Element?, (bool Matches, string Actual)> condition, TimeSpan? timeout, CancellationToken cancellationToken)
        => WaitAsync(expectation, () => condition(_locator.ResolveSingle()), timeout, cancellationToken);

    private async Task WaitAsync(string expectation, Func<(bool Matches, string Actual)> condition, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var actual = "";
        try
        {
            await _locator.Page.WaitForAsync(() =>
            {
                (var matches, actual) = condition();
                return matches != _isNot;
            }, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new ExpectationFailedException($"Expected {_locator} {(_isNot ? "not " : "")}{expectation}, but {actual} (waited {timeout ?? _locator.Page.DefaultTimeout})", ex);
        }
    }
}
