using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Changes a laid out document of about 5,000 elements (500 cards and a table of 100 rows, styled by a style sheet of class,
/// descendant, child, sibling and structural selectors) and updates its layout: the cost of the invalidation of the styles,
/// of the box tree and of the layout after a small change.
/// </summary>
[MemoryDiagnoser]
public class InvalidationBenchmarks
{
    private BrowsingContext? _context;
    private Document _document = null!;
    private Element _card = null!;
    private Element _tableBody = null!;
    private HtmlElement[] _cards = [];
    private ManualTimeProvider _clock = null!;
    private readonly AnimationBenchmarkScenario _animation = new(InvalidationDocumentKind.Animation);
    private readonly AnimationBenchmarkScenario _transitions = new(InvalidationDocumentKind.Transitions);
    private Element _transitionsCard = null!;

    [GlobalSetup]
    public void Setup()
    {
        _clock = new ManualTimeProvider();
        _context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = _clock });
        _document = _context.Navigate("https://example.com/", _ => InvalidationDocument.Create(InvalidationDocumentKind.Static));
        _card = _document.GetElementById("card250")!;
        _tableBody = _document.QuerySelector("tbody")!;
        _cards = [.. _document.QuerySelectorAll(".card").Cast<HtmlElement>()];
        _ = _document.GetLayoutEngine().EnsureLayout();
        _ = _animation.RunFrame();
        _ = _transitions.RunFrame();
        _transitionsCard = _transitions.Document.GetElementById("card250")!;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
        _animation.Dispose();
        _transitions.Dispose();
    }

    /// <summary>Toggles a class of one card, then updates the layout.</summary>
    [Benchmark]
    public object? ClassChange()
    {
        _card.ClassList.Toggle("highlight");
        return _document.GetLayoutEngine().EnsureLayout();
    }

    /// <summary>Appends a row to the table, updates the layout, then removes the row (laid out by the next invocation).</summary>
    [Benchmark]
    public object? AppendTableRow()
    {
        var row = InvalidationDocument.CreateRow(_document, 999);
        _tableBody.AppendChild(row);
        var root = _document.GetLayoutEngine().EnsureLayout();
        _tableBody.RemoveChild(row);
        return root;
    }

    /// <summary>Changes the width of 10 cards, reading the offsetTop of each card after the write (10 forced layouts).</summary>
    [Benchmark]
    public int OffsetTopAfterEachWrite()
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var card = _cards[i * 50];
            card.Style.SetProperty("width", card.Style.GetPropertyValue("width") == "90%" ? "" : "90%");
            sum += card.OffsetTop;
        }

        return sum;
    }

    /// <summary>Runs one animation frame of a document with one running CSS animation (the rendering steps update the styles and the layout).</summary>
    [Benchmark]
    public object? AnimationFrame() => _animation.RunFrame();

    /// <summary>
    /// Toggles a class of one card of a document whose style sheet declares transitions and styles every ::before and ::after
    /// (as Bootstrap does), then runs one animation frame (the rendering steps update the CSS animations and transitions). The
    /// class changes no transitioned property.
    /// </summary>
    [Benchmark]
    public object? ClassChangeTransitionFrame()
    {
        _transitionsCard.ClassList.Toggle("selected");
        return _transitions.RunFrame();
    }

    /// <summary>
    /// Toggles a class of one card that changes its transitioned properties, then runs one animation frame: every toggle cancels
    /// or reverses the running transitions of the card.
    /// </summary>
    [Benchmark]
    public object? TransitionReversalFrame()
    {
        _transitionsCard.ClassList.Toggle("highlight");
        return _transitions.RunFrame();
    }
}

/// <summary>The document of <see cref="InvalidationBenchmarks"/> with a running CSS animation or transitions, advanced one frame at a time.</summary>
internal sealed class AnimationBenchmarkScenario(InvalidationDocumentKind kind) : IDisposable
{
    private BrowsingContext? _context;
    private ManualTimeProvider? _clock;
    private Document? _document;

    public Document Document => _document ?? throw new InvalidOperationException("The scenario did not run a frame.");

    public object? RunFrame()
    {
        if (_context is null)
        {
            _clock = new ManualTimeProvider();
            _context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = _clock });
            _document = _context.Navigate("https://example.com/", _ => InvalidationDocument.Create(kind));
            _ = _document.GetLayoutEngine().EnsureLayout();
            _context.EventLoop.RunUntilIdle();
        }

        _clock!.Advance(TimeSpan.FromMilliseconds(1000d / 60));
        _context.EventLoop.RunUntilIdle();
        return _document!.GetLayoutEngine().BoxTree;
    }

    public void Dispose() => _context?.Dispose();
}

/// <summary>A clock advanced by the benchmarks (the event loop runs its timers and its rendering steps with it).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_timestamp);

    public void Advance(TimeSpan delay) => _timestamp += delay.Ticks;
}

/// <summary>The variants of the document of <see cref="InvalidationBenchmarks"/>.</summary>
internal enum InvalidationDocumentKind
{
    /// <summary>No animation nor transition.</summary>
    Static,

    /// <summary>One card has a running CSS animation.</summary>
    Animation,

    /// <summary>The cards have transitions, and a rule styles every element and its ::before and ::after pseudo-elements (as Bootstrap does).</summary>
    Transitions,
}

