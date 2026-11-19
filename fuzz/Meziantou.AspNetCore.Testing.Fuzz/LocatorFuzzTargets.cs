using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Fuzz;

/// <summary>
/// The input is a query on its first line (<c>kind[flags]:argument[:name]</c>) and the HTML of the body of a page on the
/// next lines. The locator of the query must find distinct elements in document order, agree with its other code paths
/// (<c>Count</c>, <c>Nth</c>, <c>And</c>, <c>Or</c>, <c>Filter</c>), match <c>QuerySelectorAll</c> for a CSS selector,
/// and its elements must satisfy the query (the role, the attribute, the smallest elements for a text).
/// </summary>
public static class LocatorFuzzTargets
{
    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("aspnetcore-testing-locators", Locators)
        {
            Seeds = GetSeeds,
            Dictionary =
            [
                "c:", ":scope", "r:", "rh:", "re:", "t:", "te:", "l:", "le:", "p:", "a:", "T:", "i:", "\n",
                "button", "link", "heading", "listitem", "checkbox", "textbox", "img",
                "<button>", "</button>", "<a href=\"#\">", "<h1>", "<ul><li>", "<label for=\"x\">", "<label>", "</label>", "<input id=\"x\">", "<input type=\"submit\" value=\"Go\">",
                "<select>", "</select>", "<option>", "<option selected>", "<textarea>", "</textarea>", "<button type=\"button\">", "<input type=\"number\" value=\"3\">", "<input type=\"checkbox\">", "aria-valuetext=\"", "aria-valuenow=\"",
                "<img alt=\"", "placeholder=\"", "title=\"", "data-testid=\"", "aria-label=\"", "aria-labelledby=\"", "aria-hidden=\"true\"", "role=\"", "style=\"display: none\"", "<script>", "<style>", " id=\"",
            ],
        },
        new("aspnetcore-testing-fill-values", FillValues)
        {
            Seeds = GetFillValueSeeds,
            Dictionary = ["-", "+", ".", "e", "E", "e+", "e-", "0", "9", " ", "Infinity", "NaN", "1e999", "2026", "-07", "-01", "T", ":", ":00", "W", "#", "ff", "FF"],
        },
    ];

    private static IEnumerable<FuzzSeed> GetFillValueSeeds()
    {
        yield return FuzzSeed.FromText("range", [0], "7");
        yield return FuzzSeed.FromText("range-exponent", [0], "2.5e0");
        yield return FuzzSeed.FromText("range-plus", [0], "+7");
        yield return FuzzSeed.FromText("range-infinite", [0], "1e999");
        yield return FuzzSeed.FromText("date", [1], "1815-12-10");
        yield return FuzzSeed.FromText("time", [2], "07:30:00");
        yield return FuzzSeed.FromText("datetime-local", [3], "2026-07-01T12:30:00");
        yield return FuzzSeed.FromText("month", [4], "2026-07");
        yield return FuzzSeed.FromText("week", [5], "2026-W27");
        yield return FuzzSeed.FromText("color", [6], "#FF0000");
        yield return FuzzSeed.FromText("invalid-date", [1], "not a date");
    }

    /// <summary>
    /// Fills an input whose value is set (a range, a date, a time, a color): a rejected value leaves the input unchanged,
    /// an accepted value is a value of the input (the empty string only for an empty text), within the bounds of a range.
    /// </summary>
    private static void FillValues(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var type = ValueInputTypes[data[0] % ValueInputTypes.Length];
        var text = FuzzText.Decode(data[1..]);
        using var page = new FuzzPage();
        if (page.Document.Body is not { } body)
            return;

        body.InnerHtml = $"<input type=\"{type}\" min=\"0\" max=\"10\" aria-label=\"Field\">";
        var field = (HtmlInputElement)page.Find("input");
        var previousValue = field.Value;
        try
        {
            page.GetByLabel("Field").FillAsync(text).GetAwaiter().GetResult();
        }
        catch (ElementNotInteractableException)
        {
            FuzzAssert.Equal(previousValue, field.Value, "A rejected value leaves the input unchanged");
            return;
        }

        var value = field.Value;
        if (text.Trim().Length > 0 || type is "range" or "color")
        {
            FuzzAssert.True(value.Length > 0, $"The input of type {type} accepted {FuzzAssert.Format(text)} but its value is empty");
        }

        if (type == "range")
        {
            FuzzAssert.True(double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 10, $"The range has the value {FuzzAssert.Format(value)}");
        }
    }

    private static readonly string[] ValueInputTypes = ["range", "date", "time", "datetime-local", "month", "week", "color"];

    private const string UsersMarkup = """
        <h1>Users</h1>
        <h2 aria-hidden="true">Hidden heading</h2>
        <ul>
          <li data-testid="user-ada">Ada Lovelace <button>Delete</button></li>
          <li data-testid="user-grace">Grace Hopper <button>Delete</button></li>
          <li data-testid="user-alan" style="display: none">Alan Turing <button>Delete</button></li>
        </ul>
        <label for="search">Search users</label>
        <input id="search" placeholder="Name or email" title="Search field">
        <label>Subscribe to the newsletter <input type="checkbox" checked></label>
        <button aria-label="Close dialog">x</button>
        <span id="caption">Profile picture</span>
        <img alt="Portrait of Ada" aria-labelledby="caption">
        <input type="submit" value="Send now">
        """;

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("role", "r:button:Delete\n" + UsersMarkup);
        yield return FuzzSeed.FromText("role-exact-hidden", "reh:heading:Hidden heading\n" + UsersMarkup);
        yield return FuzzSeed.FromText("role-input-types", "r:textbox:Start date\n<label>Start date <input type=date value=2026-01-14></label><input type=password aria-label=Password><input type=color><input type=file aria-label=Upload><input type=hidden>");
        yield return FuzzSeed.FromText("text", "t:ada\n" + UsersMarkup);
        yield return FuzzSeed.FromText("text-exact", "te:Send now\n" + UsersMarkup);
        yield return FuzzSeed.FromText("label", "l:Search\n" + UsersMarkup);
        yield return FuzzSeed.FromText("label-aria", "l:Profile picture\n" + UsersMarkup);
        yield return FuzzSeed.FromText("label-nested-controls", "le:Currency\n<label>Name <input type=text value=Ada></label><label>Currency <select><option></option><option>USD</option><option selected>EUR</option></select></label><label>Account <button type=button>Checking</button></label><label><input type=checkbox> Flash every <input type=number value=3> seconds</label><label>Notes <textarea>Some notes</textarea></label>");
        yield return FuzzSeed.FromText("label-aria-valuetext", "le:Notify at low\n<label><input type=checkbox> Notify at <input type=range value=25 aria-valuetext=low></label><label><input type=checkbox> Repeat <input type=number value=2 aria-valuenow=4> times</label>");
        yield return FuzzSeed.FromText("placeholder", "p:email\n" + UsersMarkup);
        yield return FuzzSeed.FromText("alt", "a:portrait\n" + UsersMarkup);
        yield return FuzzSeed.FromText("title", "T:Search field\n" + UsersMarkup);
        yield return FuzzSeed.FromText("test-id", "i:user-grace\n" + UsersMarkup);
        yield return FuzzSeed.FromText("css", "c:li > button\n" + UsersMarkup);
        yield return FuzzSeed.FromText("css-scope", "c::scope > body > ul\n" + UsersMarkup);
        yield return FuzzSeed.FromText("nested-text", "t:a\n<div>a<div>a<span>a</span></div><p>A</p></div><script>a</script>");
    }

    private static void Locators(ReadOnlySpan<byte> data)
    {
        var input = FuzzText.Decode(data);
        var newLine = input.IndexOf('\n', StringComparison.Ordinal);
        if (newLine < 0)
            return;

        var query = input[..newLine];
        var separator = query.IndexOf(':', StringComparison.Ordinal);
        if (separator < 1)
            return;

        var kind = query[0];
        var flags = query[1..separator];
        var argument = query[(separator + 1)..];
        var exact = flags.Contains('e', StringComparison.Ordinal);

        using var page = new FuzzPage();
        if (page.Document.Body is not { } body)
            return;

        body.InnerHtml = input[(newLine + 1)..];

        Locator locator;
        try
        {
            locator = kind switch
            {
                'c' => page.Locator(argument),
                'r' => CreateRoleLocator(page, argument, exact, includeHidden: flags.Contains('h', StringComparison.Ordinal)),
                't' => page.GetByText(argument, exact),
                'l' => page.GetByLabel(argument, exact),
                'p' => page.GetByPlaceholder(argument, exact),
                'a' => page.GetByAltText(argument, exact),
                'T' => page.GetByTitle(argument, exact),
                'i' => page.GetByTestId(argument),
                _ => null!,
            };
        }
        catch (DomException)
        {
            // An invalid CSS selector
            return;
        }

        if (locator is null)
            return;

        var elements = locator.GetElements();
        CheckConsistency(page, locator, elements);

        switch (kind)
        {
            case 'c':
                var expected = page.Document.QuerySelectorAll(argument).OfType<Element>().ToList();
                FuzzAssert.True(expected.SequenceEqual(elements), $"Locator({FuzzAssert.Format(argument)}) finds {elements.Count} elements, QuerySelectorAll finds {expected.Count}");
                break;

            case 'r':
                foreach (var element in elements)
                {
                    FuzzAssert.True(element.ComputedRole == GetRole(argument), $"GetByRole found an element whose role is {element.ComputedRole}");
                }

                if (!flags.Contains('h', StringComparison.Ordinal))
                {
                    foreach (var element in elements)
                    {
                        FuzzAssert.False(element.GetAccessibilityProperties().IsHidden, "GetByRole found an element hidden from the accessibility tree");
                    }

                    var withHidden = CreateRoleLocator(page, argument, exact, includeHidden: true).GetElements();
                    FuzzAssert.True(elements.All(withHidden.Contains), "IncludeHidden does not find all the visible elements");
                }

                break;

            case 't':
                // The smallest elements: no element contains another one
                foreach (var element in elements)
                {
                    FuzzAssert.False(elements.Any(other => other != element && element.Contains(other)), "GetByText found an element and one of its descendants");
                }

                // The search agrees with the filter by text, and an exact text is also a substring
                var filtered = page.Locator("*").Filter(new LocatorFilterOptions { HasText = argument }).GetElements();
                FuzzAssert.True(elements.All(filtered.Contains), "GetByText found an element that Filter(HasText) does not keep");
                if (exact)
                {
                    var lax = page.GetByText(argument).GetElements();
                    FuzzAssert.True(elements.All(lax.Contains), "GetByText(exact: true) found an element that GetByText does not find");
                }

                break;

            case 'l':
                // The elements are labelled by aria-labelledby, aria-label or a label element, and an exact label is also a substring
                var labelled = page.Document.QuerySelectorAll("label").OfType<HtmlLabelElement>().Select(label => label.Control).OfType<Element>().ToHashSet();
                foreach (var element in elements)
                {
                    FuzzAssert.True(element.HasAttribute("aria-labelledby") || element.HasAttribute("aria-label") || labelled.Contains(element), "GetByLabel found an element without label");
                }

                if (exact)
                {
                    var lax = page.GetByLabel(argument).GetElements();
                    FuzzAssert.True(elements.All(lax.Contains), "GetByLabel(exact: true) found an element that GetByLabel does not find");
                }

                break;

            case 'p' or 'a' or 'T' or 'i':
                var attribute = kind switch { 'p' => "placeholder", 'a' => "alt", 'T' => "title", _ => "data-testid" };
                foreach (var element in elements)
                {
                    FuzzAssert.True(element.HasAttribute(attribute), $"The element has no {attribute} attribute");
                    if (kind == 'i')
                    {
                        FuzzAssert.Equal(argument, element.GetAttribute(attribute), "GetByTestId compares the whole value");
                    }
                }

                break;
        }
    }

    private static Locator CreateRoleLocator(FuzzPage page, string argument, bool exact, bool includeHidden)
    {
        var nameSeparator = argument.IndexOf(':', StringComparison.Ordinal);
        var options = new GetByRoleOptions { Exact = exact, IncludeHidden = includeHidden };
        return nameSeparator < 0 ? page.GetByRole(argument, options: options) : page.GetByRole(argument[..nameSeparator], argument[(nameSeparator + 1)..], options);
    }

    private static string GetRole(string argument) => argument.IndexOf(':', StringComparison.Ordinal) is var index and >= 0 ? argument[..index] : argument;

    /// <summary>The elements are distinct, in document order, and the other code paths of the locator agree with them.</summary>
    private static void CheckConsistency(FuzzPage page, Locator locator, IReadOnlyList<Element> elements)
    {
        FuzzAssert.Equal(elements.Count, locator.Count, "Count is the number of elements");
        FuzzAssert.Equal(elements.Count, elements.Distinct().Count(), "The elements are distinct");

        var order = page.Document.QuerySelectorAll("*").OfType<Element>().Select((element, index) => (element, index)).ToDictionary(item => item.element, item => item.index);
        for (var i = 1; i < elements.Count; i++)
        {
            FuzzAssert.True(order[elements[i - 1]] < order[elements[i]], "The elements are in document order");
        }

        FuzzAssert.True(locator.Or(locator).GetElements().SequenceEqual(elements), "Or with itself finds the same elements");
        FuzzAssert.True(locator.And(locator).GetElements().SequenceEqual(elements), "And with itself finds the same elements");
        FuzzAssert.True(locator.Filter(new LocatorFilterOptions()).GetElements().SequenceEqual(elements), "An empty filter keeps the elements");
        if (elements.Count > 0)
        {
            FuzzAssert.True(locator.First.GetElement() == elements[0], "First is the first element");
            FuzzAssert.True(locator.Last.GetElement() == elements[^1], "Last is the last element");
            FuzzAssert.True(locator.Nth(elements.Count - 1).GetElement() == elements[^1], "Nth(Count - 1) is the last element");
        }

        FuzzAssert.Equal(0, locator.Nth(elements.Count).Count, "Nth(Count) finds no element");
        if (elements.Count <= 1)
        {
            FuzzAssert.True(locator.GetElementOrDefault() == (elements.Count == 0 ? null : elements[0]), "GetElementOrDefault finds the element");
        }
        else
        {
            try
            {
                _ = locator.GetElementOrDefault();
                FuzzAssert.Fail("GetElementOrDefault is strict");
            }
            catch (StrictModeViolationException)
            {
            }
        }
    }

    private sealed class FuzzPage : TestPage
    {
        public FuzzPage()
            : base(new TestPageSettings())
        {
        }
    }
}
