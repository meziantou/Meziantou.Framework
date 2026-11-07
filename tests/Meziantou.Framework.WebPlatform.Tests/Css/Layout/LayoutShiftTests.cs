using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>Layout Instability (https://wicg.github.io/layout-instability/): the layout shifts are reported by the rendering steps, after the layout.</summary>
public sealed class LayoutShiftTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<ScriptError> _errors = [];

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            UnhandledScriptError = (_, error) => _errors.Add(error),
            Features = features,
        });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    /// <summary>Runs the rendering steps of the next frames (the virtual clock advances by one frame each time).</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static List<LayoutShift> Observe(Document document, bool buffered = false)
    {
        var entries = new List<LayoutShift>();
        var observer = new PerformanceObserver(document.DefaultView!, (list, _, _) => entries.AddRange(list.GetEntries().Cast<LayoutShift>()));
        observer.Observe(new PerformanceObserverInit { Type = "layout-shift", Buffered = buffered });
        return entries;
    }

    /// <summary>The expected score of a shift of the WPT tests: the impact fraction of the 800x600 viewport times the distance fraction.</summary>
    private static double Score(double impactArea, double moveDistance) => impactArea / (800 * 600) * (moveDistance / 800);

    private static (double X, double Y, double Width, double Height) ToTuple(DomRectReadOnly rect) => (rect.X, rect.Y, rect.Width, rect.Height);

    [Fact]
    public void SimpleBlockMovement_IsReportedWithItsSource()
    {
        var document = Parse("<div id=shifter style='position: relative; width: 300px; height: 200px; background: blue'></div>");
        var entries = Observe(document);
        RunFrames(2);
        Assert.Empty(entries);

        var shifter = Get(document, "shifter");
        shifter.Style.SetProperty("top", "160px");
        RunFrames();
        var entry = Assert.Single(entries);
        Assert.Equal("layout-shift", entry.EntryType);
        Assert.Equal("", entry.Name);
        Assert.Equal(0, entry.Duration);
        Assert.Equal(Score(300 * (200 + 160), 160), entry.Value);
        Assert.False(entry.HadRecentInput);
        Assert.Equal(0, entry.LastInputTime);
        var source = Assert.Single(entry.Sources);
        Assert.Same(shifter, source.Node);
        Assert.Equal((0, 0, 300, 200), ToTuple(source.PreviousRect));
        Assert.Equal((0, 160, 300, 200), ToTuple(source.CurrentRect));

        // Nothing moves anymore
        RunFrames(2);
        Assert.Single(entries);
    }

    [Fact]
    public void ShiftWhileScrolled_OnlyTheVisiblePartIsCounted()
    {
        var document = Parse("<style>body { height: 2000px }</style><div id=shifter style='position: relative; width: 300px; height: 200px; background: blue'></div>");
        var entries = Observe(document);
        RunFrames(2);
        document.ScrollingElement!.ScrollTop = 100;
        Get(document, "shifter").Style.SetProperty("top", "60px");
        RunFrames();
        var entry = Assert.Single(entries);
        var source = Assert.Single(entry.Sources);
        Assert.Equal((0, 0, 300, 100), ToTuple(source.PreviousRect));
        Assert.Equal((0, 0, 300, 160), ToTuple(source.CurrentRect));

        // The viewport has a vertical scrollbar: 785x600
        Assert.Equal(300 * (200 - 100 + 60) / (785d * 600) * (60d / 785), entry.Value);
    }

    [Fact]
    public void SmallMovesAndTransforms_AreNotShifts()
    {
        var document = Parse("""
            <div id=small style='position: relative; width: 100px; height: 100px; background: blue'></div>
            <div id=transformed style='position: relative; width: 100px; height: 100px; background: blue; transform: translateX(20px)'></div>
            <div id=invisible style='position: relative; width: 100px; height: 100px'></div>
            <div id=hidden style='position: relative; width: 100px; height: 100px; background: blue; visibility: hidden'></div>
            <div style='opacity: 0'><div id=transparent style='position: relative; width: 100px; height: 100px; background: blue'></div></div>
            """);
        var entries = Observe(document);
        RunFrames();
        Get(document, "small").Style.SetProperty("left", "2px");
        Get(document, "transformed").Style.SetProperty("transform", "translateY(100px)");
        Get(document, "invisible").Style.SetProperty("top", "50px");
        Get(document, "hidden").Style.SetProperty("top", "50px");
        Get(document, "transparent").Style.SetProperty("top", "50px");
        RunFrames(2);
        Assert.Empty(entries);
    }

    [Fact]
    public void ScrollAndCounterScroll_AreNotShifts()
    {
        var document = Parse("""
            <style>body { height: 2000px }</style>
            <div id=scroller style='overflow: scroll; width: 200px; height: 200px'>
              <div style='height: 600px'><div id=child style='position: relative; top: 100px; height: 50px; background: blue'></div></div>
            </div>
            """);
        var entries = Observe(document);
        RunFrames();
        document.ScrollingElement!.ScrollTop = 50;
        Get(document, "scroller").ScrollTop = 100;
        RunFrames();
        Get(document, "child").Style.SetProperty("top", "200px");
        Get(document, "scroller").ScrollTop = 200;
        RunFrames();
        Assert.Empty(entries);
    }

    [Fact]
    public void Sources_AreSortedByImpactAndEnclosedSourcesAreRedundant()
    {
        var document = Parse("""
            <div id=grow></div>
            <div id=outer style='width: 300px; height: 100px; background: blue'><div id=inner style='width: 100px; height: 50px; background: red'></div></div>
            <div id=small style='width: 10px; height: 10px; background: blue'></div>
            """);
        var entries = Observe(document);
        RunFrames();
        Get(document, "grow").Style.SetProperty("height", "50px");
        RunFrames();
        var entry = Assert.Single(entries);
        Assert.Equal([Get(document, "outer"), Get(document, "small")], entry.Sources.Select(source => source.Node));
        Assert.Equal((0, 50, 300, 100), ToTuple(entry.Sources[0].CurrentRect));
    }

    [Fact]
    public void RecentInput_IsReported()
    {
        var document = Parse("<div id=shifter style='position: relative; width: 300px; height: 100px; background: blue'></div>");
        var entries = Observe(document);
        RunFrames();
        var shifter = Get(document, "shifter");
        EventDispatcher.Dispatch(new MouseEvent("mousedown", new MouseEventInit { Bubbles = true }) { IsTrusted = true }, shifter);
        shifter.Style.SetProperty("top", "60px");
        RunFrames();
        var entry = Assert.Single(entries);
        Assert.True(entry.HadRecentInput);
        Assert.True(entry.LastInputTime > 0 && entry.LastInputTime < entry.StartTime);

        // More than 500ms after the input
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        shifter.Style.SetProperty("top", "0");
        RunFrames();
        Assert.Equal(2, entries.Count);
        Assert.False(entries[1].HadRecentInput);
        Assert.Equal(entry.LastInputTime, entries[1].LastInputTime);
    }

    [Fact]
    public void BufferedObserver_SeesThePreviousEntries()
    {
        var document = Parse("<div id=shifter style='position: relative; width: 300px; height: 100px; background: blue'></div>");
        Observe(document);
        RunFrames();
        Get(document, "shifter").Style.SetProperty("top", "60px");
        RunFrames();

        var buffered = Observe(document, buffered: true);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(buffered);

        // The layout shifts are not available from the performance timeline
        Assert.Empty(document.DefaultView!.Performance.GetEntriesByType("layout-shift"));
    }

    [Fact]
    public void FeatureDisabled_LayoutShiftIsNotSupported()
    {
        var document = Parse("<div id=shifter style='position: relative; width: 300px; height: 100px; background: blue'></div>", new WebPlatformFeatures { LayoutInstabilityEnabled = false });
        var entries = Observe(document);
        Assert.DoesNotContain("layout-shift", document.DefaultView!.Performance.SupportedEntryTypes);
        Get(document, "shifter").Style.SetProperty("top", "60px");
        RunFrames();
        Assert.Empty(entries);
        Assert.False(document.IsTrackingLayoutShifts);
    }
}
