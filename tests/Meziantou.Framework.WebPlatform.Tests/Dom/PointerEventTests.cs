using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>PointerEvent (https://w3c.github.io/pointerevents/#pointerevent-interface).</summary>
public sealed class PointerEventTests
{
    [Fact]
    public void Constructor_Defaults()
    {
        var e = new PointerEvent("pointerdown");
        Assert.Equal((0, 1d, 1d, 0f, 0, 0, 0, "", false), (e.PointerId, e.Width, e.Height, e.Pressure, e.TiltX, e.TiltY, e.Twist, e.PointerType, e.IsPrimary));
        Assert.Equal(Math.PI / 2, e.AltitudeAngle, 10);
        Assert.Equal(0, e.AzimuthAngle);
        Assert.Empty(e.GetCoalescedEvents());
        Assert.IsAssignableFrom<MouseEvent>(e);
    }

    [Fact]
    public void Constructor_TiltIsConvertedToAngles()
    {
        var e = new PointerEvent("pointermove", new PointerEventInit { TiltX = 0, TiltY = 45, PointerType = "pen", ClientX = 3 });
        Assert.Equal(Math.PI / 4, e.AltitudeAngle, 10);
        Assert.Equal(Math.PI / 2, e.AzimuthAngle, 10);
        Assert.Equal(3, e.ClientX);
        Assert.Equal("pen", e.PointerType);
    }

    [Fact]
    public void Constructor_AnglesAreConvertedToTilt()
    {
        var e = new PointerEvent("pointermove", new PointerEventInit { AltitudeAngle = Math.PI / 4, AzimuthAngle = 0 });
        Assert.Equal((45, 0), (e.TiltX, e.TiltY));
    }

    [Fact]
    public void FromElementAndToElement()
    {
        var document = new HtmlDocumentParser().Parse("<p id=a></p><p id=b></p>");
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        var events = new List<string>();
        foreach (var type in new[] { "mouseover", "mouseout", "mousemove", "pointerover" })
        {
            a.AddEventListener(type, e =>
            {
                var mouseEvent = (MouseEvent)e;
#pragma warning disable WEBPLATEXP0029 // The legacy attributes of Blink and WebKit
                events.Add($"{type} {(mouseEvent.FromElement as Element)?.Id ?? "null"} {(mouseEvent.ToElement as Element)?.Id ?? "null"}");
#pragma warning restore WEBPLATEXP0029
            });
        }

        a.DispatchEvent(new MouseEvent("mouseover", new MouseEventInit { RelatedTarget = b }));
        a.DispatchEvent(new MouseEvent("mouseout", new MouseEventInit { RelatedTarget = b }));
        a.DispatchEvent(new MouseEvent("mousemove"));
        a.DispatchEvent(new PointerEvent("pointerover", new PointerEventInit { RelatedTarget = b }));

        Assert.Equal(["mouseover b a", "mouseout a b", "mousemove null a", "pointerover null null"], events);
    }

    [Fact]
    public void Click_FiresASyntheticPointerEvent()
    {
        var document = new HtmlDocumentParser().Parse("<button id=b>");
        Event? clickEvent = null;
        var button = (HtmlElement)document.GetElementById("b")!;
        button.AddEventListener("click", e => clickEvent = e);
        button.Click();
        var pointerEvent = Assert.IsType<PointerEvent>(clickEvent);
        Assert.Equal(-1, pointerEvent.PointerId);
        Assert.True(pointerEvent.Composed);
    }
}
