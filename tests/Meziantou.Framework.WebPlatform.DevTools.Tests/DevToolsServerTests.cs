using System.Net.WebSockets;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Playwright;

namespace Meziantou.Framework.WebPlatform.DevTools.Tests;

public sealed class DevToolsServerTests(DevToolsServerFixture fixture) : IClassFixture<DevToolsServerFixture>
{
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    [Fact]
    public async Task ReadmeExample_Connect()
    {
        // The test starts its own driver and server. Microsoft.Playwright does not notice when its driver exits, and then
        // waits forever: the timeout fails the test instead of hanging the test run.
        byte[] screenshot;
        try
        {
            screenshot = await RunExampleAsync().WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException("The example did not complete within 2 minutes. " + DevToolsServerFixture.DescribeDrivers(), ex);
        }

        Assert.NotEmpty(screenshot);

        static async Task<byte[]> RunExampleAsync()
        {
            #region DevToolsServer_ConnectOverCDP
            await using var server = new DevToolsServer(); // no DocumentLoader: the pages use page.SetContentAsync() and the routes
            await server.StartAsync();

            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.ConnectOverCDPAsync(server.HttpEndpoint.ToString());
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <label for=name>Name</label><input id=name>
                <button onclick="document.querySelector('output').textContent = 'Hello ' + document.querySelector('input').value">Greet</button>
                <output></output>
                """);

            await page.GetByLabel("Name").FillAsync("World");
            await page.GetByRole(AriaRole.Button, new() { Name = "Greet" }).ClickAsync();
            await Assertions.Expect(page.Locator("output")).ToHaveTextAsync("Hello World");
            var screenshot = await page.ScreenshotAsync();
            #endregion

            return screenshot;
        }
    }

    [Fact]
    public async Task SetContent_EvaluateAndTitle()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<title>Hello</title><p id=p>world</p>");

        Assert.Equal("Hello", await page.TitleAsync());
        Assert.Equal(42, await page.EvaluateAsync<int>("() => 40 + 2"));
        Assert.Equal("world", await page.EvaluateAsync<string>("(id) => document.getElementById(id).textContent", "p"));
    }

    [Fact]
    public async Task Evaluate_SerializesTheArgumentsAndTheResults()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var result = await page.EvaluateAsync<Dictionary<string, object>>("(arg) => ({ sum: arg.values.reduce((a, b) => a + b, 0), name: arg.name.toUpperCase(), flag: !arg.flag, nothing: null })", new { values = new[] { 1, 2, 3 }, name = "abc", flag = false });
        var array = await page.EvaluateAsync<string[]>("() => ['a', 'b']");
        var date = await page.EvaluateAsync<DateTime>("() => new Date(Date.UTC(2020, 0, 2))");
        var promise = await page.EvaluateAsync<int>("async () => { await new Promise(r => setTimeout(r, 10)); return 5; }");

        Assert.Equal(6, Convert.ToInt32(result["sum"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("ABC", result["name"]?.ToString());
        Assert.Equal(["a", "b"], array);
        Assert.Equal(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), date.ToUniversalTime());
        Assert.Equal(5, promise);
    }

    [Fact]
    public async Task Evaluate_ThrowsTheErrorsOfThePage()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<PlaywrightException>(() => page.EvaluateAsync("() => { throw new Error('boom'); }"));

        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateHandle_JSHandleAndElementHandle()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<div id=a>text</div>");

        var handle = await page.EvaluateHandleAsync("() => ({ a: 1, b: 'two' })");
        var property = await handle.GetPropertyAsync("b");
        var element = await page.QuerySelectorAsync("#a");

        Assert.Equal("two", await property.JsonValueAsync<string>());
        Assert.Equal(1, await handle.EvaluateAsync<int>("(o) => o.a"));
        Assert.NotNull(element);
        Assert.Equal("text", await element.TextContentAsync());
        Assert.Equal("DIV", await element.EvaluateAsync<string>("(e) => e.tagName"));
        Assert.Equal("text", await page.EvalOnSelectorAsync<string>("#a", "(e) => e.textContent"));
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task Goto_ReturnsTheResponseAndWaitsForTheLoadEvent()
    {
        var url = fixture.AddPage("goto.html", "<title>Goto</title><script>window.loaded = false; addEventListener('load', () => window.loaded = true);</script>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var response = await page.GotoAsync(url);

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.True(response.Ok);
        Assert.Equal(url, page.Url);
        Assert.Equal("Goto", await page.TitleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => window.loaded"));
        Assert.Contains("<title>Goto</title>", await response.TextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Goto_NotFound_ReturnsA404Response()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var response = await page.GotoAsync("https://test.local/does-not-exist");

        Assert.NotNull(response);
        Assert.Equal(404, response.Status);
    }

    [Fact]
    public async Task Goto_NetworkError_Throws()
    {
        fixture.AddResource("network-error", _ => null);
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var exception = await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoAsync("https://test.local/network-error"));

        Assert.Contains("net::ERR_FAILED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadAndHistory()
    {
        var first = fixture.AddPage("history-1.html", "<title>One</title><a href=history-2.html>next</a>");
        fixture.AddPage("history-2.html", "<title>Two</title>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.GotoAsync(first);

        await page.ClickAsync("a");
        await Expect(page).ToHaveTitleAsync("Two");
        await page.GoBackAsync();
        Assert.Equal("One", await page.TitleAsync());
        await page.GoForwardAsync();
        Assert.Equal("Two", await page.TitleAsync());
        await page.ReloadAsync();
        Assert.Equal("Two", await page.TitleAsync());
        await Expect(page).ToHaveURLAsync("https://test.local/history-2.html");
    }

    [Fact]
    public async Task Locators_FindTheElements()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("""
            <h1>Title</h1>
            <label for=name>Name</label><input id=name placeholder="Your name">
            <button>Submit</button><button disabled>Cancel</button>
            <img alt="Logo" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
            <span title="Tooltip">hover</span>
            <ul><li>Apple</li><li>Banana</li><li>Cherry</li></ul>
            <div data-testid=status>Ready</div>
            """);

        Assert.Equal("Title", await page.GetByRole(AriaRole.Heading).TextContentAsync());
        Assert.Equal(2, await page.GetByRole(AriaRole.Button).CountAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).IsDisabledAsync());
        Assert.Equal("name", await page.GetByLabel("Name").GetAttributeAsync("id"));
        Assert.Equal("name", await page.GetByPlaceholder("Your name").GetAttributeAsync("id"));
        Assert.Equal("IMG", await page.GetByAltText("Logo").EvaluateAsync<string>("e => e.tagName"));
        Assert.Equal("hover", await page.GetByTitle("Tooltip").TextContentAsync());
        Assert.Equal("Ready", await page.GetByTestId("status").InnerTextAsync());
        Assert.Equal("Banana", await page.Locator("li").Nth(1).TextContentAsync());
        Assert.Equal("Cherry", await page.Locator("li").Last.TextContentAsync());
        Assert.Equal("Cherry", await page.Locator("li").Filter(new() { HasText = "err" }).TextContentAsync());
        Assert.Equal(["Apple", "Banana", "Cherry"], await page.Locator("ul > li").AllTextContentsAsync());
        Assert.Equal("Banana", await page.GetByText("Banana").TextContentAsync());
        Assert.Equal("Apple", await page.Locator("ul").Locator("li").First.TextContentAsync());
        Assert.Equal("Apple", await page.Locator("xpath=//li[1]").TextContentAsync());
    }

    [Fact]
    public async Task Locators_StrictModeViolation_Throws()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<button>A</button><button>B</button>");

        var exception = await Assert.ThrowsAsync<PlaywrightException>(() => page.GetByRole(AriaRole.Button).ClickAsync(new() { Timeout = 2000 }));

        Assert.Contains("strict mode violation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actions_ClickFillTypePressCheckSelect()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("""
            <button onclick="this.textContent = Number(this.textContent) + 1">0</button>
            <input id=text>
            <textarea id=area></textarea>
            <input id=check type=checkbox>
            <select id=select><option value=a>Alpha</option><option value=b>Beta</option></select>
            <div id=dbl ondblclick="this.textContent = 'double'">dbl</div>
            """);

        await page.Locator("button").ClickAsync();
        await page.Locator("button").ClickAsync();
        await page.Locator("#text").FillAsync("hello");
        await page.Locator("#area").PressSequentiallyAsync("abc");
        await page.Locator("#area").PressAsync("Backspace");
        await page.Locator("#check").CheckAsync();
        var selected = await page.Locator("#select").SelectOptionAsync("Beta");
        await page.Locator("#dbl").DblClickAsync();

        Assert.Equal("2", await page.Locator("button").TextContentAsync());
        Assert.Equal("hello", await page.Locator("#text").InputValueAsync());
        Assert.Equal("ab", await page.Locator("#area").InputValueAsync());
        Assert.True(await page.Locator("#check").IsCheckedAsync());
        Assert.Equal(["b"], selected);
        Assert.Equal("b", await page.Locator("#select").InputValueAsync());
        Assert.Equal("double", await page.Locator("#dbl").TextContentAsync());

        await page.Locator("#check").UncheckAsync();
        Assert.False(await page.Locator("#check").IsCheckedAsync());
    }

    [Fact]
    public async Task Actions_PressEnterSubmitsTheForm()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<form onsubmit=\"event.preventDefault(); document.title = 'submitted ' + this.q.value\"><input name=q></form>");

        await page.Locator("input").FillAsync("query");
        await page.Locator("input").PressAsync("Enter");

        await Expect(page).ToHaveTitleAsync("submitted query");
    }

    [Fact]
    public async Task Actions_WaitForTheElementToBeEnabled()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<button disabled onclick=\"document.title = 'clicked'\">Go</button><script>setTimeout(() => document.querySelector('button').disabled = false, 200);</script>");

        await page.GetByRole(AriaRole.Button, new() { Name = "Go" }).ClickAsync();

        Assert.Equal("clicked", await page.TitleAsync());
    }

    [Fact]
    public async Task Actions_HoverAndFocus()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<div id=a style='width: 100px; height: 100px' onmouseover=\"document.title = 'over'\"></div><input id=b onfocus=\"this.value = 'focused'\">");

        await page.Locator("#a").HoverAsync();
        await page.Locator("#b").FocusAsync();

        Assert.Equal("over", await page.TitleAsync());
        Assert.Equal("focused", await page.Locator("#b").InputValueAsync());
    }

    [Fact]
    public async Task Actions_SetInputFiles()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<input type=file onchange=\"document.title = this.files[0].name + ':' + this.files[0].size\">");

        await page.Locator("input").SetInputFilesAsync(new FilePayload { Name = "file.txt", MimeType = "text/plain", Buffer = Encoding.UTF8.GetBytes("hello") });

        await Expect(page).ToHaveTitleAsync("file.txt:5");
    }

    [Fact]
    public async Task Keyboard_TypeAndShortcuts()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<input id=a autofocus>");
        await page.Locator("#a").FocusAsync();

        await page.Keyboard.TypeAsync("Hello");
        await page.Keyboard.PressAsync("Shift+A");
        await page.Keyboard.InsertTextAsync(" world");

        Assert.Equal("HelloA world", await page.Locator("#a").InputValueAsync());
    }

    [Fact]
    public async Task Mouse_ClickAtCoordinates()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<style>body { margin: 0 }</style><div style='width: 100px; height: 100px' onclick=\"document.title = event.clientX + ',' + event.clientY\"></div>");

        await page.Mouse.ClickAsync(10, 20);

        Assert.Equal("10,20", await page.TitleAsync());
    }

    [Fact]
    public async Task Assertions_PassAndFail()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<title>Assertions</title><p id=a>Hello world</p><p id=b hidden>Hidden</p><input id=c value=v><input id=d type=checkbox checked><ul><li>1</li><li>2</li></ul>");

        await Expect(page.Locator("#a")).ToHaveTextAsync("Hello world");
        await Expect(page.Locator("#a")).ToContainTextAsync("world");
        await Expect(page.Locator("#a")).ToBeVisibleAsync();
        await Expect(page.Locator("#b")).ToBeHiddenAsync();
        await Expect(page.Locator("#c")).ToHaveValueAsync("v");
        await Expect(page.Locator("#d")).ToBeCheckedAsync();
        await Expect(page.Locator("li")).ToHaveCountAsync(2);
        await Expect(page.Locator("li")).ToHaveTextAsync(["1", "2"]);
        await Expect(page.Locator("#a")).ToHaveAttributeAsync("id", "a");
        await Expect(page.Locator("#missing")).Not.ToBeVisibleAsync();
        await Expect(page).ToHaveTitleAsync("Assertions");

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => Expect(page.Locator("#a")).ToHaveTextAsync("Goodbye", new() { Timeout = 500 }));
        Assert.Contains("Goodbye", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Hello world", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assertions_WaitForTheExpectedValue()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<p id=a>Loading</p><script>setTimeout(() => document.getElementById('a').textContent = 'Done', 300);</script>");

        await Expect(page.Locator("#a")).ToHaveTextAsync("Done");
    }

    [Fact]
    public async Task WaitForSelectorAndWaitForFunction()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<script>setTimeout(() => { const p = document.createElement('p'); p.id = 'late'; p.textContent = 'late'; document.body.append(p); window.ready = true; }, 200);</script>");

        var element = await page.WaitForSelectorAsync("#late");
        await page.WaitForFunctionAsync("() => window.ready === true");

        Assert.NotNull(element);
        Assert.Equal("late", await element.TextContentAsync());
    }

    [Fact]
    public async Task Screenshots()
    {
        var (context, page) = await fixture.NewPageAsync(new() { ViewportSize = new() { Width = 200, Height = 100 } });
        await using var _ = context;
        await page.SetContentAsync("<style>body { margin: 0; background: red } div { width: 50px; height: 20px; background: blue }</style><div></div>");

        var png = await page.ScreenshotAsync();
        var jpeg = await page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg });
        var element = await page.Locator("div").ScreenshotAsync();

        Assert.Equal((200, 100), GetPngSize(png));
        Assert.Equal((50, 20), GetPngSize(element));
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);

        static (int Width, int Height) GetPngSize(byte[] png) => (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Fact]
    public async Task Pdf()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<h1>PDF</h1>");

        var pdf = await page.PdfAsync(new() { Format = "A4" });

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Dialogs_AreGivenToTheClient()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        var messages = new List<string>();
        page.Dialog += async (_, dialog) =>
        {
            messages.Add(dialog.Type + ":" + dialog.Message);
            await dialog.AcceptAsync(dialog.Type == "prompt" ? "answer" : null);
        };

        var result = await page.EvaluateAsync<string>("() => [confirm('sure?'), prompt('name?')].join()");

        Assert.Equal("true,answer", result);
        Assert.Equal(["confirm:sure?", "prompt:name?"], messages);
    }

    [Fact]
    public async Task Dialogs_WithoutHandler_AreDismissed()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        Assert.False(await page.EvaluateAsync<bool>("() => confirm('sure?')"));
    }

    [Fact]
    public async Task ConsoleMessagesAndPageErrors()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        var console = new List<string>();
        var errors = new List<string>();
        page.Console += (_, message) => console.Add(message.Type + ":" + message.Text);
        page.PageError += (_, error) => errors.Add(error);

        await page.SetContentAsync("<script>console.log('hello', 1); console.warn('careful'); setTimeout(() => { throw new Error('oops'); }, 0);</script>");
        await page.WaitForTimeoutAsync(200);

        Assert.Contains("log:hello 1", console);
        Assert.Contains("warning:careful", console);
        Assert.Contains(errors, error => error.Contains("oops", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Popups()
    {
        var target = fixture.AddPage("popup-target.html", "<title>Popup</title>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync($"<a href='{target}' target=_blank>open</a>");

        var popup = await page.RunAndWaitForPopupAsync(() => page.ClickAsync("a"));
        await popup.WaitForLoadStateAsync();

        Assert.Equal("Popup", await popup.TitleAsync());
        Assert.Equal(2, context.Pages.Count);
    }

    [Fact]
    public async Task Popups_TheScriptsAndTheRoutesOfTheContextApplyToTheFirstDocument()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await context.AddInitScriptAsync("window.fromInitScript = 42;");
        await context.RouteAsync("**/routed-popup", route => route.FulfillAsync(new() { Body = "<script>document.title = String(window.fromInitScript);</script>", ContentType = "text/html" }));
        await page.SetContentAsync("<a href='https://test.local/routed-popup' target=_blank>open</a>");

        var popup = await page.RunAndWaitForPopupAsync(() => page.ClickAsync("a"));
        await popup.WaitForLoadStateAsync();

        Assert.Equal("42", await popup.TitleAsync());
    }

    [Fact]
    public async Task Popups_WithoutUrl()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var popup = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync("() => { window.open(); }"));

        Assert.Equal("about:blank", popup.Url);
        Assert.Equal(2, await popup.EvaluateAsync<int>("() => 1 + 1"));
    }

    [Fact]
    public async Task Frames()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<iframe name=child srcdoc='<button onclick=\"this.textContent = 1\">inner</button>'></iframe>");
        await page.WaitForFunctionAsync("() => document.querySelector('iframe').contentDocument?.querySelector('button')");

        await page.FrameLocator("iframe").GetByRole(AriaRole.Button).ClickAsync();

        Assert.Equal(2, page.Frames.Count);
        Assert.Equal("1", await page.FrameLocator("iframe").Locator("button").TextContentAsync());
        Assert.NotNull(page.Frame("child"));
    }

    [Fact]
    public async Task Routes_FulfillAbortAndContinue()
    {
        fixture.AddResource("api/data", request => new DocumentResource("{\"value\":\"original\"}") { Headers = [new("Content-Type", "application/json"), new("X-Echo", request.Headers.FirstOrDefault(h => h.Key == "X-Test").Value ?? "")] });
        var page1 = fixture.AddPage("routes.html", "<title>Routes</title>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.GotoAsync(page1);

        await page.RouteAsync("**/api/fulfilled", route => route.FulfillAsync(new() { Status = 201, Body = "fulfilled", ContentType = "text/plain" }));
        await page.RouteAsync("**/api/aborted", route => route.AbortAsync());
        await page.RouteAsync("**/api/data", route => route.ContinueAsync(new() { Headers = new Dictionary<string, string>(route.Request.Headers) { ["X-Test"] = "added" } }));

        var fulfilled = await page.EvaluateAsync<string>("() => fetch('/api/fulfilled').then(r => r.status + ':' + r.statusText.length + ':' + r.headers.get('content-type')).then(s => s)");
        var fulfilledText = await page.EvaluateAsync<string>("() => fetch('/api/fulfilled').then(r => r.text())");
        var aborted = await page.EvaluateAsync<string>("() => fetch('/api/aborted').then(() => 'ok', e => 'failed')");
        var echoed = await page.EvaluateAsync<string>("() => fetch('/api/data').then(r => r.headers.get('X-Echo'))");

        Assert.StartsWith("201:", fulfilled, StringComparison.Ordinal);
        Assert.Equal("fulfilled", fulfilledText);
        Assert.Equal("failed", aborted);
        Assert.Equal("added", echoed);
    }

    [Fact]
    public async Task RequestAndResponseEvents()
    {
        fixture.AddResource("events/data.json", _ => new DocumentResource("{\"a\":1}") { Headers = [new("Content-Type", "application/json")] });
        var url = fixture.AddPage("events.html", "<title>Events</title>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        var requests = new List<string>();
        page.Request += (_, request) => requests.Add(request.Method + " " + request.Url);
        await page.GotoAsync(url);

        var response = await page.RunAndWaitForResponseAsync(() => page.EvaluateAsync("() => fetch('/events/data.json', { method: 'POST', body: 'payload' })"), "**/data.json");

        Assert.Contains("GET " + url, requests);
        Assert.Equal(200, response.Status);
        Assert.Equal("POST", response.Request.Method);
        Assert.Equal("payload", response.Request.PostData);
        Assert.Equal(1, (await response.JsonAsync())?.GetProperty("a").GetInt32());
    }

    [Fact]
    public async Task Cookies_AndStorageState()
    {
        // Playwright enumerates the keys of localStorage (Object.keys): a key named as a method of Storage (e.g. "key") is not enumerated
        var url = fixture.AddPage("cookies.html", "<script>document.cookie = 'fromPage=1'; localStorage.setItem('token', 'value');</script>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        await context.AddCookiesAsync([new Cookie { Name = "added", Value = "yes", Url = "https://test.local/" }]);
        await page.GotoAsync(url);
        var cookies = await context.CookiesAsync();
        var documentCookie = await page.EvaluateAsync<string>("() => document.cookie");
        var state = await context.StorageStateAsync();

        Assert.Contains(cookies, c => c.Name == "added" && c.Value == "yes");
        Assert.Contains(cookies, c => c.Name == "fromPage" && c.Value == "1");
        Assert.Contains("added=yes", documentCookie, StringComparison.Ordinal);
        Assert.Contains("\"token\"", state, StringComparison.Ordinal);

        await context.ClearCookiesAsync();
        Assert.Empty(await context.CookiesAsync());

        await using var restored = await fixture.Browser.NewContextAsync(new() { StorageState = state });
        var restoredPage = await restored.NewPageAsync();
        await restoredPage.GotoAsync(url);
        Assert.Equal("value", await restoredPage.EvaluateAsync<string>("() => localStorage.getItem('token')"));
    }

    [Fact]
    public async Task Emulation_ViewportMediaLocaleAndUserAgent()
    {
        var (context, page) = await fixture.NewPageAsync(new() { Locale = "fr-FR", UserAgent = "TestAgent/1.0", ViewportSize = new() { Width = 640, Height = 480 } });
        await using var _ = context;

        Assert.Equal(640, await page.EvaluateAsync<int>("() => innerWidth"));
        Assert.Equal("fr-FR", await page.EvaluateAsync<string>("() => navigator.language"));
        Assert.Equal("TestAgent/1.0", await page.EvaluateAsync<string>("() => navigator.userAgent"));

        await page.SetViewportSizeAsync(800, 600);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });

        Assert.Equal(800, await page.EvaluateAsync<int>("() => innerWidth"));
        Assert.True(await page.EvaluateAsync<bool>("() => matchMedia('(prefers-color-scheme: dark)').matches"));
        Assert.Equal(800, page.ViewportSize?.Width);
    }

    [Fact]
    public async Task InitScriptsAndBindings()
    {
        var url = fixture.AddPage("init.html", "<script>document.title = String(window.injected);</script>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await context.AddInitScriptAsync("window.injected = 42;");
        await page.ExposeFunctionAsync("add", (int a, int b) => a + b);

        await page.GotoAsync(url);

        Assert.Equal("42", await page.TitleAsync());
        Assert.Equal(5, await page.EvaluateAsync<int>("() => window.add(2, 3)"));
    }

    [Fact]
    public async Task ExtraHttpHeaders()
    {
        var url = fixture.AddPage("headers.html", "<p>headers</p>");
        var (context, page) = await fixture.NewPageAsync(new() { ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Extra"] = "value" } });
        await using var _ = context;

        await page.GotoAsync(url);

        Assert.Contains(fixture.Requests, request => request.Url == url && request.Headers.Any(header => header.Key == "X-Extra" && header.Value == "value"));
    }

    [Fact]
    public async Task Server_DisposeAsync_ClosesTheConnectedClients()
    {
        var server = new DevToolsServer();
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(server.WebSocketEndpoint, TestContext.Current.CancellationToken);

        // The client is still connected: disposing the server closes its connection
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Server_JsonVersionEndpoint()
    {
        using var httpClient = new HttpClient();

        using var document = System.Text.Json.JsonDocument.Parse(await httpClient.GetStringAsync(new Uri(fixture.Server.HttpEndpoint, "json/version"), TestContext.Current.CancellationToken));

        Assert.Equal(fixture.Server.WebSocketEndpoint.ToString(), document.RootElement.GetProperty("webSocketDebuggerUrl").GetString());
        Assert.StartsWith("HeadlessChrome/", document.RootElement.GetProperty("Browser").GetString(), StringComparison.Ordinal);
        Assert.Equal("[]", await httpClient.GetStringAsync(new Uri(fixture.Server.HttpEndpoint, "json/list"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Server_RejectsTheHostsThatAreNotIPAddresses()
    {
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(fixture.Server.HttpEndpoint, "json/version"));
        request.Headers.Host = "attacker.example";

        using var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Server_RejectsTheUnknownWebSocketPaths()
    {
        using var client = new ClientWebSocket();
        var uri = new UriBuilder(fixture.Server.WebSocketEndpoint) { Path = "/devtools/browser/unknown" }.Uri;

        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync(uri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Protocol_CommandsAndErrors()
    {
        using var client = await CdpClient.ConnectAsync(fixture.Server.WebSocketEndpoint);

        var version = await client.SendAsync("""{"id":1,"method":"Browser.getVersion"}""");
        var unknown = await client.SendAsync("""{"id":2,"method":"Unknown.command"}""");
        var invalid = await client.SendAsync("""{"method":"Browser.getVersion"}""");
        var badSession = await client.SendAsync("""{"id":3,"method":"Runtime.evaluate","sessionId":"none"}""");

        Assert.StartsWith("HeadlessChrome/", version.GetProperty("result").GetProperty("product").GetString(), StringComparison.Ordinal);
        Assert.Equal(-32601, unknown.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32600, invalid.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(3, badSession.GetProperty("id").GetInt32());
        Assert.Equal("Session with given id not found.", badSession.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Protocol_AttachToTargetWithoutAutoAttach()
    {
        // As Puppeteer: the targets are created, then attached (they run at once, nobody waits for them)
        using var client = await CdpClient.ConnectAsync(fixture.Server.WebSocketEndpoint);

        var created = await client.SendAsync("""{"id":1,"method":"Target.createTarget","params":{"url":"about:blank"}}""");
        var targetId = created.GetProperty("result").GetProperty("targetId").GetString();
        var attached = await client.SendAsync("{\"id\":2,\"method\":\"Target.attachToTarget\",\"params\":{\"targetId\":\"" + targetId + "\",\"flatten\":true}}");
        var sessionId = attached.GetProperty("result").GetProperty("sessionId").GetString();
        var evaluated = await client.SendAsync("{\"id\":3,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"[1, 2].map(x => x * 21)\",\"returnByValue\":true},\"sessionId\":\"" + sessionId + "\"}");
        var targets = await client.SendAsync("""{"id":4,"method":"Target.getTargets"}""");

        Assert.Equal("[21,42]", evaluated.GetProperty("result").GetProperty("result").GetProperty("value").GetRawText());
        Assert.Contains(targets.GetProperty("result").GetProperty("targetInfos").EnumerateArray(), target => target.GetProperty("targetId").GetString() == targetId && target.GetProperty("type").GetString() == "page");
    }

    [Fact]
    public async Task Protocol_PausedTargetNavigatesOnceResumed()
    {
        // A command can create the browsing context of a paused target: the target still navigates to its URL when it is resumed
        var url = fixture.AddPage("paused.html", "<title>Paused</title>");
        using var client = await CdpClient.ConnectAsync(fixture.Server.WebSocketEndpoint);
        await client.SendAsync("""{"id":1,"method":"Target.setAutoAttach","params":{"autoAttach":true,"waitForDebuggerOnStart":true,"flatten":true}}""");

        await client.SendAsync("{\"id\":2,\"method\":\"Target.createTarget\",\"params\":{\"url\":\"" + url + "\"}}");
        var attached = client.Events.Single(e => e.GetProperty("method").GetString() == "Target.attachedToTarget").GetProperty("params");
        var sessionId = attached.GetProperty("sessionId").GetString();
        await client.SendAsync("{\"id\":3,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"1\"},\"sessionId\":\"" + sessionId + "\"}");
        await client.SendAsync("{\"id\":4,\"method\":\"Runtime.runIfWaitingForDebugger\",\"sessionId\":\"" + sessionId + "\"}");

        string? title = null;
        for (var i = 0; i < 100 && title != "Paused"; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            var evaluated = await client.SendAsync("{\"id\":5,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"document.title\",\"returnByValue\":true},\"sessionId\":\"" + sessionId + "\"}");
            title = evaluated.GetProperty("result").GetProperty("result").TryGetProperty("value", out var value) ? value.GetString() : null;
        }

        Assert.Equal("Paused", title);
        Assert.True(attached.GetProperty("waitingForDebugger").GetBoolean());
    }

    [Fact]
    public async Task Protocol_PropertiesAreReleasedWithTheirGroup()
    {
        using var client = await CdpClient.ConnectAsync(fixture.Server.WebSocketEndpoint);
        var created = await client.SendAsync("""{"id":1,"method":"Target.createTarget","params":{"url":"about:blank"}}""");
        var targetId = created.GetProperty("result").GetProperty("targetId").GetString();
        var sessionId = (await client.SendAsync("{\"id\":2,\"method\":\"Target.attachToTarget\",\"params\":{\"targetId\":\"" + targetId + "\",\"flatten\":true}}")).GetProperty("result").GetProperty("sessionId").GetString();

        var evaluated = await client.SendAsync("{\"id\":3,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"({ inner: { a: 1 } })\",\"objectGroup\":\"inspection\"},\"sessionId\":\"" + sessionId + "\"}");
        var objectId = evaluated.GetProperty("result").GetProperty("result").GetProperty("objectId").GetString();
        var properties = await client.SendAsync("{\"id\":4,\"method\":\"Runtime.getProperties\",\"params\":{\"objectId\":\"" + objectId + "\",\"ownProperties\":true},\"sessionId\":\"" + sessionId + "\"}");
        var innerId = properties.GetProperty("result").GetProperty("result").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "inner").GetProperty("value").GetProperty("objectId").GetString();
        await client.SendAsync("{\"id\":5,\"method\":\"Runtime.releaseObjectGroup\",\"params\":{\"objectGroup\":\"inspection\"},\"sessionId\":\"" + sessionId + "\"}");
        var released = await client.SendAsync("{\"id\":6,\"method\":\"Runtime.getProperties\",\"params\":{\"objectId\":\"" + innerId + "\"},\"sessionId\":\"" + sessionId + "\"}");

        Assert.Equal("Could not find object with given id", released.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Protocol_NavigationOfThePageAfterAFragmentNavigationIsReported()
    {
        var url = fixture.AddPage("fragment.html", "<p>fragment</p>");
        var next = fixture.AddPage("fragment-next.html", "<p>next</p>");
        using var client = await CdpClient.ConnectAsync(fixture.Server.WebSocketEndpoint);
        var created = await client.SendAsync("""{"id":1,"method":"Target.createTarget","params":{"url":"about:blank"}}""");
        var targetId = created.GetProperty("result").GetProperty("targetId").GetString();
        var sessionId = (await client.SendAsync("{\"id\":2,\"method\":\"Target.attachToTarget\",\"params\":{\"targetId\":\"" + targetId + "\",\"flatten\":true}}")).GetProperty("result").GetProperty("sessionId").GetString();
        await client.SendAsync("{\"id\":3,\"method\":\"Page.enable\",\"sessionId\":\"" + sessionId + "\"}");

        await client.SendAsync("{\"id\":4,\"method\":\"Page.navigate\",\"params\":{\"url\":\"" + url + "\"},\"sessionId\":\"" + sessionId + "\"}");
        await client.SendAsync("{\"id\":5,\"method\":\"Page.navigate\",\"params\":{\"url\":\"" + url + "#section\"},\"sessionId\":\"" + sessionId + "\"}");
        await client.SendAsync("{\"id\":6,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"location.href = '" + next + "'\"},\"sessionId\":\"" + sessionId + "\"}");
        for (var i = 0; i < 100 && !client.Events.Exists(IsRequested); i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            await client.SendAsync("{\"id\":7,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"1\"},\"sessionId\":\"" + sessionId + "\"}");
        }

        Assert.Contains(client.Events, IsRequested);

        bool IsRequested(System.Text.Json.JsonElement e) => e.GetProperty("method").GetString() == "Page.frameRequestedNavigation" && e.GetProperty("params").GetProperty("url").GetString() == next;
    }

    [Fact]
    public async Task ConnectOverCDP_WebSocketEndpoint()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(fixture.Server.WebSocketEndpoint.ToString());
        await using var context = await browser.NewContextAsync(new() { Locale = "fr-FR", TimezoneId = "Europe/Paris", UserAgent = "Custom agent" });
        var page = await context.NewPageAsync();

        Assert.Equal("fr-FR", await page.EvaluateAsync<string>("() => navigator.language"));
        Assert.Equal("Europe/Paris", await page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone"));
        Assert.Equal("Custom agent", await page.EvaluateAsync<string>("() => navigator.userAgent"));
    }

    [Fact]
    public async Task DriverFeatures_TracingClockLocatorHandlersAndAriaSnapshots()
    {
        // These features run in the driver of Playwright: they work as with Chromium
        var url = fixture.AddPage("driver.html", "<h1>Title</h1><button>Go</button><div id=overlay hidden><button onclick='this.parentElement.hidden = true'>Close</button></div><script>setTimeout(() => document.getElementById('overlay').hidden = false, 50)</script>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        await page.Clock.InstallAsync(new() { TimeString = "2024-02-02T08:00:00Z" });
        await page.AddLocatorHandlerAsync(page.Locator("#overlay"), overlay => overlay.Locator("button").ClickAsync());

        await page.GotoAsync(url);
        await page.Clock.RunForAsync(100);
        await page.GetByRole(AriaRole.Button, new() { Name = "Go" }).ClickAsync();
        var tracePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await context.Tracing.StopAsync(new() { Path = tracePath });

        try
        {
            Assert.Equal(2024, await page.EvaluateAsync<int>("() => new Date().getUTCFullYear()"));
            await Expect(page.Locator("body")).ToMatchAriaSnapshotAsync("""
                - heading "Title" [level=1]
                - button "Go"
                """);
            Assert.True(new FileInfo(tracePath).Length > 0);
        }
        finally
        {
            File.Delete(tracePath);
        }
    }

    [Fact]
    public async Task DriverFeatures_HarRecording()
    {
        var url = fixture.AddPage("har.html", "<p>har</p>");
        var harPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".har");
        try
        {
            var context = await fixture.Browser.NewContextAsync(new() { RecordHarPath = harPath });
            var page = await context.NewPageAsync();
            await page.GotoAsync(url);
            await context.CloseAsync();

            Assert.Contains(url, await File.ReadAllTextAsync(harPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(harPath);
        }
    }

    [Theory]
    [InlineData("*", "https://example.com/", true)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("https://example.com/*", "https://example.com/a/b", true)]
    [InlineData("https://example.com/*", "http://example.com/a", false)]
    [InlineData("*.png", "https://example.com/image.png", true)]
    [InlineData("*.png", "https://example.com/image.png?x=1", false)]
    [InlineData("https://example.com/?", "https://example.com/a", true)]
    [InlineData("https://example.com/?", "https://example.com/", false)]
    [InlineData("*\\*", "a*", true)]
    [InlineData("*\\*", "ab", false)]
    [InlineData("a\\?b", "a?b", true)]
    [InlineData("a\\?b", "axb", false)]
    [InlineData("a\\", "a\\", true)]
    [InlineData("**a**b**", "xxaxxbxx", true)]
    public void UrlGlob_IsMatch(string pattern, string url, bool expected)
    {
        Assert.Equal(expected, Network.UrlGlob.IsMatch(pattern, url));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("127.0.0.1:9222", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST:80", true)]
    [InlineData("[::1]:9222", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("example.com", false)]
    [InlineData("example.com:9222", false)]
    [InlineData("[example.com]", false)]
    [InlineData("[::1", false)]
    [InlineData("localhost.example.com", false)]
    public void Server_IsAllowedHost(string? host, bool expected)
    {
        Assert.Equal(expected, DevToolsServer.IsAllowedHost(host));
    }

    [Theory]
    [InlineData("""{"id":1,"method":"Browser.getVersion"}""", true, 0)]
    [InlineData("""{"id":2,"method":"Runtime.evaluate","params":{"expression":"1"},"sessionId":"A"}""", true, 0)]
    [InlineData("""{"id":3,"method":"Page.enable","params":null}""", true, 0)]
    [InlineData("""{"method":"Browser.getVersion"}""", false, -32600)]
    [InlineData("""{"id":1.5,"method":"Browser.getVersion"}""", false, -32600)]
    [InlineData("""{"id":1}""", false, -32600)]
    [InlineData("""{"id":1,"method":"A","sessionId":1}""", false, -32600)]
    [InlineData("""{"id":1,"method":"A","params":[]}""", false, -32600)]
    [InlineData("""[1]""", false, -32700)]
    [InlineData("""{"id":""", false, -32700)]
    public void Protocol_ParseMessage(string message, bool isValid, int errorCode)
    {
        var result = Protocol.CdpMessage.TryParse(Encoding.UTF8.GetBytes(message), out var parsed, out _, out var code, out _);

        Assert.Equal(isValid, result);
        Assert.Equal(errorCode, code);
        if (isValid)
        {
            Assert.NotEmpty(parsed.Method);
        }
    }

    [Fact]
    public void Protocol_ParseMessage_InvalidUtf8()
    {
        byte[] message = [.. "{\"id\":3,\"method\":\"Page.ena"u8, 0xEB, .. "ble\"}"u8];

        var result = Protocol.CdpMessage.TryParse(message, out _, out _, out var code, out _);

        Assert.False(result);
        Assert.Equal(-32700, code);
    }

    [Theory]
    [InlineData("GET /json/version HTTP/1.1\r\nHost: localhost\r\n\r\n", "GET", "/json/version")]
    [InlineData("GET /json/version?x=1 HTTP/1.1\r\n\r\n", "GET", "/json/version")]
    [InlineData(" sT /\r\n\r\n", null, null)]
    [InlineData("GET  HTTP/1.1\r\n\r\n", null, null)]
    [InlineData("GET /\r\n\r\n", null, null)]
    public async Task Server_ParseHttpRequest(string text, string? method, string? path)
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(text));

        var request = await Transport.HttpRequest.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal(method, request?.Method);
        Assert.Equal(path, request?.Path);
    }

    /// <summary>A minimal client of the protocol: it sends a message and returns the response of its id (or the error without id).</summary>
    private sealed class CdpClient : IDisposable
    {
        private readonly ClientWebSocket _webSocket = new();

        /// <summary>Gets the events received so far.</summary>
        public List<System.Text.Json.JsonElement> Events { get; } = [];

        public static async Task<CdpClient> ConnectAsync(Uri endpoint)
        {
            var client = new CdpClient();
            await client._webSocket.ConnectAsync(endpoint, TestContext.Current.CancellationToken);
            return client;
        }

        public async Task<System.Text.Json.JsonElement> SendAsync(string message)
        {
            using var request = System.Text.Json.JsonDocument.Parse(message);
            int? id = request.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : null;
            await _webSocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, TestContext.Current.CancellationToken);
            var buffer = new byte[1024 * 1024];
            while (true)
            {
                var length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer.AsMemory(length), TestContext.Current.CancellationToken);
                    length += result.Count;
                }
                while (!result.EndOfMessage);

                using var response = System.Text.Json.JsonDocument.Parse(buffer.AsMemory(0, length));
                var root = response.RootElement;
                var responseId = root.TryGetProperty("id", out var responseIdElement) ? responseIdElement.GetInt32() : (int?)null;
                if (root.TryGetProperty("method", out _))
                {
                    Events.Add(root.Clone());
                    continue;
                }

                if (responseId == id)
                    return root.Clone();
            }
        }

        public void Dispose() => _webSocket.Dispose();
    }

    [Fact]
    public async Task EngineThread_ContinuationsOfTheTasksOfTheEventLoopRunOnTheThread()
    {
        // The library completes its asynchronous operations (e.g. the load of a document) from a task of the event loop, then
        // continues with ConfigureAwait(false): the continuation must run on the thread of the event loop, not on the thread pool
        using var engine = new Engine.EngineThread("test");
        var (threadId, eventLoop) = await engine.InvokeAsync(() =>
        {
            var browsingContext = new BrowsingContext(new BrowsingContextOptions());
            engine.AddEventLoop(browsingContext.EventLoop);
            return (Environment.CurrentManagedThreadId, browsingContext.EventLoop);
        });
        var completion = new TaskCompletionSource();
        var continuationThreadId = ContinueAsync(completion.Task);

        eventLoop.QueueTask(completion.SetResult);

        Assert.Equal(threadId, await continuationThreadId.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        static async Task<int> ContinueAsync(Task task)
        {
            await task.ConfigureAwait(false);
            return Environment.CurrentManagedThreadId;
        }
    }

    [Fact]
    public async Task AriaSnapshot()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<h1>Title</h1><button>Go</button>");

        var snapshot = await page.Locator("body").AriaSnapshotAsync();

        Assert.Contains("- heading \"Title\" [level=1]", snapshot, StringComparison.Ordinal);
        Assert.Contains("- button \"Go\"", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DragAndDrop()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("""
            <style>body { margin: 0 } div { width: 100px; height: 100px }</style>
            <div id=source draggable=true ondragstart="event.dataTransfer.setData('text/plain', 'moved')"></div>
            <div id=target ondragover="event.preventDefault()" ondrop="event.preventDefault(); document.title = event.dataTransfer.getData('text/plain')"></div>
            """);

        await page.DragAndDropAsync("#source", "#target");

        await Expect(page).ToHaveTitleAsync("moved");
    }

    [Fact]
    public async Task Tap_RequiresHasTouch()
    {
        var (context, page) = await fixture.NewPageAsync(new() { HasTouch = true });
        await using var _ = context;
        await page.SetContentAsync("<button style='width: 100px; height: 50px'>tap</button><script>document.querySelector('button').addEventListener('touchstart', () => document.title = 'touched');</script>");

        await page.Locator("button").TapAsync();

        Assert.Equal("touched", await page.TitleAsync());
    }

    [Fact]
    public async Task Page_CloseAndBeforeUnload()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<button>x</button><script>addEventListener('beforeunload', e => e.preventDefault());</script>");
        await page.ClickAsync("button");
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) =>
        {
            dialogs.Add(dialog.Type);
            await dialog.DismissAsync();
        };

        await page.CloseAsync(new() { RunBeforeUnload = true });
        await page.WaitForTimeoutAsync(100);
        Assert.False(page.IsClosed);
        Assert.Equal(["beforeunload"], dialogs);

        var closed = false;
        page.Close += (_, _) => closed = true;
        await page.CloseAsync();
        Assert.True(page.IsClosed);
        Assert.True(closed);
        Assert.Empty(context.Pages);
    }

    [Fact]
    public async Task WindowOpen_CreatesAPopupAndWindowCloseClosesIt()
    {
        var target = fixture.AddPage("window-open.html", "<title>Opened</title>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<p>opener</p>");

        var popup = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync($"() => {{ window.popup = window.open('{target}'); }}"));
        Assert.Equal(target, popup.Url); // the popup is reported once its initial navigation commits
        await popup.WaitForLoadStateAsync();
        Assert.Equal("Opened", await popup.TitleAsync());
        Assert.Same(page, await popup.OpenerAsync());

        var closed = new TaskCompletionSource();
        popup.Close += (_, _) => closed.TrySetResult();
        await page.EvaluateAsync("() => window.popup.close()");
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(popup.IsClosed);
    }

    [Fact]
    public async Task WindowOpen_WithoutUrlCreatesAnEmptyPopup()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<p>opener</p>");

        var popup = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync("() => { window.open(); }"));

        Assert.Equal("about:blank", popup.Url);
        Assert.Same(page, await popup.OpenerAsync());
        Assert.Equal(2, context.Pages.Count);
    }

    [Fact]
    public async Task Context_CloseClosesThePages()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<p>x</p>");

        await context.CloseAsync();

        Assert.True(page.IsClosed);
        await Assert.ThrowsAnyAsync<PlaywrightException>(() => page.EvaluateAsync("() => 1"));
    }

    [Fact]
    public async Task Offline()
    {
        var url = fixture.AddPage("offline.html", "<p>online</p>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.GotoAsync(url);

        await context.SetOfflineAsync(true);
        Assert.False(await page.EvaluateAsync<bool>("() => navigator.onLine"));
        var exception = await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoAsync(url));
        Assert.Contains("net::ERR_INTERNET_DISCONNECTED", exception.Message, StringComparison.Ordinal);

        await context.SetOfflineAsync(false);
        Assert.True(await page.EvaluateAsync<bool>("() => navigator.onLine"));
    }

    [Fact]
    public async Task HttpCredentials()
    {
        var url = fixture.AddResource("auth.html", request => request.Headers.Any(header => header.Key == "Authorization" && header.Value == "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass")))
            ? new DocumentResource("<title>Authorized</title>") { Headers = [new("Content-Type", "text/html")] }
            : new DocumentResource("denied") { StatusCode = 401, Headers = [new("WWW-Authenticate", "Basic realm=test")] });
        var (context, page) = await fixture.NewPageAsync(new() { HttpCredentials = new() { Username = "user", Password = "pass" } });
        await using var _ = context;

        var response = await page.GotoAsync(url);

        Assert.Equal(200, response?.Status);
        Assert.Equal("Authorized", await page.TitleAsync());
    }

    [Fact]
    public async Task Routes_FallbackToTheContextRoutes()
    {
        var url = fixture.AddPage("fallback.html", "<p>fallback</p>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await context.RouteAsync("**/fallback-data", route => route.FulfillAsync(new() { Body = "from context" }));
        await page.RouteAsync("**/fallback-data", route => route.FallbackAsync());
        await page.GotoAsync(url);

        var text = await page.EvaluateAsync<string>("() => fetch('/fallback-data').then(r => r.text())");

        Assert.Equal("from context", text);
    }

    [Fact]
    public async Task Redirects()
    {
        fixture.AddResource("redirect-from", _ => new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://test.local/redirect-to")] });
        var target = fixture.AddPage("redirect-to", "<title>Redirected</title>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var response = await page.GotoAsync("https://test.local/redirect-from");

        Assert.Equal(target, page.Url);
        Assert.Equal(200, response?.Status);
        Assert.Equal("https://test.local/redirect-from", response?.Request.RedirectedFrom?.Url);
    }

    [Fact]
    public async Task Frames_AttachAndDetach()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<p>x</p>");
        var attached = new List<string>();
        var detached = new List<string>();
        page.FrameAttached += (_, frame) => attached.Add(frame.Name);
        page.FrameDetached += (_, frame) => detached.Add(frame.Name);

        await page.EvaluateAsync("() => { const f = document.createElement('iframe'); f.name = 'dyn'; document.body.append(f); }");
        await page.WaitForTimeoutAsync(100);
        Assert.Equal(2, page.Frames.Count);
        await page.EvaluateAsync("() => document.querySelector('iframe').remove()");
        await page.WaitForTimeoutAsync(100);

        // As in Chromium, the name of a frame is known once its document is committed: it is empty when the frame is attached
        Assert.Single(attached);
        Assert.Equal(["dyn"], detached);
        Assert.Single(page.Frames);
    }

    [Fact]
    public async Task WaitForSelector_States()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<p id=a>a</p><script>setTimeout(() => document.getElementById('a').style.display = 'none', 100); setTimeout(() => document.getElementById('a').remove(), 200);</script>");

        await page.Locator("#a").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await page.Locator("#a").WaitForAsync(new() { State = WaitForSelectorState.Detached });
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => page.Locator("#never").WaitForAsync(new() { Timeout = 300 }));

        Assert.Contains("300ms", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElementHandle_Methods()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<style>body { margin: 0 }</style><div id=a style='width: 50px; height: 20px; margin-left: 10px'><span>inner</span></div><input id=b>");
        var element = (await page.QuerySelectorAsync("#a"))!;

        var box = await element.BoundingBoxAsync();
        var span = await element.QuerySelectorAsync("span");
        var spans = await element.QuerySelectorAllAsync("span");
        var input = (await page.QuerySelectorAsync("#b"))!;
        await input.FillAsync("typed");

        Assert.NotNull(box);
        Assert.Equal(10, box.X);
        Assert.Equal(50, box.Width);
        Assert.Equal("inner", await span!.TextContentAsync());
        Assert.Single(spans);
        Assert.True(await element.IsVisibleAsync());
        Assert.Equal("typed", await input.InputValueAsync());
        Assert.Equal("inner", await element.EvalOnSelectorAsync<string>("span", "e => e.textContent"));
        Assert.Equal(page.MainFrame, await element.OwnerFrameAsync());
    }

    [Fact]
    public async Task Request_Response()
    {
        fixture.AddResource("request-response.json", _ => new DocumentResource("[1,2]") { Headers = [new("Content-Type", "application/json"), new("X-Custom", "yes")] });
        var url = fixture.AddPage("request-response.html", "<p>x</p>");
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.GotoAsync(url);

        var request = await page.RunAndWaitForRequestAsync(() => page.EvaluateAsync("() => fetch('/request-response.json')"), "**/request-response.json");
        var response = await request.ResponseAsync();

        Assert.NotNull(response);
        Assert.Equal("yes", await response.HeaderValueAsync("x-custom"));
        Assert.Equal("[1,2]", await response.TextAsync());
        Assert.Equal("fetch", request.ResourceType);
        Assert.Equal(page.MainFrame, request.Frame);
    }

    [Fact]
    public async Task ConsoleMessage_Arguments()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;

        var message = await page.RunAndWaitForConsoleMessageAsync(() => page.EvaluateAsync("() => console.log('value', { a: 1 })"));

        Assert.StartsWith("value ", message.Text, StringComparison.Ordinal);
        Assert.Equal(2, message.Args.Count);
        Assert.Equal(1, await message.Args[1].EvaluateAsync<int>("o => o.a"));
    }

    [Fact]
    public async Task ContentSecurityPolicy_DoesNotBlockTheClients()
    {
        var url = fixture.AddResource("csp.html", _ => new DocumentResource("<p id=a>csp</p><button onclick=\"this.textContent = 'clicked'\">b</button>") { Headers = [new("Content-Type", "text/html"), new("Content-Security-Policy", "script-src 'none'")] });
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.GotoAsync(url);

        Assert.Equal(2, await page.EvaluateAsync<int>("() => 1 + 1"));
        Assert.Equal("csp", await page.Locator("#a").TextContentAsync());
        await Expect(page.Locator("#a")).ToHaveTextAsync("csp");
    }

    [Fact]
    public async Task FileChooser()
    {
        var (context, page) = await fixture.NewPageAsync();
        await using var _ = context;
        await page.SetContentAsync("<input type=file multiple onchange=\"document.title = this.files.length\">");

        var fileChooser = await page.RunAndWaitForFileChooserAsync(() => page.Locator("input").ClickAsync());
        await fileChooser.SetFilesAsync([new FilePayload { Name = "a.txt", MimeType = "text/plain", Buffer = [1] }, new FilePayload { Name = "b.txt", MimeType = "text/plain", Buffer = [2] }]);

        Assert.True(fileChooser.IsMultiple);
        await Expect(page).ToHaveTitleAsync("2");
    }

    [Fact]
    public async Task JavaScriptDisabled()
    {
        var url = fixture.AddPage("no-script.html", "<p id=a>static</p><script>document.getElementById('a').textContent = 'script';</script>");
        var (context, page) = await fixture.NewPageAsync(new() { JavaScriptEnabled = false });
        await using var _ = context;

        await page.GotoAsync(url);

        await Expect(page.Locator("#a")).ToHaveTextAsync("static");
        Assert.Equal("static", await page.Locator("#a").TextContentAsync());
    }
}
