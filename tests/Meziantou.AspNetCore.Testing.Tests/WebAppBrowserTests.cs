using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Testing.TestApp;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Wasm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static Meziantou.AspNetCore.Testing.Expectations;

namespace Meziantou.AspNetCore.Testing.Tests;

public sealed class WebAppBrowserTests
{
    [Fact]
    public async Task NavigateSubmitFormAndReadCookie()
    {
        #region WebAppBrowser_Usage
        // The application runs in memory: Program is the entry point of the application under test
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/");
        await Expect(page).ToHaveTitleAsync("Home");

        // Follow a link, fill a form and submit it: the POST, the redirect and the cookie go through the application
        await page.GetByRole("link", "Sign in").ClickAsync();
        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole("button", "Sign in").ClickAsync();

        await Expect(page).ToHaveUrlAsync("/profile");
        await Expect(page.Locator("#greeting")).ToHaveTextAsync("Hello Ada");
        #endregion

        Assert.Equal("http://localhost/profile", page.Url);
        await Expect(page).ToHaveUrlAsync(new Regex("/profile$", RegexOptions.NonBacktracking));
        await Expect(page).Not.ToHaveUrlAsync("/sign-in");
        await Expect(page).ToHaveTitleAsync(new Regex("^Prof", RegexOptions.NonBacktracking));
        var exception = await Assert.ThrowsAsync<ExpectationFailedException>(() => Expect(page).ToHaveTitleAsync("Home", TimeSpan.FromMilliseconds(50)));
        Assert.StartsWith("Expected the page to have the title \"Home\", but it is \"", exception.Message);

        await page.GoBackAsync();
        Assert.Equal("Sign in", page.Document.Title);

        // The pages of a browser share the cookies
        var otherPage = await browser.OpenAsync("/profile");
        Assert.Equal("Hello Ada", otherPage.Find("#greeting").TextContent);
    }

