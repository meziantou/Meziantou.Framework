using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Svg;

/// <summary>
/// SVG animations (SMIL, https://svgwg.org/specs/animations/): the timeline of the document fragments, the intervals and the
/// timing events of the animation elements, and the animated values of the attributes and of the properties.
/// </summary>
public sealed class SvgAnimationTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Loads a document whose body is an svg element with the given content: the timeline begins with the load event.</summary>
    private Document Load(string svg, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Features = features });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><svg id=svg width=200 height=200>" + svg + "</svg>");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Loads a document and pauses its timeline: the tests seek it.</summary>
    private (Document Document, SvgSvgElement Svg) LoadPaused(string svg)
    {
        var document = Load(svg);
        var root = (SvgSvgElement)document.GetElementById("svg")!;
        root.PauseAnimations();
        return (document, root);
    }

    private void Advance(double milliseconds)
    {
        _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        _contexts.EventLoop.RunUntilIdle();
    }

    private static string GetComputedValue(Element element, string property) => element.OwnerDocument!.DefaultView!.GetComputedStyle(element).GetPropertyValue(property);

    [Fact]
    public void Animate_AnimatesTheAnimatedValueOfAnAttribute()
    {
        var (document, svg) = LoadPaused("<rect id=target width=10 height=10><animate attributeName=width from=10 to=110 dur=4s /></rect>");
        var rect = (SvgRectElement)document.GetElementById("target")!;

        svg.SetCurrentTime(1);
        Assert.Equal(35, rect.Width.AnimVal.Value);
        Assert.Equal(10, rect.Width.BaseVal.Value);
        Assert.Equal("10", rect.GetAttribute("width"));

        // The width attribute is the presentation attribute of the width property, and the geometry of the element
        Assert.Equal("35px", GetComputedValue(rect, "width"));
        Assert.Equal(35, rect.GetBBox().Width);

        // After the end of the interval, the animation has no effect (fill="remove")
        svg.SetCurrentTime(5);
        Assert.Equal(10, rect.Width.AnimVal.Value);
        Assert.Equal("10px", GetComputedValue(rect, "width"));
    }

    [Fact]
    public void Animate_FreezesTheLastValue()
    {
        var (document, svg) = LoadPaused("<rect id=target width=10 height=10><animate attributeName=width from=10 to=110 dur=4s fill=freeze /></rect>");
        var rect = (SvgRectElement)document.GetElementById("target")!;

        svg.SetCurrentTime(10);
        Assert.Equal(110, rect.Width.AnimVal.Value);

        // Seeking backwards
        svg.SetCurrentTime(2);
        Assert.Equal(60, rect.Width.AnimVal.Value);
    }

    [Fact]
    public void Animate_AnimatesAProperty()
    {
        var (document, svg) = LoadPaused("<rect id=target width=10 height=10 fill=red style='fill: yellow'><animate attributeName=fill from='rgb(255, 0, 0)' to='rgb(0, 0, 255)' dur=4s /></rect>");
        var rect = document.GetElementById("target")!;

        // The animated value overrides the style attribute
        svg.SetCurrentTime(2);
        Assert.Equal("rgb(128, 0, 128)", GetComputedValue(rect, "fill"));
        Assert.Equal("red", rect.GetAttribute("fill"));

        svg.SetCurrentTime(4);
        Assert.Equal("rgb(255, 255, 0)", GetComputedValue(rect, "fill"));
    }

    [Fact]
    public void Animate_ImportantDeclarationsWinOverTheAnimation()
    {
        var (document, svg) = LoadPaused("<style>#target { opacity: 0.5 !important }</style><rect id=target width=10 height=10><animate attributeName=opacity from=0 to=1 dur=4s /></rect>");

        svg.SetCurrentTime(2);
        Assert.Equal("0.5", GetComputedValue(document.GetElementById("target")!, "opacity"));
    }

    [Fact]
    public void Animate_ValuesKeyTimesAndCalcMode()
    {
        var (document, svg) = LoadPaused("""
            <rect id=linear width=10 height=10><animate attributeName=x values="0; 100; 200" keyTimes="0; 0.25; 1" dur=4s /></rect>
            <rect id=discrete width=10 height=10><animate attributeName=x values="0; 100; 200" calcMode=discrete dur=3s /></rect>
            <rect id=spline width=10 height=10><animate attributeName=x values="0; 100" calcMode=spline keySplines="0 0 1 1" dur=4s /></rect>
            <rect id=paced width=10 height=10><animate attributeName=x values="0; 10; 100" calcMode=paced dur=4s /></rect>
            """);

        svg.SetCurrentTime(0.5f);
        Assert.Equal(50, ((SvgRectElement)document.GetElementById("linear")!).X.AnimVal.Value);
        Assert.Equal(0, ((SvgRectElement)document.GetElementById("discrete")!).X.AnimVal.Value);

        svg.SetCurrentTime(2);
        Assert.Equal(100 + (100f / 3), ((SvgRectElement)document.GetElementById("linear")!).X.AnimVal.Value, 3);
        Assert.Equal(200, ((SvgRectElement)document.GetElementById("discrete")!).X.AnimVal.Value);
        Assert.Equal(50, ((SvgRectElement)document.GetElementById("spline")!).X.AnimVal.Value, 3);

        // A paced animation moves at a constant speed: half of the distance at the middle of the duration
        Assert.Equal(50, ((SvgRectElement)document.GetElementById("paced")!).X.AnimVal.Value, 3);
    }

    [Fact]
    public void Animate_AdditiveAndCumulativeAnimations()
    {
        var (document, svg) = LoadPaused("""
            <rect id=by x=10 width=10 height=10><animate attributeName=x by=100 dur=4s /></rect>
            <rect id=sum x=10 width=10 height=10><animate attributeName=x from=0 to=100 additive=sum dur=4s /></rect>
            <rect id=accumulate x=10 width=10 height=10><animate attributeName=x from=0 to=100 accumulate=sum repeatCount=3 dur=4s /></rect>
            <rect id=sandwich x=10 width=10 height=10>
              <animate attributeName=x to=100 dur=4s />
              <animate attributeName=x by=20 dur=4s />
            </rect>
            """);

        svg.SetCurrentTime(2);
        Assert.Equal(60, ((SvgRectElement)document.GetElementById("by")!).X.AnimVal.Value);
        Assert.Equal(60, ((SvgRectElement)document.GetElementById("sum")!).X.AnimVal.Value);
        Assert.Equal(50, ((SvgRectElement)document.GetElementById("accumulate")!).X.AnimVal.Value);

        // The animations are applied in order: the to animation goes from the base value to 100, the by animation adds to it
        Assert.Equal(65, ((SvgRectElement)document.GetElementById("sandwich")!).X.AnimVal.Value);

        svg.SetCurrentTime(6);
        Assert.Equal(150, ((SvgRectElement)document.GetElementById("accumulate")!).X.AnimVal.Value);
    }

    [Fact]
    public void Set_SetsTheValueDuringTheInterval()
    {
        var (document, svg) = LoadPaused("<rect id=target width=10 height=10 visibility=visible><set attributeName=visibility to=hidden begin=1s dur=1s /></rect>");
        var rect = document.GetElementById("target")!;

        svg.SetCurrentTime(0.5f);
        Assert.Equal("visible", GetComputedValue(rect, "visibility"));
        svg.SetCurrentTime(1.5f);
        Assert.Equal("hidden", GetComputedValue(rect, "visibility"));
        svg.SetCurrentTime(2);
        Assert.Equal("visible", GetComputedValue(rect, "visibility"));
    }

    [Fact]
    public void AnimateTransform_AnimatesTheTransformList()
    {
        var (document, svg) = LoadPaused("""
            <rect id=replace width=10 height=10 transform="scale(2)"><animateTransform attributeName=transform type=translate from="0 0" to="100 50" dur=4s /></rect>
            <rect id=sum width=10 height=10 transform="scale(2)"><animateTransform attributeName=transform type=translate from="0 0" to="100 50" additive=sum dur=4s /></rect>
            """);

        svg.SetCurrentTime(2);
        var replace = (SvgRectElement)document.GetElementById("replace")!;
        Assert.Equal("translate(50 25)", SerializeTransforms(replace.Transform.AnimVal));
        Assert.Equal("scale(2 2)", SerializeTransforms(replace.Transform.BaseVal));
        var matrix = replace.GetCtm()!;
        Assert.Equal((1, 50, 25), (matrix.A, matrix.E, matrix.F));

        // An additive animation appends its transform to the transforms of the attribute
        var sum = (SvgRectElement)document.GetElementById("sum")!;
        Assert.Equal("scale(2 2) translate(50 25)", SerializeTransforms(sum.Transform.AnimVal));
        matrix = sum.GetCtm()!;
        Assert.Equal((2, 100, 50), (matrix.A, matrix.E, matrix.F));

        static string SerializeTransforms(SvgTransformList list) => string.Join(' ', list.Select(transform => transform.Type switch
        {
            SvgTransform.SvgTransformTranslate => $"translate({transform.Matrix.E} {transform.Matrix.F})",
            SvgTransform.SvgTransformScale => $"scale({transform.Matrix.A} {transform.Matrix.D})",
            _ => "other",
        }));
    }

    [Fact]
    public void AnimateMotion_MovesTheElementAlongThePath()
    {
        var (document, svg) = LoadPaused("""
            <path id=track d="M 0 0 H 100 V 100" />
            <rect id=path width=10 height=10><animateMotion path="M 0 0 H 100 V 100" dur=4s rotate=auto /></rect>
            <rect id=mpath width=10 height=10><animateMotion dur=4s><mpath href="#track" /></animateMotion></rect>
            <rect id=values width=10 height=10 transform="translate(1 2)"><animateMotion from="0,0" to="40,80" dur=4s /></rect>
            """);

        // At 3/4 of the path: the second segment, whose direction is the direction of the y axis
        svg.SetCurrentTime(3);
        var matrix = ((SvgRectElement)document.GetElementById("path")!).GetCtm()!;
        Assert.Equal((100, 50), (matrix.E, matrix.F));
        Assert.Equal((0, 1), (Math.Round(matrix.A, 6, MidpointRounding.AwayFromZero), Math.Round(matrix.B, 6, MidpointRounding.AwayFromZero)));

        matrix = ((SvgRectElement)document.GetElementById("mpath")!).GetCtm()!;
        Assert.Equal((1, 100, 50), (matrix.A, matrix.E, matrix.F));

        // The supplemental transform is applied after the transform of the element
        matrix = ((SvgRectElement)document.GetElementById("values")!).GetCtm()!;
        Assert.Equal((31, 62), (matrix.E, matrix.F));
    }

    [Fact]
    public void Timeline_BeginsWithTheLoadEventAndFollowsTheClock()
    {
        var document = Load("<rect id=target width=10 height=10><animate attributeName=width from=10 to=110 dur=1s fill=freeze /></rect>");
        var svg = (SvgSvgElement)document.GetElementById("svg")!;
        var rect = (SvgRectElement)document.GetElementById("target")!;
        Assert.False(svg.AnimationsPaused());

        Advance(500);
        Assert.InRange(svg.GetCurrentTime(), 0.45, 0.55);
        Assert.InRange(rect.Width.AnimVal.Value, 55, 65);

        // A paused timeline does not follow the clock
        svg.PauseAnimations();
        Assert.True(svg.AnimationsPaused());
        var time = svg.GetCurrentTime();
        Advance(200);
        Assert.Equal(time, svg.GetCurrentTime());

        svg.UnpauseAnimations();
        Advance(1000);
        Assert.Equal(110, rect.Width.AnimVal.Value);
    }

    [Fact]
    public void TimingEvents_AreFiredWhenTheIntervalBeginsRepeatsAndEnds()
    {
        var document = Load("<rect width=10 height=10><animate id=animation attributeName=width from=10 to=110 begin=100ms dur=100ms repeatCount=3 /></rect>");
        var animation = (SvgAnimateElement)document.GetElementById("animation")!;
        var events = new List<string>();
        foreach (var type in (string[])["beginEvent", "repeatEvent", "endEvent"])
        {
            animation.AddEventListener(type, e => events.Add(e.Type + ":" + ((TimeEvent)e).Detail.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        animation.OnEnd = _ => events.Add("onend");
        Assert.Equal(0.1f, animation.GetStartTime());
        Assert.Equal(0.1f, animation.GetSimpleDuration());

        Advance(50);
        Assert.Empty(events);
        Advance(1000);
        Assert.Equal(["beginEvent:0", "repeatEvent:1", "repeatEvent:2", "endEvent:0", "onend"], events);
        Assert.Throws<DomException>(() => animation.GetStartTime());
    }

    [Fact]
    public void Seeking_OnlyFiresTheEventsOfTheChangesOfActiveState()
    {
        var (document, svg) = LoadPaused("""
            <rect width=10 height=10>
              <set id=skipped attributeName=x to=1 begin=1s dur=1s />
              <set id=active attributeName=y to=1 begin=4s dur=10s />
            </rect>
            """);
        var events = new List<string>();
        foreach (var id in (string[])["skipped", "active"])
        {
            foreach (var type in (string[])["beginEvent", "endEvent"])
            {
                document.GetElementById(id)!.AddEventListener(type, e => events.Add(id + ":" + e.Type));
            }
        }

        svg.SetCurrentTime(5);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["active:beginEvent"], events);

        svg.SetCurrentTime(0);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["active:beginEvent", "active:endEvent"], events);
    }

    [Fact]
    public void Begin_SyncbaseEventAndRepeatValues()
    {
        var (document, svg) = LoadPaused("""
            <rect id=target width=10 height=10>
              <set id=first attributeName=x to=1 begin=1s dur=2s />
              <set id=second attributeName=y to=1 begin="first.end+1s" dur=1s />
              <set id=third attributeName=width to=50 begin="target.click" dur=1s />
            </rect>
            """);
        var second = (SvgSetElement)document.GetElementById("second")!;
        var rect = (SvgRectElement)document.GetElementById("target")!;
        Assert.Equal(4, second.GetStartTime());

        svg.SetCurrentTime(4.5f);
        Assert.Equal(1, rect.Y.AnimVal.Value);

        // An event of the target element begins the element at the current time
        rect.DispatchEvent(new Event("click"));
        Assert.Equal(50, rect.Width.AnimVal.Value);
        Assert.Equal(4.5f, ((SvgSetElement)document.GetElementById("third")!).GetStartTime());
        svg.SetCurrentTime(5.5f);
        Assert.Equal(10, rect.Width.AnimVal.Value);
    }

    [Fact]
    public void BeginElementAndEndElement()
    {
        var (document, svg) = LoadPaused("<rect id=target width=10 height=10><set id=set attributeName=width to=50 begin=indefinite /></rect>");
        var set = (SvgSetElement)document.GetElementById("set")!;
        var rect = (SvgRectElement)document.GetElementById("target")!;
        Assert.Same(rect, set.TargetElement);
        Assert.Throws<DomException>(() => set.GetStartTime());

        svg.SetCurrentTime(2);
        set.BeginElement();
        Assert.Equal(2, set.GetStartTime());
        Assert.Equal(50, rect.Width.AnimVal.Value);

        set.EndElement();
        Assert.Equal(10, rect.Width.AnimVal.Value);
    }

    [Fact]
    public void TargetElement_IsTheReferencedElement()
    {
        var (document, svg) = LoadPaused("<rect id=target width=10 height=10 /><set id=set href='#target' attributeName=width to=50 />");
        var set = (SvgSetElement)document.GetElementById("set")!;
        var rect = (SvgRectElement)document.GetElementById("target")!;
        Assert.Same(rect, set.TargetElement);

        svg.SetCurrentTime(1);
        Assert.Equal(50, rect.Width.AnimVal.Value);

        // The animation stops when the element is removed
        set.Remove();
        Assert.Equal(10, rect.Width.AnimVal.Value);
    }

    [Fact]
    public void DocumentWithoutBrowsingContext_IsSampledAtTheTimeOfItsTimeline()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><svg id=svg><rect id=target width=10 height=10><animate attributeName=width from=10 to=110 dur=4s /></rect></svg>");
        var svg = (SvgSvgElement)document.GetElementById("svg")!;
        var rect = (SvgRectElement)document.GetElementById("target")!;

        // The timeline has no clock: it is at 0 until it is seeked
        Assert.Equal(10, rect.Width.AnimVal.Value);
        svg.SetCurrentTime(2);
        Assert.Equal(60, rect.Width.AnimVal.Value);
        Assert.Equal(60, rect.GetBBox().Width);
    }

    [Fact]
    public void DisabledFeature_TheAnimationsHaveNoEffect()
    {
        var document = Load("<rect id=target width=10 height=10><set attributeName=width to=50 /></rect>", new WebPlatformFeatures { SvgAnimationsEnabled = false });
        var rect = (SvgRectElement)document.GetElementById("target")!;

        Advance(100);
        Assert.Equal(10, rect.Width.AnimVal.Value);
    }
}
