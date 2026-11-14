using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Testing.TestApp;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
}
