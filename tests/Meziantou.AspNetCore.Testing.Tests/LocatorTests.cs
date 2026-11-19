using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Testing.Components;
using Meziantou.AspNetCore.Testing.Tests.TestComponents;
using Meziantou.Framework.WebPlatform.Dom;
using static Meziantou.AspNetCore.Testing.Expectations;

namespace Meziantou.AspNetCore.Testing.Tests;

public sealed class LocatorTests
{
    private const string UsersMarkup = """
        <main>
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
          <button aria-pressed="true">Bold</button>
          <button aria-expanded="false">Menu</button>
          <button disabled>Archive</button>
          <h3>Section</h3>
        </main>
        """;

    private static Task<RenderedComponent<FragmentContainer>> RenderMarkupAsync(ComponentTestHost host, string markup)
        => host.RenderAsync(builder => builder.AddMarkupContent(0, markup));

    [Fact]
    public async Task GetBy_FindsTheElementsAsAUser()
    {
        #region Locator_Queries
        await using var host = new ComponentTestHost();
        var page = await host.RenderAsync(builder => builder.AddMarkupContent(0, """
            <h1>Users</h1>
            <label for="search">Search users</label>
            <input id="search" placeholder="Name or email">
            <label>Subscribe <input type="checkbox" checked></label>
            <button aria-pressed="true">Bold</button>
            <img alt="Portrait of Ada">
            <p data-testid="user">Ada Lovelace <button>Delete</button></p>
            """));

        // A text matches a substring, ignoring the case, unless it is exact
        Assert.Equal("Users", page.GetByRole("heading", "users").TextContent);
        Assert.Equal(0, page.GetByRole("heading", "users", new() { Exact = true }).Count);

        // The states of the accessibility tree
        Assert.Equal("Users", page.GetByRole("heading", options: new() { Level = 1 }).TextContent);
        Assert.Equal("Bold", page.GetByRole("button", options: new() { Pressed = true }).TextContent);
        Assert.True(page.GetByLabel("subscribe").IsChecked);

        // The labels, the attributes and the texts
        Assert.Equal("search", page.GetByLabel("Search users").GetAttribute("id"));
        Assert.Equal("search", page.GetByPlaceholder("email").GetAttribute("id"));
        Assert.Equal("img", page.GetByAltText("Portrait").GetElement().LocalName);
        Assert.Equal("p", page.GetByText("Ada Lovelace").GetElement().LocalName);
        await page.GetByTestId("user").GetByRole("button", "Delete").ClickAsync();
        #endregion
    }

    [Fact]
    public async Task Locator_FiltersAndCombinesTheElements()
    {
        #region Locator_Filters
        await using var host = new ComponentTestHost();
        var page = await host.RenderAsync(builder => builder.AddMarkupContent(0, """
            <ul>
              <li>Ada Lovelace <button>Delete</button></li>
              <li>Grace Hopper <button>Delete</button></li>
              <li>Alan Turing</li>
            </ul>
            """));
        var users = page.GetByRole("listitem");
        Assert.Equal(3, users.Count);

        // Search inside the elements of a locator, filter them by text or by descendant, or pick one
        await users.Filter(new() { HasText = "grace" }).GetByRole("button", "Delete").ClickAsync();
        Assert.Equal(2, users.Filter(new() { Has = page.GetByRole("button", "Delete") }).Count);
        Assert.Equal("Alan Turing", users.Filter(new() { HasNot = page.GetByRole("button") }).TextContent);
        Assert.Equal("Alan Turing", users.Last.TextContent);
        Assert.Equal("Ada Lovelace Delete", users.First.TextContent);

        // Combine locators
        Assert.Equal(2, page.GetByRole("button").And(page.GetByText("Delete")).Count);
        Assert.Equal(5, users.Or(page.GetByRole("button")).Count);
        #endregion
    }

