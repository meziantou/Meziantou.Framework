using System.Globalization;
using Meziantou.AspNetCore.Testing;
using Meziantou.AspNetCore.Testing.Components;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Xunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using static Meziantou.AspNetCore.Testing.Expectations;

namespace Meziantou.AspNetCore.Components.Tests;

public class LogViewerComponentTests
{
    [Fact]
    public async Task ReadmeExample_AnsiMessage()
    {
        #region LogEntry_AnsiMessage
        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            LogLevel = LogLevel.Information,
            Message = "\u001b[1;33mwarning\u001b[0m \u001b[38;2;100;200;50mcustom color\u001b[0m",
        };
        #endregion

        var html = await RenderViewerAsync(new { Entries = new[] { entry } });
        Assert.DoesNotContain("\u001b", html);
        Assert.Contains("custom color", html);
    }

    private static async Task<string> RenderAsync<TComponent>(object parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(EmptyServiceProvider.Instance, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(ToDictionary(parameters)));
            return output.ToHtmlString();
        });
    }

    private static Dictionary<string, object?> ToDictionary(object parameters)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in parameters.GetType().GetProperties())
        {
            result[property.Name] = property.GetValue(parameters);
        }

        return result;
    }


    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new EmptyServiceProvider();

        public object? GetService(Type serviceType) => null;
    }

    private static Task<string> RenderViewerAsync(object parameters) => RenderAsync<LogViewer>(parameters);

    private static Task<string> RenderDetailsAsync(object data, LogDetailsDisplayFormat format = LogDetailsDisplayFormat.Table)
        => RenderAsync<LogEntryDetails>(new { Data = data, Format = format });

    private static LogEntry Entry(string message, int offsetSeconds = 0) => new()
    {
        Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(offsetSeconds),
        LogLevel = LogLevel.Information,
        Message = message,
    };

    [Fact]
    public async Task LineNumbersAreAssignedWhenEntriesIsAComputedSequence()
    {
        // A projecting sequence yields new LogEntry instances on every enumeration. The component must
        // snapshot it, otherwise every per-entry lookup misses and line numbers render as 0.
        var source = new[] { "first", "second", "third" };
        var entries = source.Select(text => Entry(text));

        var html = await RenderViewerAsync(new { Entries = entries });

        Assert.Contains(">1</button>", html);
        Assert.Contains(">2</button>", html);
        Assert.Contains(">3</button>", html);
        Assert.DoesNotContain(">0</button>", html);
    }

    [Fact]
    public async Task LineNumbersCountCollapsedChildren()
    {
        var entries = new List<LogEntry>
        {
            new() { Message = "parent", Children = [Entry("child-a"), Entry("child-b")] },
            Entry("sibling"),
        };

        var html = await RenderViewerAsync(new { Entries = entries });

        // The collapsed children still consume line numbers 2 and 3, so the sibling is 4.
        Assert.Contains(">1</button>", html);
        Assert.Contains(">4</button>", html);
        Assert.DoesNotContain("child-a", html);
    }

    [Fact]
    public async Task ExpandedEntryRendersItsChildren()
    {
        var entries = new List<LogEntry>
        {
            new() { Message = "parent", Expanded = true, Children = [Entry("child-a")] },
        };

        var html = await RenderViewerAsync(new { Entries = entries });

        Assert.Contains("child-a", html);
        Assert.Contains("aria-expanded=\"true\"", html);
    }

    [Fact]
    public async Task ToggleAndLineNumberAreFocusableControls()
    {
        var entries = new List<LogEntry> { new() { Message = "parent", Children = [Entry("child")] } };

        var html = await RenderViewerAsync(new { Entries = entries });

        Assert.Contains("<button type=\"button\" class=\"log-linenumber\"", html);
        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("aria-label=\"Toggle child entries\"", html);
    }

    [Fact]
    [RunIf(TestGlobalizationMode.NotInvariant)]
    public async Task TimestampsDoNotDependOnTheCurrentCulture()
    {
        var entries = new List<LogEntry> { Entry("a"), Entry("b", offsetSeconds: 1) };

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var html = await RenderViewerAsync(new
            {
                Entries = entries,
                TimestampDisplayFormat = TimestampDisplayFormat.RelativeTimeStartingAtZero,
            });

            // "G" is culture-sensitive: fr-FR would use a comma as the decimal separator.
            Assert.Contains("0:00:00:01.0000000", html);
            Assert.DoesNotContain("0:00:00:01,0000000", html);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task RelativeTimeUsesTheConfiguredFormatForEveryRowIncludingTheFirst()
    {
        var entries = new List<LogEntry> { Entry("a"), Entry("b", offsetSeconds: 90) };

        var html = await RenderViewerAsync(new
        {
            Entries = entries,
            TimestampDisplayFormat = TimestampDisplayFormat.RelativeTimeStartingAtZero,
            TimeSpanStringFormat = @"mm\:ss",
        });

        Assert.Contains("00:00", html);
        Assert.Contains("01:30", html);
        // The first row used to bypass TimeSpanStringFormat and render TimeSpan.Zero as "00:00:00".
        Assert.DoesNotContain("00:00:00", html);
    }

    [Fact]
    public async Task RelativeTimeIsBasedOnTheEarliestTimestamp()
    {
        var entries = new List<LogEntry> { Entry("newest", offsetSeconds: 60), Entry("oldest") };

        var html = await RenderViewerAsync(new
        {
            Entries = entries,
            TimestampDisplayFormat = TimestampDisplayFormat.RelativeTimeStartingAtZero,
        });

        // "G" includes the sign, so a baseline taken from the first entry rather than the earliest would show up here.
        Assert.DoesNotContain("-0:00:01:00", html);
        Assert.Contains("0:00:01:00.0000000", html);
    }

    [Fact]
    public async Task MessageIsHtmlEncoded()
    {
        var entries = new List<LogEntry> { Entry("<script>alert(1)</script>") };

        var html = await RenderViewerAsync(new { Entries = entries });

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task ObjectWithAnIndexerDoesNotThrow()
    {
        var html = await RenderDetailsAsync(new WithIndexer());

        Assert.Contains("Name", html);
        Assert.Contains("value", html);
    }

    [Fact]
    public async Task ThrowingPropertyGetterIsRenderedAsAnError()
    {
        var html = await RenderDetailsAsync(new WithThrowingGetter());

        Assert.Contains("Ok", html);
        Assert.Contains("boom", html);
        Assert.Contains("value-error", html);
    }

    [Fact]
    public async Task CyclicGraphIsBoundedInTableFormat()
    {
        var a = new Node("a");
        var b = new Node("b") { Next = a };
        a.Next = b;

        var html = await RenderDetailsAsync(a);

        Assert.Contains("&#8230;", html);
    }

    [Fact]
    public async Task CyclicGraphDoesNotThrowInJsonFormat()
    {
        var a = new Node("a");
        var b = new Node("b") { Next = a };
        a.Next = b;

        var html = await RenderDetailsAsync(a, LogDetailsDisplayFormat.Json);

        // The JSON is HTML-encoded by the renderer. IgnoreCycles terminates the graph instead of throwing.
        Assert.Contains("&quot;Name&quot;", html);
        Assert.Contains("&quot;Next&quot;: null", html);
        Assert.DoesNotContain("object cycle", html);
    }

    [Fact]
    public async Task DictionaryWithNonObjectValuesRendersKeyValueRows()
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["User-Agent"] = "Mozilla/5.0",
        };

        var html = await RenderDetailsAsync(data);

        Assert.Contains("User-Agent", html);
        Assert.Contains("Mozilla/5.0", html);
        // Not rendered as an indexed list of KeyValuePair objects.
        Assert.DoesNotContain("[0]", html);
    }

    [Fact]
    public async Task DateOnlyAndTimeOnlyRenderAsSingleValues()
    {
        var html = await RenderDetailsAsync(new { Date = new DateOnly(2024, 1, 2), Time = new TimeOnly(3, 4, 5) });

        Assert.DoesNotContain("DayOfYear", html);
        Assert.DoesNotContain("Millisecond", html);
    }

    [Fact]
    public async Task StringDataIsNotRenderedCharacterByCharacter()
    {
        var html = await RenderDetailsAsync("hello");

        Assert.Contains("hello", html);
        Assert.DoesNotContain("[0]", html);
    }

    private sealed class WithIndexer
    {
        public string Name { get; } = "value";

        public string this[int index] => "indexed";
    }

    private sealed class WithThrowingGetter
    {
        // These must be instance properties: the component only reflects over public instance members.
#pragma warning disable CA1822 // Mark members as static
        public int Ok => 1;

        public string Bad => throw new InvalidOperationException("boom");
#pragma warning restore CA1822
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public Node? Next { get; set; }
    }

    private static ComponentTestHost CreateInteractiveHost() => new(new ComponentTestHostOptions
    {
        // The styles of the components apply to the whole page: the tests do not load the bundle of the CSS isolation
        StyleSheets =
        {
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "LogViewer.razor.css")),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "LogEntryDetails.razor.css")),
        },
    });

    private static LogEntry Entry(string message, LogLevel level = LogLevel.Information, params LogEntry[] children) => new()
    {
        Timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        LogLevel = level,
        Message = message,
        Children = children.Length > 0 ? children : null,
    };

    private static string[] GetSelectedLineNumbers<TComponent>(RenderedComponent<TComponent> viewer)
        where TComponent : IComponent
        => [.. viewer.FindAll(".log-entry.selected .log-linenumber").Select(button => button.TextContent ?? "")];

    [Fact]
    public async Task Interactive_ClickingALineNumberSelectsTheEntry()
    {
        await using var host = CreateInteractiveHost();
        var viewer = await host.RenderAsync<LogViewer>(parameters => parameters.Add(c => c.Entries, [Entry("first"), Entry("second"), Entry("third")]));

        await viewer.GetByRole("button", "1").ClickAsync();
        Assert.Equal(["1"], GetSelectedLineNumbers(viewer));

        await viewer.GetByRole("button", "2").ClickAsync();
        Assert.Equal(["2"], GetSelectedLineNumbers(viewer));

        // Ctrl (or Cmd) adds the entry to the selection, or removes it when it is selected
        await viewer.GetByRole("button", "3").ClickAsync(new ClickOptions { Modifiers = InputModifiers.Control });
        Assert.Equal(["2", "3"], GetSelectedLineNumbers(viewer));

        await viewer.GetByRole("button", "2").ClickAsync(new ClickOptions { Modifiers = InputModifiers.Control });
        Assert.Equal(["3"], GetSelectedLineNumbers(viewer));

        await viewer.GetByRole("button", "1").ClickAsync(new ClickOptions { Modifiers = InputModifiers.Meta });
        Assert.Equal(["1", "3"], GetSelectedLineNumbers(viewer));

        // The selected line number is highlighted
        var style = host.BrowsingContext.Window.GetComputedStyle(viewer.GetByRole("button", "1", new() { Exact = true }).GetElement());
        Assert.Equal("rgb(88, 166, 255)", style.Color);
        Assert.Equal("underline", style.GetPropertyValue("text-decoration-line"));
    }

    [Fact]
    public async Task Interactive_ToggleShowsAndHidesTheChildEntries()
    {
        await using var host = CreateInteractiveHost();
        var expandedParent = Entry("expanded parent", LogLevel.Information, Entry("expanded child"));
        expandedParent.Expanded = true;
        var viewer = await host.RenderAsync<LogViewer>(parameters => parameters.Add(c => c.Entries, [Entry("parent", LogLevel.Information, Entry("child 1"), Entry("child 2")), expandedParent]));

        Assert.Equal(["parent", "expanded parent", "expanded child"], viewer.FindAll(".log-message").Select(message => message.TextContent));
        var toggles = viewer.FindAll(".log-toggle-details");
        Assert.Equal(["false", "true"], toggles.Select(toggle => toggle.GetAttribute("aria-expanded")));
        Assert.Equal("Toggle child entries", toggles[0].ComputedName);

        await toggles[0].ClickAsync();
        Assert.Equal(["parent", "child 1", "child 2", "expanded parent", "expanded child"], viewer.FindAll(".log-message").Select(message => message.TextContent));
        Assert.Equal(["1", "2", "3", "4", "5"], viewer.FindAll(".log-linenumber").Select(button => button.TextContent));
        Assert.Equal("true", viewer.FindAll(".log-toggle-details")[0].GetAttribute("aria-expanded"));

        await viewer.FindAll(".log-toggle-details")[1].ClickAsync();
        await viewer.FindAll(".log-toggle-details")[0].ClickAsync();
        Assert.Equal(["parent", "expanded parent"], viewer.FindAll(".log-message").Select(message => message.TextContent));
    }

    [Fact]
    public async Task Interactive_ControlsWorkWithTheKeyboard()
    {
        await using var host = CreateInteractiveHost();
        var viewer = await host.RenderAsync<LogViewer>(parameters => parameters.Add(c => c.Entries, [Entry("parent", LogLevel.Information, Entry("child"))]));

        // Tab moves the focus to the line number, then to the toggle; Enter activates the focused button
        await host.Input.PressAsync("Tab");
        await Expect(viewer.GetByRole("button", "1", new() { Exact = true })).ToBeFocusedAsync();
        await host.Input.PressAsync("Enter");
        Assert.Equal(["1"], GetSelectedLineNumbers(viewer));

        await host.Input.PressAsync("Tab");
        await Expect(viewer.Locator(".log-toggle-details")).ToBeFocusedAsync();
        await host.Input.PressAsync("Enter");
        Assert.Equal(["parent", "child"], viewer.FindAll(".log-message").Select(message => message.TextContent));
    }

    [Fact]
    public async Task Interactive_StateIsKeptForTheSameEntriesAndResetForNewOnes()
    {
        await using var host = CreateInteractiveHost();
        var entries = new List<LogEntry> { Entry("first"), Entry("parent", LogLevel.Information, Entry("child")) };
        var viewer = await host.RenderAsync<LogViewer>(parameters => parameters.Add(c => c.Entries, entries));
        await viewer.GetByRole("button", "1").ClickAsync();
        await viewer.Find(".log-toggle-details").ClickAsync();

        // The same collection with a new entry: the selection and the expanded entries stay
        entries.Add(Entry("appended"));
        await viewer.SetParametersAsync(parameters => parameters.Add(c => c.Entries, entries));
        Assert.Equal(["1"], GetSelectedLineNumbers(viewer));
        Assert.Equal(["first", "parent", "child", "appended"], viewer.FindAll(".log-message").Select(message => message.TextContent));

        // Another collection: the state is reset
        await viewer.SetParametersAsync(parameters => parameters.Add(c => c.Entries, [.. entries]));
        Assert.Empty(GetSelectedLineNumbers(viewer));
        Assert.Equal(["first", "parent", "appended"], viewer.FindAll(".log-message").Select(message => message.TextContent));
    }

    [Fact]
    public async Task Interactive_DetailsSwitchBetweenTableAndJson()
    {
        await using var host = CreateInteractiveHost();
        var entry = new LogEntry { Timestamp = DateTimeOffset.UnixEpoch, Message = new { Name = "Ada", Level = LogLevel.Warning } };
        var viewer = await host.RenderAsync<LogViewer>(parameters => parameters.Add(c => c.Entries, [entry]));
        Assert.Equal(["Name:", "Level:"], viewer.FindAll("th").Select(header => header.TextContent));

        // The links do not navigate (href="#" with @onclick:preventDefault)
        await viewer.GetByRole("link", "json").ClickAsync();
        Assert.Equal("""
            {
              "Name": "Ada",
              "Level": "Warning"
            }
            """, viewer.Find("pre").TextContent);
        Assert.Equal("https://localhost/", host.Url);

        // A render of the parent keeps the format the user selected
        await viewer.SetParametersAsync(parameters => parameters.Add(c => c.ShowLineNumbers, false));
        Assert.NotNull(viewer.FindOrDefault("pre"));
        Assert.Empty(viewer.FindAll(".log-linenumber"));

        await viewer.GetByRole("link", "table").ClickAsync();
        Assert.Null(viewer.FindOrDefault("pre"));
        Assert.Equal(["Name:", "Level:"], viewer.FindAll("th").Select(header => header.TextContent));
    }

    [Fact]
    public async Task Interactive_LevelsAndLinksAreRendered()
    {
        await using var host = CreateInteractiveHost();
        var viewer = await host.RenderAsync<LogViewer>(parameters => parameters.Add(c => c.Entries,
        [
            Entry("Downloading https://example.com/file.zip"),
            Entry("Disk almost full", LogLevel.Warning),
            Entry("Request failed", LogLevel.Error),
        ]));

        var window = host.BrowsingContext.Window;
        Assert.Equal(["rgb(255, 255, 255)", "rgb(255, 165, 0)", "rgb(255, 0, 0)"], viewer.FindAll(".log-message").Select(message => window.GetComputedStyle(message).Color));

        var link = viewer.GetByRole("link", "https://example.com/file.zip");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], viewer.TakeScreenshotAsPng().Take(4));
    }
}
