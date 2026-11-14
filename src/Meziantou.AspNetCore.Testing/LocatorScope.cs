using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Testing.Internals;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// Creates the locators of the elements of a page, of a component or of the elements of another locator, as Playwright
/// does. A locator finds its elements again each time it is used, so it keeps working when the page renders new elements.
/// The texts are matched as substrings, ignoring the case and collapsing the white space, unless they are exact.
/// The elements of the open shadow roots are found too.
/// </summary>
public abstract class LocatorScope
{
    private protected LocatorScope()
    {
    }

    /// <summary>Gets the name of the attribute that <see cref="GetByTestId(string)"/> matches.</summary>
    private protected abstract string TestIdAttributeCore { get; }

    /// <summary>Creates a locator that applies a step to the elements of this scope.</summary>
    private protected abstract Locator CreateLocator(LocatorStep step);

    /// <summary>Creates a locator of the elements that match a CSS selector.</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The locator.</returns>
    public Locator Locator(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return CreateLocator(new CssStep(selector));
    }

    /// <summary>
    /// Creates a locator of the elements that have an ARIA role and, optionally, an accessible name
    /// (<c>GetByRole("button", "Save")</c>). The elements hidden from the accessibility tree are not found, unless
    /// <see cref="GetByRoleOptions.IncludeHidden"/> is true.
    /// </summary>
    /// <param name="role">The computed ARIA role (<c>button</c>, <c>link</c>, <c>textbox</c>, <c>heading</c>...).</param>
    /// <param name="name">A substring of the accessible name, ignoring the case (see <see cref="GetByRoleOptions.Exact"/>), or null to match any name.</param>
    /// <param name="options">The states of the elements, or null to match any state.</param>
    /// <returns>The locator.</returns>
    public Locator GetByRole(string role, string? name = null, GetByRoleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        options ??= new GetByRoleOptions();
        return CreateLocator(new RoleStep(role, name is null ? null : TextMatcher.Create(name, options.Exact), options));
    }

    /// <summary>Creates a locator of the elements that have an ARIA role and an accessible name that matches a regular expression.</summary>
    /// <param name="role">The computed ARIA role.</param>
    /// <param name="name">The regular expression.</param>
    /// <param name="options">The states of the elements, or null to match any state.</param>
    /// <returns>The locator.</returns>
    public Locator GetByRole(string role, Regex name, GetByRoleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        return CreateLocator(new RoleStep(role, TextMatcher.Create(name), options ?? new GetByRoleOptions()));
    }

    /// <summary>
    /// Creates a locator of the smallest elements that contain a text: an element is found when its text matches, and the
    /// text of none of its children matches. The text of an input button is its value.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="exact">Whether the text matches the whole text, with the case. By default, it matches a substring, ignoring the case.</param>
    /// <returns>The locator.</returns>
    public Locator GetByText(string text, bool exact = false) => CreateLocator(new TextStep(TextMatcher.Create(text, exact)));

    /// <summary>Creates a locator of the smallest elements whose text matches a regular expression.</summary>
    /// <param name="text">The regular expression.</param>
    /// <returns>The locator.</returns>
    public Locator GetByText(Regex text) => CreateLocator(new TextStep(TextMatcher.Create(text)));

    /// <summary>
    /// Creates a locator of the elements labelled by a text: the elements referenced by <c>aria-labelledby</c>, else the
    /// <c>aria-label</c> attribute, else the <c>label</c> elements of a form control.
    /// </summary>
    /// <param name="text">The text of the label.</param>
    /// <param name="exact">Whether the text matches the whole label, with the case. By default, it matches a substring, ignoring the case.</param>
    /// <returns>The locator.</returns>
    public Locator GetByLabel(string text, bool exact = false) => CreateLocator(new LabelStep(TextMatcher.Create(text, exact)));

    /// <summary>Creates a locator of the elements whose label matches a regular expression.</summary>
    /// <param name="text">The regular expression.</param>
    /// <returns>The locator.</returns>
    public Locator GetByLabel(Regex text) => CreateLocator(new LabelStep(TextMatcher.Create(text)));

    /// <summary>Creates a locator of the elements whose <c>placeholder</c> attribute contains a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="exact">Whether the text matches the whole attribute, with the case. By default, it matches a substring, ignoring the case.</param>
    /// <returns>The locator.</returns>
    public Locator GetByPlaceholder(string text, bool exact = false) => CreateLocator(new AttributeStep(nameof(GetByPlaceholder), "placeholder", TextMatcher.Create(text, exact)));

    /// <summary>Creates a locator of the elements whose <c>placeholder</c> attribute matches a regular expression.</summary>
    /// <param name="text">The regular expression.</param>
    /// <returns>The locator.</returns>
    public Locator GetByPlaceholder(Regex text) => CreateLocator(new AttributeStep(nameof(GetByPlaceholder), "placeholder", TextMatcher.Create(text)));

    /// <summary>Creates a locator of the elements (usually images) whose <c>alt</c> attribute contains a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="exact">Whether the text matches the whole attribute, with the case. By default, it matches a substring, ignoring the case.</param>
    /// <returns>The locator.</returns>
    public Locator GetByAltText(string text, bool exact = false) => CreateLocator(new AttributeStep(nameof(GetByAltText), "alt", TextMatcher.Create(text, exact)));

    /// <summary>Creates a locator of the elements whose <c>alt</c> attribute matches a regular expression.</summary>
    /// <param name="text">The regular expression.</param>
    /// <returns>The locator.</returns>
    public Locator GetByAltText(Regex text) => CreateLocator(new AttributeStep(nameof(GetByAltText), "alt", TextMatcher.Create(text)));

    /// <summary>Creates a locator of the elements whose <c>title</c> attribute contains a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="exact">Whether the text matches the whole attribute, with the case. By default, it matches a substring, ignoring the case.</param>
    /// <returns>The locator.</returns>
    public Locator GetByTitle(string text, bool exact = false) => CreateLocator(new AttributeStep(nameof(GetByTitle), "title", TextMatcher.Create(text, exact)));

    /// <summary>Creates a locator of the elements whose <c>title</c> attribute matches a regular expression.</summary>
    /// <param name="text">The regular expression.</param>
    /// <returns>The locator.</returns>
    public Locator GetByTitle(Regex text) => CreateLocator(new AttributeStep(nameof(GetByTitle), "title", TextMatcher.Create(text)));

    /// <summary>
    /// Creates a locator of the elements whose test identifier is a value: the <c>data-testid</c> attribute by default (see
    /// <see cref="TestPageSettings.TestIdAttribute"/>). The value is compared as is.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <returns>The locator.</returns>
    public Locator GetByTestId(string testId) => CreateLocator(new AttributeStep(nameof(GetByTestId), TestIdAttributeCore, TextMatcher.CreateVerbatim(testId)));

    /// <summary>Creates a locator of the elements whose test identifier matches a regular expression.</summary>
    /// <param name="testId">The regular expression.</param>
    /// <returns>The locator.</returns>
    public Locator GetByTestId(Regex testId) => CreateLocator(new AttributeStep(nameof(GetByTestId), TestIdAttributeCore, TextMatcher.Create(testId)));
}