    [Fact]
    public async Task ReplaceTheServicesOfTheApplication()
    {
        #region WebAppBrowser_ReplaceServices
        await using var application = new WebApplicationFactory<Program>();
        await using var factory = application.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<GreetingService, FrenchGreetingService>()));
        await using var browser = WebAppBrowser.Create(factory.Server);

        // Sign in by setting the cookie of the application
        browser.Cookies.SetCookie("http://localhost/", "user=Ada");
        var page = await browser.OpenAsync("/profile");

        Assert.Equal("Bonjour Ada", page.Find("#greeting").TextContent);
        #endregion
    }

    private sealed class FrenchGreetingService : GreetingService
    {
        public override string Greet(string name) => "Bonjour " + name;
    }

    [Fact]
    public async Task HistoryActionsThatStayOnTheSameDocumentOrDoNothingComplete()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
        var page = await browser.OpenAsync("/");

        // There is no next entry: the action does nothing, and does not wait for a navigation
        await page.GoForwardAsync();
        Assert.Equal("http://localhost/", page.Url);

        // Two entries of the same document with the same URL
        var document = page.Document;
        page.BrowsingContext.Window.History.PushState("state", "");
        await page.GoBackAsync();
        Assert.Same(document, page.Document);
        await page.GoForwardAsync();
        Assert.Same(document, page.Document);
        Assert.Equal("http://localhost/", page.Url);

        // A navigation to another document still waits for it
        await page.GoToAsync("/sign-in");
        await page.GoBackAsync();
        await Expect(page).ToHaveTitleAsync("Home");
    }

    [Fact]
    public async Task ScriptsFetchFromTheApplication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/client");

        await page.Find("#load").ClickAsync();

        Assert.Equal("Count: 42", page.Find("output").TextContent);
        Assert.Empty(page.ScriptErrors);
    }

    [Fact]
    public async Task ContentSecurityPolicyViolations()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/secure");

        var violation = Assert.Single(page.ContentSecurityPolicyViolations);
        Assert.Equal("script-src-elem", violation.EffectiveDirective);
        Assert.Equal("inline", violation.BlockedUrl);
        Assert.Equal("Secure", page.Document.Title);
        Assert.Empty(page.ScriptErrors);

        var otherPage = await browser.OpenAsync("/");
        Assert.Empty(otherPage.ContentSecurityPolicyViolations);
    }

    [Fact]
    public async Task FakeClipboard_SuppliesWhatThePagesPasteAndReceivesWhatTheyCopy()
    {
        #region FakeClipboard_Usage
        var copied = new List<string?>();
        var clipboard = new FakeClipboard
        {
            // What the pages paste or read
            ProvideContent = () => [FakeClipboard.CreateTextItem("Pasted text")],

            // Called with what the pages copy or write
            ContentChanged = items => copied.Add(FakeClipboard.GetText(items)),
        };

        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
        {
            // The pages that use the same clipboard share it
            ConfigureBrowsingContext = options => options with { Clipboard = clipboard },
        });
        var page = await browser.OpenAsync("/secure");

        await page.Find("#copy").ClickAsync();
        await page.Find("#paste").ClickAsync();

        Assert.Equal(["Copied text"], copied);
        Assert.Equal("Pasted text", page.Find("output").TextContent);
        #endregion
    }

    [Fact]
    public async Task FakeClipboard_WithoutProviderReadsWhatWasWritten()
    {
        var clipboard = new FakeClipboard();
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
        {
            ConfigureBrowsingContext = options => options with { Clipboard = clipboard },
        });
        var page = await browser.OpenAsync("/secure");
        var otherPage = await browser.OpenAsync("/secure");

        await page.Find("#copy").ClickAsync();
        await otherPage.Find("#paste").ClickAsync();

        Assert.Equal("Copied text", clipboard.ReadText());
        Assert.Equal("Copied text", otherPage.Find("output").TextContent);
    }

    [Fact]
    public void FakeClipboard_ContentChangedRunsBeforeTheChangeEvents()
    {
        var content = new List<SystemClipboardItem>();
        var clipboard = new FakeClipboard
        {
            ProvideContent = () => content,
            ContentChanged = items => content = [.. items],
        };
        string? textSeenByListener = null;
        clipboard.Changed += (_, _) => textSeenByListener = clipboard.ReadText();

        clipboard.WriteText("New text");

        Assert.Equal("New text", textSeenByListener);
        Assert.Equal(1, clipboard.ChangeCount);
    }

    [Fact]
    public void FakeClipboard_NotifyContentChanged()
    {
        var text = "First";
        var clipboard = new FakeClipboard { ProvideContent = () => [FakeClipboard.CreateTextItem(text)] };
        var changes = 0;
        clipboard.Changed += (_, _) => changes++;
        var changeCount = clipboard.ChangeCount;

        text = "Second";
        clipboard.NotifyContentChanged();

        Assert.Equal("Second", clipboard.ReadText());
        Assert.Equal(1, changes);
        Assert.NotEqual(changeCount, clipboard.ChangeCount);
    }

    [Fact]
    public async Task PagesHaveTheirOwnClipboardByDefault()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/secure");
        var otherPage = await browser.OpenAsync("/secure");

        await page.Find("#copy").ClickAsync();
        await otherPage.Find("#paste").ClickAsync();
        await page.Find("#paste").ClickAsync();

        Assert.Null(page.BrowsingContext.Options.Clipboard);
        Assert.Equal("", otherPage.Find("output").TextContent);
        Assert.Equal("Copied text", page.Find("output").TextContent);
    }

    [Fact]
    public async Task Dialogs_QueuedAnswers()
    {
        #region DialogController_Usage
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // Answer the next confirm() with Cancel: the item is kept
        page.Dialogs.DismissNext();
        await page.GetByRole("button", "Delete").ClickAsync();
        await Expect(page.Locator("output")).ToHaveTextAsync("Kept");

        // Answer the next prompt() with a text
        page.Dialogs.AcceptNext("Invoice.pdf");
        await page.GetByRole("button", "Rename").ClickAsync();
        await Expect(page.GetByRole("listitem")).ToHaveTextAsync("Invoice.pdf");

        // Answer the next confirm() with OK, then the alert() that it shows: the answers are used in order
        page.Dialogs.AcceptNext();
        page.Dialogs.AcceptNext();
        await page.GetByRole("button", "Delete").ClickAsync();
        await Expect(page.GetByRole("listitem")).ToHaveCountAsync(0);

        // The controller records the dialogs and their answers
        var history = page.Dialogs.History;
        Assert.Equal(["Delete Report.pdf?", "New name", "Delete Invoice.pdf?", "Deleted"], history.Select(dialog => dialog.Message));
        Assert.Equal([UserPromptType.Confirm, UserPromptType.Prompt, UserPromptType.Confirm, UserPromptType.Alert], history.Select(dialog => dialog.Type));
        Assert.Equal([false, true, true, true], history.Select(dialog => dialog.Accepted));
        Assert.Equal("Invoice.pdf", history[1].Text);
        #endregion

        Assert.Equal("Report.pdf", history[1].DefaultValue);
        Assert.Null(history[0].Text);
        Assert.All(history, dialog => Assert.Same(page, dialog.Page));
        Assert.All(history, dialog => Assert.False(dialog.IsUnexpected));
        Assert.Equal(0, page.Dialogs.PendingCount);

        page.Dialogs.ClearHistory();
        Assert.Empty(page.Dialogs.History);
    }

    [Fact]
    public async Task Dialogs_HandleNextChecksTheDialog()
    {
        #region DialogController_HandleNext
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // Check the dialog, then answer it
        page.Dialogs.HandleNext(dialog =>
        {
            Assert.Equal(UserPromptType.Prompt, dialog.Type);
            Assert.Equal("Report.pdf", dialog.DefaultValue);
            dialog.Accept(dialog.DefaultValue.Replace(".pdf", ".txt", StringComparison.Ordinal));
        });
        await page.GetByRole("button", "Rename").ClickAsync();

        await Expect(page.GetByRole("listitem")).ToHaveTextAsync("Report.txt");
        #endregion
    }

    [Fact]
    public async Task Dialogs_HandleAll()
    {
        #region DialogController_HandleAll
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // Answer the dialogs that no queued handler answers
        page.Dialogs.HandleAll(dialog =>
        {
            if (dialog.Type is UserPromptType.Prompt)
            {
                dialog.Accept(dialog.DefaultValue.ToUpperInvariant());
            }
            else
            {
                dialog.Accept();
            }
        });

        await page.GetByRole("button", "Rename").ClickAsync();
        await Expect(page.GetByRole("listitem")).ToHaveTextAsync("REPORT.PDF");

        await page.GetByRole("button", "Delete").ClickAsync();
        await Expect(page.GetByRole("listitem")).ToHaveCountAsync(0);
        #endregion

        Assert.Equal(3, page.Dialogs.History.Count);

        // A queued handler comes first: stay on the page
        page.Dialogs.DismissNext();
        await page.GetByRole("link", "Home").ClickAsync();
        await Expect(page).ToHaveUrlAsync("/dialogs");
    }

    [Fact]
    public async Task Dialogs_StrictModeFailsTheActionThatShowsAnUnexpectedDialog()
    {
        #region DialogController_StrictMode
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // No answer: confirm() returns false, and the click fails
        var exception = await Assert.ThrowsAsync<UnexpectedDialogException>(() => page.GetByRole("button", "Delete").ClickAsync());

        Assert.Equal("Delete Report.pdf?", exception.Dialog?.Message);
        Assert.Equal("Kept", page.Find("output").TextContent);
        #endregion

        Assert.True(exception.Dialog?.IsUnexpected);
        Assert.False(exception.Dialog?.Accepted);

        // The failure is reported once
        await page.WaitForIdleAsync();
    }

    [Fact]
    public async Task Dialogs_StrictModeFailsAClickThatDoesNotWait()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // The confirm() of the click handler is synchronous: the click fails even when it does not wait afterward
        var exception = await Assert.ThrowsAsync<UnexpectedDialogException>(() => page.GetByRole("button", "Delete").ClickAsync(new ClickOptions { NoWaitAfter = true }));
        Assert.Equal("Delete Report.pdf?", exception.Dialog?.Message);

        // The failure is reported once
        await page.WaitForIdleAsync();
    }

    [Fact]
    public async Task Dialogs_AnExceptionOfAHandlerFailsTheAction()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        page.Dialogs.HandleNext(dialog => throw new InvalidOperationException("Unexpected message: " + dialog.Message));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetByRole("button", "Delete").ClickAsync());
        Assert.Equal("Unexpected message: Delete Report.pdf?", exception.Message);
        Assert.Equal("Kept", page.Find("output").TextContent);

        // A dialog is answered once, while its handler runs
        PageDialog? answered = null;
        page.Dialogs.HandleNext(dialog =>
        {
            answered = dialog;
            dialog.Dismiss();
            dialog.Accept();
        });
        var twice = await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetByRole("button", "Delete").ClickAsync());
        Assert.Equal("The dialog is already dismissed.", twice.Message);
        Assert.Throws<InvalidOperationException>(() => answered!.Dismiss());
        Assert.False(answered!.Accepted);
    }

    [Fact]
    public async Task Dialogs_LooseModeGivesTheAnswersOfABrowserWithoutDialogs()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");
        page.Dialogs.Mode = DialogMode.Loose;

        await page.GetByRole("button", "Rename").ClickAsync();
        await page.GetByRole("button", "Delete").ClickAsync();

        Assert.Equal("Report.pdf", page.Find("li").TextContent);
        Assert.Equal("Kept", page.Find("output").TextContent);
        Assert.Equal([false, false], page.Dialogs.History.Select(dialog => dialog.Accepted));
        Assert.All(page.Dialogs.History, dialog => Assert.True(dialog.IsUnexpected));

        // The beforeunload confirmation is accepted: the page leaves
        await page.GetByRole("link", "Home").ClickAsync();
        await Expect(page).ToHaveUrlAsync("/");
        Assert.Equal(UserPromptType.BeforeUnload, page.Dialogs.History[^1].Type);
        Assert.True(page.Dialogs.History[^1].Accepted);
    }

    [Fact]
    public async Task Dialogs_DismissTheBeforeUnloadConfirmationToStayOnThePage()
    {
        #region DialogController_BeforeUnload
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // The click gives the page the sticky activation that the confirmation requires
        page.Dialogs.DismissNext();
        await page.GetByRole("button", "Delete").ClickAsync();

        // Stay on the page that has unsaved changes
        page.Dialogs.DismissNext();
        await page.GetByRole("link", "Home").ClickAsync();
        await Expect(page).ToHaveUrlAsync("/dialogs");
        #endregion

        Assert.Equal(UserPromptType.BeforeUnload, page.Dialogs.History[^1].Type);
        Assert.False(page.Dialogs.History[^1].Accepted);
        Assert.Equal("", page.Dialogs.History[^1].Message);

        // Without answer, the strict mode fails the navigation
        await Assert.ThrowsAsync<UnexpectedDialogException>(() => page.GetByRole("link", "Home").ClickAsync());
        await Expect(page).ToHaveUrlAsync("/");
    }

    [Fact]
    public async Task Dialogs_WaitForDialog()
    {
        #region TestPage_WaitForDialog
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/dialogs");

        // The answer is set up before the dialog: the script waits for it
        page.Dialogs.AcceptNext();
        await page.GetByRole("button", "Save").ClickAsync(new ClickOptions { NoWaitAfter = true });

        // The alert() is shown by a timer: wait for it, and get it with its answer
        var dialog = await page.WaitForDialogAsync();
        Assert.Equal("Saved", dialog.Message);
        Assert.True(dialog.Accepted);
        #endregion

        await Assert.ThrowsAsync<TimeoutException>(() => page.WaitForDialogAsync(TimeSpan.FromMilliseconds(50)));

        // Each dialog is returned once, in order
        page.Dialogs.AcceptAll();
        await page.GetByRole("button", "Delete").ClickAsync();
        Assert.Equal("Delete Report.pdf?", (await page.WaitForDialogAsync()).Message);
        Assert.Equal("Deleted", (await page.WaitForDialogAsync()).Message);
    }

    [Fact]
    public async Task Dialogs_PagesShareTheControllerOfTheOptions()
    {
        #region WebAppBrowser_Dialogs
        // The pages of the browser share the controller: dismiss every dialog
        var dialogs = DialogController.DismissingAll();
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { Dialogs = dialogs });
        var page = await browser.OpenAsync("/dialogs");
        var otherPage = await browser.OpenAsync("/dialogs");

        await page.GetByRole("button", "Delete").ClickAsync();
        await otherPage.GetByRole("button", "Rename").ClickAsync();

        Assert.Same(dialogs, page.Dialogs);
        Assert.Equal([page, otherPage], dialogs.History.Select(dialog => dialog.Page));
        #endregion

        Assert.All(dialogs.History, dialog => Assert.False(dialog.Accepted));
        var accepting = DialogController.AcceptingAll();
        Assert.NotSame(accepting, DialogController.AcceptingAll());
        Assert.Equal(DialogMode.Strict, accepting.Mode);
    }

    [Fact]
    public async Task Dialogs_FallBackToTheHandlerOfTheBrowsingContextOptions()
    {
        var messages = new List<string>();
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
        {
            ConfigureBrowsingContext = options => options with
            {
                UserPromptHandler = (_, prompt) =>
                {
                    messages.Add(prompt.Message);
                    return UserPromptResult.Accept();
                },
            },
        });
        var page = await browser.OpenAsync("/dialogs");

        // The queued handler comes first, the handler of all the dialogs next, then the handler of the options
        page.Dialogs.DismissNext();
        await page.GetByRole("button", "Delete").ClickAsync();
        page.Dialogs.AcceptAll("From the page");
        await page.GetByRole("button", "Rename").ClickAsync();
        page.Dialogs.HandleAll(null);
        await page.GetByRole("button", "Delete").ClickAsync();

        Assert.Equal(["Delete From the page?", "Deleted"], messages);
        Assert.Equal(["Delete Report.pdf?", "New name", "Delete From the page?", "Deleted"], page.Dialogs.History.Select(dialog => dialog.Message));
        Assert.Equal([false, true, true, true], page.Dialogs.History.Select(dialog => dialog.Accepted));
        Assert.All(page.Dialogs.History, dialog => Assert.False(dialog.IsUnexpected));
    }

    [Fact]
    public async Task ConfigureTheBrowsingContextOfThePages()
    {
        #region WebAppBrowser_ConfigureBrowsingContext
        var reports = new List<ContentSecurityPolicyViolationReport>();
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
        {
            // Change any option of the browsing contexts of the pages
            ConfigureBrowsingContext = options => options with
            {
                UserAgent = "TestBrowser/1.0",
                ContentSecurityPolicyViolation = (_, report) => reports.Add(report),
            },
        });
        var page = await browser.OpenAsync("/secure");

        Assert.Equal("TestBrowser/1.0", page.BrowsingContext.Window.Navigator.UserAgent);

        // The page still records what it reports, and calls the callbacks of the options
        Assert.Single(page.ContentSecurityPolicyViolations);
        Assert.Single(reports);
        #endregion
    }

    [Fact]
    public async Task ConfigureBrowsingContextCannotReplaceTheLoader()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
        {
            ConfigureBrowsingContext = options => options with { DocumentLoader = TestDocumentLoaders.FromTestServer(factory.Server) },
        });

        var exception = Assert.Throws<InvalidOperationException>(browser.NewPage);
        Assert.Contains("cannot replace the DocumentLoader or the ScriptEngine", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeProviderIsTheClockOfTheDates()
    {
        #region WebAppBrowser_TimeProvider
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test"));
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { TimeProvider = clock });

        // The page writes new Date().toISOString() and new Date().getTimezoneOffset()
        var page = await browser.OpenAsync("/clock");

        Assert.Equal("2026-01-10T12:00:00.000Z -120", page.Find("output").TextContent);
        #endregion
        Assert.Empty(page.ScriptErrors);
    }

    [Fact]
    public async Task WebAssemblyRuntimeRunsTheModulesOfThePages()
    {
        #region WebAppBrowser_WebAssemblyRuntime
        // The runtime of the Meziantou.Framework.WebPlatform.Wasm package: the pages share it
        using var runtime = new WasmWebAssemblyRuntime();
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { WebAssemblyRuntime = runtime });

        // The page instantiates a WebAssembly module and calls its add function
        var page = await browser.OpenAsync("/wasm");

        await Expect(page.Locator("output")).ToHaveTextAsync("2 + 3 = 5");
        #endregion
        Assert.Same(runtime, page.BrowsingContext.Options.WebAssemblyRuntime);
        Assert.Empty(page.ScriptErrors);
    }

    [Fact]
    public async Task WebAssemblyIsNotDefinedWithoutRuntime()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server);
        var page = await browser.OpenAsync("/wasm");

        Assert.Equal("WebAssembly is not defined", page.Find("output").TextContent);
        Assert.Null(page.BrowsingContext.Options.WebAssemblyRuntime);
    }

    [Fact]
    public async Task PagesWithoutJavaScript()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { EnableJavaScript = false });
        var page = await browser.OpenAsync("/client");

        await page.Find("#load").ClickAsync();

        Assert.Equal("", page.Find("output").TextContent);
    }

    [Fact]
    public async Task SnapshotsOfThePage()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { Viewport = new ViewportOptions { Width = 320, Height = 200 } });
        var page = await browser.OpenAsync("/");

        Assert.Equal("""
            <h1>Home</h1>
            <a href="/sign-in">Sign in</a>
            """, page.GetMarkup());
        Assert.Equal("""
            - heading "Home" [level=1]
            - link "Sign in":
              - /url: /sign-in
            """, page.GetAccessibilitySnapshot());
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], page.TakeScreenshotAsPng().Take(4));
    }

    [Fact]
    public async Task FromDirectories_ServesTheIndexFilesAndTheFallbackFile()
    {
        var directory = Directory.CreateTempSubdirectory("meziantou-aspnetcore-testing-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "index.html"), "<title>Index</title>");
            File.WriteAllText(Path.Combine(directory.FullName, "app.css"), "body {}");
            Directory.CreateDirectory(Path.Combine(directory.FullName, "docs"));
            File.WriteAllText(Path.Combine(directory.FullName, "docs", "index.html"), "<title>Docs</title>");
            var origin = new Uri("https://localhost/");
            var loader = TestDocumentLoaders.FromDirectories(origin, [new("/", directory.FullName)], fallbackPath: "/index.html");
            var loaderWithoutFallback = TestDocumentLoaders.FromDirectories(origin, [new("/", directory.FullName)]);

            Assert.Equal("<title>Index</title>", await LoadAsync(loader, "https://localhost/"));
            Assert.Equal("<title>Docs</title>", await LoadAsync(loader, "https://localhost/docs/"));
            Assert.Equal("body {}", await LoadAsync(loader, "https://localhost/app.css"));
            Assert.Equal("<title>Index</title>", await LoadAsync(loader, "https://localhost/accounts/42?tab=1"));
            Assert.Equal("404", await LoadAsync(loader, "https://localhost/missing.css"));
            Assert.Null(await loader.LoadAsync(new DocumentRequest("https://example.com/accounts"), TestContext.Current.CancellationToken));
            Assert.Equal("<title>Index</title>", await LoadAsync(loaderWithoutFallback, "https://localhost/"));
            Assert.Equal("404", await LoadAsync(loaderWithoutFallback, "https://localhost/accounts/42"));

            // The URL of a folder without its trailing slash redirects to the folder, so that the relative URLs of its page
            // resolve in the folder
            var redirect = await loader.LoadAsync(new DocumentRequest("https://localhost/docs?a=1") { FollowRedirects = false }, TestContext.Current.CancellationToken);
            Assert.NotNull(redirect);
            Assert.Equal(301, redirect.StatusCode);
            Assert.Equal("https://localhost/docs/?a=1", Assert.Single(redirect.Headers, header => header.Key == "Location").Value);
            var followed = await loader.LoadAsync(new DocumentRequest("https://localhost/docs"), TestContext.Current.CancellationToken);
            Assert.NotNull(followed);
            Assert.Equal("https://localhost/docs/", followed.Url);
            Assert.Equal("<title>Docs</title>", Encoding.UTF8.GetString(followed.ContentBytes!.Value.Span));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        static async Task<string> LoadAsync(DocumentLoader loader, string url)
        {
            var resource = await loader.LoadAsync(new DocumentRequest(url), TestContext.Current.CancellationToken);
            Assert.NotNull(resource);
            return resource.StatusCode is 404 ? "404" : Encoding.UTF8.GetString(resource.ContentBytes!.Value.Span);
        }
    }

    [Fact]
    public async Task FromDirectories_RedirectsAFolderWithoutItsTrailingSlash()
    {
        var directory = Directory.CreateTempSubdirectory("meziantou-aspnetcore-testing-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "index.html"), "<script src=\"main.js\"></script>");
            File.WriteAllText(Path.Combine(directory.FullName, "main.js"), "document.title = 'loaded';");
            var origin = new Uri("https://localhost/");
            await using var browser = new WebAppBrowser(origin, TestDocumentLoaders.FromDirectories(origin, [new("/app", directory.FullName)]));

            var page = await browser.OpenAsync("/app");

            await Expect(page).ToHaveTitleAsync("loaded");
            Assert.Equal("https://localhost/app/", page.Url);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BlazorWebAssemblyApplication()
    {
        #region WebAppBrowser_BlazorWebAssembly
        // The wwwroot folder of the published application (dotnet publish)
        var wwwroot = typeof(WebAppBrowserTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "WasmAppPath").Value!;

        // The runtime of .NET runs in the page, on Meziantou.Framework.WasmEngine. Share the runtime between the tests (a
        // static field): it keeps the compiled modules, so the next tests do not compile the runtime of .NET again
        using var wasmRuntime = new WasmWebAssemblyRuntime();

        // The fallback file serves the routes of the application (/accounts/42), as MapFallbackToFile does
        var origin = new Uri("https://localhost/");
        var loader = TestDocumentLoaders.FromDirectories(origin, [new("/", wwwroot)], fallbackPath: "/index.html");
        await using var browser = new WebAppBrowser(origin, loader, new WebAppBrowserOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(30),
            WebAssemblyRuntime = wasmRuntime,
        });

        var page = await browser.OpenAsync("/accounts/42");
        await Expect(page.GetByRole("heading", "Account 42")).ToBeVisibleAsync();

        // The router of Blazor handles the navigation in the page
        await page.GetByRole("link", "Home").ClickAsync();
        await page.GetByRole("button", "Click me").ClickAsync();
        await Expect(page.GetByRole("status")).ToHaveTextAsync("Current count: 1");
        await Expect(page).ToHaveUrlAsync("/");
        #endregion

        Assert.Empty(page.ScriptErrors);
    }
}