    [Fact]
    public async Task GetBy_MatchesSubstringsExactTextsAndRegularExpressions()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, UsersMarkup);

        // A text matches a substring, ignoring the case and collapsing the white space, unless it is exact
        Assert.Equal("Users", page.GetByRole("heading", "users").TextContent);
        Assert.Equal("Users", page.GetByRole("heading", "Users", new() { Exact = true }).TextContent);
        Assert.Equal(0, page.GetByRole("heading", "users", new() { Exact = true }).Count);
        Assert.Equal("Section", page.GetByRole("heading", new Regex("^Sec", RegexOptions.NonBacktracking)).TextContent);

        // The states of the accessibility tree
        Assert.Equal("Users", page.GetByRole("heading", options: new() { Level = 1 }).TextContent);
        Assert.Equal("Bold", page.GetByRole("button", options: new() { Pressed = true }).TextContent);
        Assert.Equal("Menu", page.GetByRole("button", options: new() { Expanded = false }).TextContent);
        Assert.Equal("Archive", page.GetByRole("button", options: new() { Disabled = true }).TextContent);
        Assert.Equal("checkbox", page.GetByRole("checkbox", options: new() { Checked = true }).GetAttribute("type"));

        // The labels: <label>, aria-label and aria-labelledby
        Assert.Equal("search", page.GetByLabel("Search users").GetAttribute("id"));
        Assert.True(page.GetByLabel("subscribe").IsChecked);
        Assert.Equal("x", page.GetByLabel("Close dialog").TextContent);
        Assert.Equal("Portrait of Ada", page.GetByLabel("Profile picture").GetAttribute("alt"));

        // The attributes
        Assert.Equal("search", page.GetByPlaceholder("name or email").GetAttribute("id"));
        Assert.Equal("search", page.GetByTitle("Search field").GetAttribute("id"));
        Assert.Equal("img", page.GetByAltText("Portrait").GetElement().LocalName);
        Assert.Equal("Grace Hopper Delete", page.GetByTestId("user-grace").GetElement().TextContent!.Trim());

        // The text finds the smallest element that contains it; the text of an input button is its value
        Assert.Equal("li", page.GetByText("Ada Lovelace").GetElement().LocalName);
        Assert.Equal("submit", page.GetByText("Send now").GetAttribute("type"));

        // The elements hidden from the accessibility tree are found with IncludeHidden
        Assert.Equal(0, page.GetByRole("heading", "Hidden").Count);
        Assert.Equal("Hidden heading", page.GetByRole("heading", "Hidden", new() { IncludeHidden = true }).TextContent);
        Assert.Equal(2, page.GetByRole("button", "Delete").Count);
        Assert.Equal(2, page.GetByRole("button", "Delete", new() { IncludeHidden = true }).Count);
        Assert.Equal(3, page.Locator("li button").Count);

        // A placeholder is not a label, and GetByTestId compares the whole value
        Assert.Equal(0, page.GetByLabel("Name or email").Count);
        Assert.Equal(0, page.GetByTestId("user").Count);
        Assert.Equal(3, page.GetByTestId(new Regex("^user-", RegexOptions.NonBacktracking)).Count);
        Assert.Equal(0, page.GetByText("Ada Lovelace", exact: true).Count);
        Assert.Equal("li", page.GetByText(new Regex(@"^\s*Ada Lovelace", RegexOptions.NonBacktracking)).GetElement().LocalName);
    }

    [Fact]
    public async Task GetByLabel_TheControlsNestedInTheLabelContributeTheirValue()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, """
            <label>Name <input type="text" value="Ada" /></label>
            <label>Currency <select><option></option><option>USD</option><option selected>EUR</option></select></label>
            <label>Account <button type="button">Checking ▼</button></label>
            <label>Notes <textarea>Some notes</textarea></label>
            <label><input type="checkbox" /> Flash every <input type="number" id="seconds" value="3" /> seconds</label>
            <label>Color <select id="color"><option>Red</option></select><span>(required)</span></label>
            <label><input type="checkbox" id="notify" /> Notify at <input type="range" value="25" aria-valuetext="low" /></label>
            <label><input type="checkbox" id="repeat" /> Repeat <input type="number" value="2" aria-valuenow="4" /> times</label>
            """);

        Assert.Equal("text", page.GetByLabel("Name", exact: true).GetAttribute("type"));
        Assert.Equal("select", page.GetByLabel("Currency", exact: true).GetElement().LocalName);
        Assert.Equal("select", page.GetByLabel(new Regex(@"^Currency\s*$", RegexOptions.NonBacktracking)).GetElement().LocalName);
        Assert.Equal("button", page.GetByLabel("Account", exact: true).GetElement().LocalName);
        Assert.Equal("button", page.GetByLabel("Account").GetElement().LocalName);
        Assert.Equal("textarea", page.GetByLabel("Notes", exact: true).GetElement().LocalName);
        Assert.Equal(0, page.GetByLabel("USD").Count);
        Assert.Equal(0, page.GetByLabel("Checking").Count);
        Assert.Equal(0, page.GetByLabel("Some notes").Count);

        // Another control of the label contributes its value
        Assert.Equal("checkbox", page.GetByLabel("Flash every 3 seconds", exact: true).GetAttribute("type"));
        Assert.Equal("color", page.GetByLabel("Color (required)", exact: true).GetAttribute("id"));

        // As in the accessible name, the value of a range or a number is its aria-valuetext, else its aria-valuenow
        Assert.Equal("notify", page.GetByLabel("Notify at low", exact: true).GetAttribute("id"));
        Assert.Equal("Notify at low", page.GetByLabel("Notify at low", exact: true).GetElement().ComputedName);
        Assert.Equal("repeat", page.GetByLabel("Repeat 4 times", exact: true).GetAttribute("id"));
        Assert.Equal("Repeat 4 times", page.GetByLabel("Repeat 4 times", exact: true).GetElement().ComputedName);

        // The value is the current value of the control
        await page.GetByRole("combobox", "Currency").SelectOptionAsync("USD");
        Assert.Equal(0, page.GetByLabel("EUR").Count);
        await page.Locator("#seconds").FillAsync("5");
        Assert.Equal("checkbox", page.GetByLabel("Flash every 5 seconds", exact: true).GetAttribute("type"));
    }

    [Fact]
    public async Task Locator_ChainsFiltersAndCombines()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, UsersMarkup);

        var users = page.GetByRole("listitem");
        Assert.Equal(2, users.Count);

        // Search inside the elements of a locator, and filter them by text or by descendant
        Assert.Equal("user-grace", users.Filter(new() { HasText = "grace" }).GetAttribute("data-testid"));
        Assert.Equal("user-ada", users.Filter(new() { HasNotText = "Grace" }).GetAttribute("data-testid"));
        Assert.Equal(2, users.Filter(new() { Has = page.GetByRole("button", "Delete") }).Count);
        Assert.Equal("user-grace", users.Last.GetAttribute("data-testid"));
        Assert.Equal("user-ada", users.Nth(0).GetAttribute("data-testid"));
        await users.Filter(new() { HasText = "Ada" }).GetByRole("button", "Delete").ClickAsync();

        // Combine locators
        Assert.Equal(1, page.GetByRole("button").And(page.GetByText("Bold")).Count);
        Assert.Equal(2, page.GetByRole("heading", "Users").Or(page.GetByRole("heading", "Section")).Count);

        // The CSS locators find the hidden elements, Filter(Visible) does not
        Assert.Equal(3, page.Locator("li").Count);
        Assert.Equal(2, page.Locator("li").Filter(new() { Visible = true }).Count);
        Assert.Equal(1, page.Locator("li").Filter(new() { Visible = false }).Count);
        Assert.Equal(0, users.Filter(new() { HasNot = page.GetByRole("button", "Delete") }).Count);

        // Has searches the descendants: the text of the item itself is not in a descendant
        Assert.Equal(0, users.Filter(new() { Has = page.GetByText("Ada") }).Count);
        Assert.Equal(["user-ada", "user-grace"], users.GetAll().Select(user => user.GetAttribute("data-testid")));
        Assert.Equal(0, users.Nth(5).Count);
        Assert.Equal("user-grace", users.Nth(-1).GetAttribute("data-testid"));

        // Or returns the elements in document order, without duplicates
        Assert.Equal(["Users", "Section"], page.GetByRole("heading", "Section").Or(page.GetByRole("heading")).GetElements().Select(element => element.TextContent));
        Assert.Equal("GetByRole(\"listitem\").Filter(HasText = \"grace\").First", users.Filter(new() { HasText = "grace" }).First.ToString());
    }

    [Fact]
    public async Task Locator_ScopeIsTheElementOfTheParentLocator()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, """
            <div id="panel"><button>Direct</button><p><button>Nested</button></p></div>
            <div id="other"><button>Other</button></div>
            """);

        // :scope is the element that the locator searches, as in querySelectorAll
        Assert.Equal(["Direct"], page.Locator("#panel").Locator(":scope > button").GetElements().Select(element => element.TextContent));
        Assert.Equal(["Direct", "Other"], page.Locator("div").Locator(":scope > button").GetElements().Select(element => element.TextContent));
        Assert.Equal(["Direct", "Nested"], page.Locator("#panel").Locator("button").GetElements().Select(element => element.TextContent));
        await page.Locator("#panel").Locator(":scope > button").ClickAsync();

        // The elements of an open shadow root are found too
        await host.InvokeAsync(() =>
        {
            var shadowRoot = page.Find("#other").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
            shadowRoot.InnerHtml = "<button>Shadow</button><slot></slot>";
        });
        Assert.Equal(["Shadow", "Other"], page.Locator("#other").Locator("button").GetElements().Select(element => element.TextContent));
    }

    [Fact]
    public async Task Locator_FindsTheElementsOfOpenShadowRoots()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, "<div id=\"host\"></div><button>Outside</button>");
        await host.InvokeAsync(() =>
        {
            var shadowRoot = page.Find("#host").AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
            shadowRoot.InnerHtml = "<label>Name <input></label><button class=\"inner\">Inside</button>";
        });

        Assert.Equal("Inside", page.Locator(".inner").TextContent);
        Assert.Equal("Inside", page.GetByRole("button", "Inside").TextContent);
        Assert.Equal("button", page.GetByText("Inside", exact: true).GetElement().LocalName);
        Assert.Equal("input", page.GetByLabel("Name").GetElement().LocalName);
        Assert.Equal(2, page.GetByRole("button").Count);
        await page.GetByRole("button", "Inside").ClickAsync();
    }

    [Fact]
    public async Task Locator_ActionsAreStrict()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, UsersMarkup);

        var exception = await Assert.ThrowsAsync<StrictModeViolationException>(() => page.GetByRole("button", "Delete").ClickAsync());
        Assert.Equal("Strict mode violation: GetByRole(\"button\", \"Delete\") finds 2 elements: <button>, <button>", exception.Message);
        Assert.Throws<StrictModeViolationException>(() => page.GetByRole("listitem").GetElement());
        Assert.Throws<StrictModeViolationException>(() => _ = page.GetByRole("listitem").TextContent);
        await Assert.ThrowsAsync<StrictModeViolationException>(() => Expect(page.GetByRole("listitem")).ToBeVisibleAsync());
    }

    [Fact]
    public async Task Locator_ActionsWaitUntilTheElementIsActionable()
    {
        #region Locator_AutoWait
        await using var host = new ComponentTestHost();

        // Render without waiting: the controls are disabled, or not rendered, for a while
        var form = host.Render<DelayedForm>();

        // The click waits until the button is enabled, the fill until the field is rendered,
        // and the selection until the select element has the option (by value or by label)
        await form.GetByRole("button", "Save").ClickAsync();
        await form.GetByLabel("Name").FillAsync("Ada");
        await form.GetByLabel("Color").SelectOptionAsync("Red");

        await Expect(form.GetByRole("status")).ToHaveTextAsync("Saved 1 times");
        await Expect(form.Locator("#name")).ToHaveTextAsync("Ada");
        await Expect(form.Locator("#color")).ToHaveTextAsync("r");
        #endregion
    }

    [Fact]
    public async Task Locator_ActionsFailWhenTheElementIsNotActionable()
    {
        await using var host = new ComponentTestHost(new ComponentTestHostOptions { DefaultTimeout = TimeSpan.FromMilliseconds(200) });
        var page = await RenderMarkupAsync(host, """
            <button disabled>Disabled</button>
            <input aria-label="Read-only" readonly>
            <p>Text</p>
            <input type="radio" aria-label="Radio" checked>
            <select aria-label="Color"><option value="r">Red</option></select>
            """);

        var notFound = await Assert.ThrowsAsync<ElementNotFoundException>(() => page.GetByRole("button", "Missing").ClickAsync());
        Assert.Equal("GetByRole(\"button\", \"Missing\"): no element matches the locator", notFound.Message);

        var disabled = await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByRole("button", "Disabled").ClickAsync());
        Assert.Equal("The element <button> cannot receive the input: it is disabled", disabled.Message);

        var readOnly = await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByLabel("Read-only").FillAsync("text"));
        Assert.Equal("The element <input> cannot receive the input: it is not editable", readOnly.Message);

        var notAField = await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByText("Text").FillAsync("text"));
        Assert.Equal("The element <p> is not a text field, a date, time, color or range <input>, a <textarea> or a [contenteditable] element", notAField.Message);

        var missingOption = await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByLabel("Color").SelectOptionAsync("Blue"));
        Assert.Equal("The element <select> cannot receive the input: it has no option whose value or label is \"Blue\"", missingOption.Message);

        await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByLabel("Radio").UncheckAsync());
        await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByText("Text").CheckAsync());

        // Force skips the checks
        await page.GetByRole("button", "Disabled").ClickAsync(new ClickOptions { Force = true });
    }

    [Fact]
    public async Task Locator_ActionsScrollTheElementIntoTheViewport()
    {
        await using var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            DefaultTimeout = TimeSpan.FromMilliseconds(500),
            Viewport = new ViewportOptions { Width = 400, Height = 300 },
            StyleSheets =
            {
                """
                body { margin: 0; height: 2000px }
                input { margin: 0; width: 20px; height: 20px }
                #partial { position: absolute; top: 295px; left: 10px }
                #scroller { position: absolute; top: 0; left: 100px; width: 100px; height: 100px; overflow: auto }
                #clipped { margin-top: 95px }
                #fixed { position: fixed; top: 400px; left: 10px }
                #tall { position: absolute; top: 0; left: 250px; width: 100px; height: 2000px; display: block }
                #tall-target { position: absolute; top: 50px; left: 10px }
                """,
            },
        });
        var page = await RenderMarkupAsync(host, """
            <input type="checkbox" id="partial" aria-label="Partial">
            <div id="scroller"><input type="checkbox" id="clipped" aria-label="Clipped"><div style="height: 300px"></div></div>
            <input type="checkbox" id="fixed" aria-label="Fixed">
            <label id="tall" for="tall-target">Tall label</label>
            <input type="checkbox" id="tall-target" aria-label="Tall target">
            """);

        // The center of the element is in the viewport, but the scroll container clips it
        var clipped = host.Document.GetElementById("clipped")!.GetBoundingClientRect();
        Assert.True(clipped.Y + (clipped.Height / 2) is > 100 and < 300);
        await page.GetByLabel("Clipped").CheckAsync();
        Assert.True(page.GetByLabel("Clipped").IsChecked);

        // The center of the element is below the viewport, but its top is visible
        await page.GetByLabel("Partial").CheckAsync();
        Assert.True(page.GetByLabel("Partial").IsChecked);

        // The requested point is scrolled into the viewport, not the center of the element
        await page.GetByText("Tall label").ClickAsync(new ClickOptions { Position = new ElementPosition(10, 1800) });
        Assert.True(page.GetByLabel("Tall target").IsChecked);

        // Scrolling cannot bring a fixed element into the viewport
        var exception = await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByLabel("Fixed").ClickAsync());
        Assert.Equal("The element <input id=\"fixed\"> cannot receive the input: the point (20, 410) is outside of the viewport (400x300)", exception.Message);
    }

    [Fact]
    public async Task Locator_ActionsScrollTheElementIntoTheViewportWithScrollSnap()
    {
        await using var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            DefaultTimeout = TimeSpan.FromMilliseconds(500),
            Viewport = new ViewportOptions { Width = 400, Height = 300 },
            StyleSheets =
            {
                """
                html { scroll-snap-type: y mandatory }
                body { margin: 0 }
                section { height: 300px; scroll-snap-align: start }
                input { position: absolute; top: 500px; left: 10px; margin: 0; width: 20px; height: 20px }
                """,
            },
        });
        var page = await RenderMarkupAsync(host, """
            <section></section><section></section><section></section><section></section>
            <input type="checkbox" aria-label="Snapped">
            """);

        // The requested point is visible once the element is scrolled to its snap position: the page does not scroll further
        await page.GetByLabel("Snapped").ClickAsync(new ClickOptions { Position = new ElementPosition(10, 10) });
        Assert.True(page.GetByLabel("Snapped").IsChecked);
        Assert.Equal(300, host.BrowsingContext.Window.ScrollY);
    }

    [Fact]
    public async Task Locator_ActionsWaitUntilTheElementIsStable()
    {
        await using var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(2),
            StyleSheets =
            {
                """
                @keyframes slide { from { left: 0 } to { left: 200px } }
                @keyframes blink { from { opacity: 1 } to { opacity: 0.5 } }
                button { position: absolute; width: 80px; height: 30px }
                #moving { top: 0; animation: slide 1s linear infinite }
                #blinking { top: 50px; animation: blink 1s linear infinite }
                """,
            },
        });
        var page = await RenderMarkupAsync(host, "<button id=\"moving\">Moving</button><button id=\"blinking\">Blinking</button>");

        var exception = await Assert.ThrowsAsync<ElementNotInteractableException>(() => page.GetByRole("button", "Moving").ClickAsync());
        Assert.Equal("The element <button id=\"moving\"> cannot receive the input: it is not stable (it is animated)", exception.Message);

        // An animation that does not move the element does not prevent the click
        await page.GetByRole("button", "Blinking").ClickAsync();
    }

    [Fact]
    public async Task Locator_CheckAndFillFollowTheLabels()
    {
        await using var host = new ComponentTestHost();
        var page = await RenderMarkupAsync(host, """
            <label><span>Accept the terms</span> <input type="checkbox"></label>
            <label>Comment <textarea></textarea></label>
            <div role="checkbox" aria-checked="false" tabindex="0" onclick="" aria-label="Custom">Custom</div>
            """);

        await page.GetByText("Accept the terms").CheckAsync();
        Assert.True(page.GetByRole("checkbox", "Accept").IsChecked);
        await page.GetByText("Accept the terms").UncheckAsync();
        await Expect(page.GetByRole("checkbox", "Accept")).Not.ToBeCheckedAsync();

        await page.GetByText("Comment").FillAsync("Hello");
        Assert.Equal("Hello", page.GetByLabel("Comment").InputValue);
        await page.GetByLabel("Comment").ClearAsync();
        await Expect(page.GetByLabel("Comment")).ToBeEmptyAsync();
        await Expect(page.GetByLabel("Comment")).ToBeEditableAsync();
    }

    [Fact]
    public async Task Locator_FillSetsTheValueOfDateTimeAndRangeInputs()
    {
        await using var host = new ComponentTestHost();
        var form = await host.RenderAsync<DateForm>();

        // The keyboard cannot type in these inputs: the value is set, then the input and change events update @bind
        await form.GetByLabel("Birth date").FillAsync("1815-12-10");
        await form.GetByLabel("Alarm").FillAsync("07:30:00");
        await form.GetByLabel("Volume").FillAsync("7");
        await Expect(form.Locator("#result")).ToHaveTextAsync("1815-12-10 07:30 7");
        await Expect(form.GetByLabel("Birth date")).ToHaveValueAsync("1815-12-10");

        await form.GetByLabel("Birth date").ClearAsync();
        await Expect(form.Locator("#result")).ToHaveTextAsync("07:30 7");

        // A valid value that the input writes in another format is accepted
        await form.GetByLabel("Meeting").FillAsync("2026-07-01T12:30:00");
        await Expect(form.GetByLabel("Meeting")).ToHaveValueAsync("2026-07-01T12:30");
        await form.GetByLabel("Color").FillAsync("#FF0000");
        await Expect(form.GetByLabel("Color")).ToHaveValueAsync("#ff0000");
        await form.GetByLabel("Volume").FillAsync("42");
        await Expect(form.GetByLabel("Volume")).ToHaveValueAsync("10");
        await form.GetByLabel("Volume").FillAsync("2.5e0");
        await Expect(form.GetByLabel("Volume")).ToHaveValueAsync("3");
        await Assert.ThrowsAsync<ElementNotInteractableException>(() => form.GetByLabel("Volume").FillAsync("loud"));

        // The number format of HTML: no plus sign, no infinity; the value is not changed
        await Assert.ThrowsAsync<ElementNotInteractableException>(() => form.GetByLabel("Volume").FillAsync("+7"));
        await Assert.ThrowsAsync<ElementNotInteractableException>(() => form.GetByLabel("Volume").FillAsync("Infinity"));
        await Assert.ThrowsAsync<ElementNotInteractableException>(() => form.GetByLabel("Volume").FillAsync("1e999"));
        await Expect(form.GetByLabel("Volume")).ToHaveValueAsync("3");
        await Assert.ThrowsAsync<ElementNotInteractableException>(() => form.GetByLabel("Color").FillAsync("red"));

        var exception = await Assert.ThrowsAsync<ElementNotInteractableException>(() => form.GetByLabel("Birth date").FillAsync("not a date"));
        Assert.Equal("The value \"not a date\" is not a valid value for the input <input> of type \"date\"", exception.Message);

        // The date, time and color fields are text fields, as in Playwright
        await form.GetByLabel("Birth date").FillAsync("1815-12-10");
        await Expect(form.GetByRole("textbox", "Birth date")).ToHaveValueAsync("1815-12-10");
        await Expect(form.GetByLabel("Meeting")).ToHaveRoleAsync("textbox");
        Assert.Equal("""
            - textbox "Birth date": 1815-12-10
            - textbox "Alarm": 07:30:00
            - slider "Volume": "3"
            - textbox "Meeting": 2026-07-01T12:30
            - textbox "Color": "#ff0000"
            - paragraph: 1815-12-10 07:30 3
            """, form.GetAccessibilitySnapshot());
    }

    [Fact]
    public async Task Expect_RetriesUntilTheAssertionPasses()
    {
        #region Locator_Assertions
        await using var host = new ComponentTestHost();
        var counter = await host.RenderAsync<Counter>();
        await counter.GetByRole("button", "Click me").ClickAsync();

        // The assertions run the event loop until they pass, and fail after the timeout of the page
        await Expect(counter.GetByRole("status")).ToHaveTextAsync("Current count: 1");
        await Expect(counter.GetByRole("button", "Click me")).ToBeEnabledAsync();
        await Expect(counter.GetByRole("button")).ToHaveCountAsync(1);
        await Expect(counter.GetByText("Current count: 2")).Not.ToBeVisibleAsync();
        #endregion
    }

    [Fact]
    public async Task Expect_AssertsTheStatesOfTheElements()
    {
        await using var host = new ComponentTestHost(new ComponentTestHostOptions { DefaultTimeout = TimeSpan.FromMilliseconds(200) });
        var page = await RenderMarkupAsync(host, """
            <h1 class="title  main" style="color: rgb(255, 0, 0)">  Hello   world  </h1>
            <input aria-label="Name" value="Ada">
            <button disabled>Archive</button>
            <input type="checkbox" aria-label="Agree" checked>
            <ul aria-label="Items"><li>One</li><li>Two</li></ul>
            """);

        await Expect(page.GetByRole("heading")).ToHaveTextAsync("Hello world");
        await Expect(page.GetByRole("heading")).ToHaveTextAsync(new Regex("^Hello", RegexOptions.NonBacktracking));
        await Expect(page.GetByRole("heading")).ToContainTextAsync("lo wor");
        await Expect(page.GetByRole("heading")).ToHaveClassAsync("title main");
        await Expect(page.GetByRole("heading")).ToHaveClassAsync(new Regex(@"\bmain\b", RegexOptions.NonBacktracking));
        await Expect(page.GetByRole("heading")).ToHaveCssAsync("color", "rgb(255, 0, 0)");
        await Expect(page.GetByRole("heading")).ToHaveAccessibleNameAsync("Hello world");
        await Expect(page.GetByRole("heading")).ToHaveAttributeAsync("class");
        await Expect(page.GetByRole("heading")).Not.ToHaveAttributeAsync("id");
        await Expect(page.GetByLabel("Name")).ToHaveValueAsync("Ada");
        await Expect(page.GetByLabel("Name")).ToHaveRoleAsync("textbox");
        await Expect(page.GetByLabel("Name")).ToBeEnabledAsync();
        await Expect(page.GetByLabel("Name")).Not.ToBeFocusedAsync();
        await Expect(page.GetByRole("button", "Archive")).ToBeDisabledAsync();
        await Expect(page.GetByLabel("Agree")).ToBeCheckedAsync();
        await Expect(page.GetByRole("listitem")).ToHaveCountAsync(2);
        await Expect(page.GetByRole("button", "Missing")).Not.ToBeAttachedAsync();
        await Expect(page.GetByRole("button", "Missing")).Not.ToBeVisibleAsync();
        await Expect(page.GetByLabel("Items")).ToHaveAccessibilitySnapshotAsync("""
            - list "Items":
              - listitem: One
              - listitem: Two
            """);

        await page.GetByLabel("Name").FocusAsync();
        await Expect(page.GetByLabel("Name")).ToBeFocusedAsync();

        var exception = await Assert.ThrowsAsync<ExpectationFailedException>(() => Expect(page.GetByRole("heading")).ToHaveTextAsync("Goodbye", TimeSpan.FromMilliseconds(50)));
        Assert.Equal("Expected GetByRole(\"heading\") to have the text \"Goodbye\", but it is \"Hello world\" (waited 00:00:00.0500000)", exception.Message);

        // An assertion on an element fails, negated or not, while the locator finds no element
        await Assert.ThrowsAsync<ExpectationFailedException>(() => Expect(page.GetByRole("button", "Missing")).Not.ToBeEnabledAsync());
        await Assert.ThrowsAsync<ExpectationFailedException>(() => Expect(page.GetByRole("heading")).Not.ToBeVisibleAsync());
    }
}
