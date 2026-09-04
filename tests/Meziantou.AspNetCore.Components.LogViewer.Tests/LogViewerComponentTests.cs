using System.Globalization;
using Meziantou.Xunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.AspNetCore.Components.Tests;

public class LogViewerComponentTests
{
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
}
