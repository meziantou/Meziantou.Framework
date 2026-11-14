using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>The user actions on the elements of a <see cref="TestPage"/> (see <see cref="PageInput"/>).</summary>
public static class ElementExtensions
{
    /// <summary>Clicks the element at its center.</summary>
    /// <param name="element">The element.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task ClickAsync(this Element element, ClickOptions? options = null, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.ClickAsync(element, options, cancellationToken);

    /// <summary>Double-clicks the element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task DoubleClickAsync(this Element element, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.DoubleClickAsync(element, cancellationToken);

    /// <summary>Moves the mouse over the element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task HoverAsync(this Element element, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.HoverAsync(element, cancellationToken);

    /// <summary>Taps the element with a finger.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task TapAsync(this Element element, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.TapAsync(element, cancellationToken);

    /// <summary>Focuses the element with a click and types a text with the keyboard.</summary>
    /// <param name="element">The element.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task TypeAsync(this Element element, string text, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.TypeAsync(element, text, cancellationToken);

    /// <summary>Replaces the value of the text field with a text typed with the keyboard, then leaves the field.</summary>
    /// <param name="element">The text field.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task FillAsync(this Element element, string text, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.FillAsync(element, text, cancellationToken);

    /// <summary>Selects options of the <c>select</c> element by value.</summary>
    /// <param name="element">The <c>select</c> element.</param>
    /// <param name="values">The values of the options to select.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task SelectOptionAsync(this Element element, params string[] values)
        => TestPage.GetPage(element).Input.SelectOptionAsync(element, values);

    /// <summary>Checks the check box or the radio button with a click.</summary>
    /// <param name="element">The input element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task CheckAsync(this Element element, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.CheckAsync(element, cancellationToken);

    /// <summary>Unchecks the check box with a click.</summary>
    /// <param name="element">The input element.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public static Task UncheckAsync(this Element element, CancellationToken cancellationToken = default)
        => TestPage.GetPage(element).Input.UncheckAsync(element, cancellationToken);

    /// <summary>Gets the HTML of the element, formatted for snapshots.</summary>
    /// <param name="element">The element.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The HTML.</returns>
    public static string GetMarkup(this Element element, MarkupOptions? options = null) => DomSnapshot.GetMarkup([element], options);

    /// <summary>Gets the accessibility tree of the element, as a YAML snapshot (the format of the ARIA snapshots of Playwright).</summary>
    /// <param name="element">The element.</param>
    /// <returns>The snapshot.</returns>
    public static string GetAccessibilitySnapshot(this Element element) => DomSnapshot.GetAccessibilitySnapshot([element]);
}