/// <summary>The documents of <see cref="InvalidationBenchmarks"/>.</summary>
internal static class InvalidationDocument
{
    private const string TransitionsStyleSheet = """
        *, ::before, ::after { box-sizing: border-box }
        .card { transition: border-color .15s ease-in-out, box-shadow .15s ease-in-out }
        .card.highlight { box-shadow: 0 0 0 4px #cc0 }
        .card.selected { background: #def }
        .card.selected h2 { text-decoration: underline }
        """;

    private const string StyleSheet = """
        body { font: 14px/1.4 sans-serif; margin: 8px }
        h1 { font-size: 20px }
        .grid { display: block }
        .card { border: 1px solid #ccc; padding: 4px 8px; margin: 4px 0; border-radius: 4px }
        .card.highlight { background: #ffd; border-color: #cc0 }
        .card.highlight h2 { color: #880 }
        .card h2 { font-size: 16px; margin: 0 0 2px }
        .card > p { margin: 2px 0; color: #333 }
        .card p .tag { display: inline-block; padding: 0 4px; margin-right: 4px; background: #eee; border-radius: 2px }
        .card p .tag + .tag { background: #def }
        .card:hover { border-color: blue }
        .card:first-child { margin-top: 0 }
        ul.list { margin: 0; padding-left: 20px }
        ul.list > li:nth-child(odd) { color: #555 }
        ul.list > li:last-child { font-style: italic }
        .footer a { color: #06c; text-decoration: none }
        .footer a:hover { text-decoration: underline }
        table { border-collapse: collapse; width: 100% }
        th { text-align: left; background: #f4f4f4 }
        td, th { padding: 2px 6px; border-bottom: 1px solid #ddd }
        tr:nth-child(even) td { background: #fafafa }
        tr.selected td { background: #def }
        td.number { text-align: right; font-variant-numeric: tabular-nums }
        @keyframes pulse { from { opacity: 1 } to { opacity: 0.5 } }
        .animated { animation: pulse 1s linear infinite alternate }
        """;

    /// <summary>Creates the document: 500 cards of 9 elements and a table of 100 rows of 5 elements (5,000 elements in the body).</summary>
    public static string Create(InvalidationDocumentKind kind)
    {
        var html = new StringBuilder(600_000);
        html.Append("<!DOCTYPE html><html><head><title>Invalidation</title><style>").Append(StyleSheet).Append(kind is InvalidationDocumentKind.Transitions ? TransitionsStyleSheet : "")
            .Append("</style></head><body><h1>Cards</h1><div class=grid>");
        for (var i = 0; i < 500; i++)
        {
            html.Append(kind is InvalidationDocumentKind.Animation && i == 250 ? "<div class=\"card animated\" id=card" : "<div class=card id=card").Append(i).Append('>')
                .Append("<h2>Card ").Append(i).Append("</h2>")
                .Append("<p>Some text of the card ").Append(i).Append(" <span class=tag>alpha</span><span class=tag>beta</span></p>")
                .Append("<ul class=list><li>First item<li>Second item<li>Third item</ul>")
                .Append("<div class=footer><a href=#card").Append(i).Append(">Link</a></div>")
                .Append("</div>");
        }

        html.Append("</div><table><thead><tr><th>Name<th>Value<th>Count<th>Total</thead><tbody>");
        for (var i = 0; i < 100; i++)
        {
            html.Append("<tr><td>Row ").Append(i).Append("<td>Value ").Append(i).Append("<td class=number>").Append(i * 3).Append("<td class=number>").Append(i * 7).Append("</tr>");
        }

        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    /// <summary>Creates a row of the table.</summary>
    public static Element CreateRow(Document document, int index)
    {
        var row = document.CreateElement("tr");
        for (var i = 0; i < 4; i++)
        {
            var cell = document.CreateElement("td");
            cell.TextContent = "Cell " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row.AppendChild(cell);
        }

        return row;
    }
}

/// <summary>Runs one scenario of <see cref="InvalidationBenchmarks"/> in a loop, to profile it (e.g. with <c>dotnet-trace</c>).</summary>
internal static class InvalidationProfile
{
    public static int Run(string scenario, int count)
    {
        var benchmarks = new InvalidationBenchmarks();
        benchmarks.Setup();
        Func<object?> action = scenario switch
        {
            "class" => benchmarks.ClassChange,
            "row" => benchmarks.AppendTableRow,
            "offset" => () => benchmarks.OffsetTopAfterEachWrite(),
            "frame" => benchmarks.AnimationFrame,
            "transition-frame" => benchmarks.ClassChangeTransitionFrame,
            "transition-reversal" => benchmarks.TransitionReversalFrame,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // (the process CPU time includes the GC threads: under load, it is more stable than the elapsed time)
        _ = action();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var collections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            _ = action();
        }

        cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu;
        var allocatedMegabytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / count / 1024 / 1024.0;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{scenario}: {stopwatch.Elapsed.TotalMilliseconds / count:F3} ms per invocation, {cpu.TotalMilliseconds / count:F3} ms CPU, {allocatedMegabytes:F1} MB allocated, GC {GC.CollectionCount(0) - collections.Item1}/{GC.CollectionCount(1) - collections.Item2}/{GC.CollectionCount(2) - collections.Item3}"));
        benchmarks.Cleanup();
        return 0;
    }
}
