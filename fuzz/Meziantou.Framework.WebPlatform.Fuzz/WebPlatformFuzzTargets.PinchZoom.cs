using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

public static partial class WebPlatformFuzzTargets
{
    // The operations of the pinch zoom target: an operation byte, then its arguments (one byte each)
    private const byte PinchZoomPinch = 0; // x, y, scale
    private const byte PinchZoomWheel = 1; // x, y, deltaX, deltaY
    private const byte PinchZoomTouchDown = 2; // finger, x, y
    private const byte PinchZoomTouchMove = 3; // finger, x, y
    private const byte PinchZoomTouchUp = 4; // finger
    private const byte PinchZoomFrame = 5;
    private const int PinchZoomOperationCount = 6;

    /// <summary>The maximum number of operations of an input of the pinch zoom target.</summary>
    private const int PinchZoomMaxOperations = 64;

    private const double PinchZoomTolerance = 1e-6;

    /// <summary>
    /// The page of the pinch zoom target: taller than the viewport, with a scroll container, and an area where touch-action
    /// prevents the pinch zoom.
    /// </summary>
    private const string PinchZoomDocumentMarkup = """
        <!DOCTYPE html>
        <style>body { margin: 0 } #scroller { width: 200px; height: 200px; overflow: auto } #content { height: 1000px } #nozoom { touch-action: pan-x pan-y; height: 200px }</style>
        <div id="scroller"><div id="content"></div></div>
        <div id="nozoom"></div>
        <div style="height: 2000px"></div>
        """;

    // (a property: the targets of the other part of the class read it when they are initialized, before the fields of this part)
    private static string[] PinchZoomTokens => ["\u0000\u0080\u0080", "\u0001\u0080\u0080", "\u0002\u0000", "\u0002\u0001", "\u0003\u0000", "\u0003\u0001", "\u0004\u0000", "\u0004\u0001", "\u0005"];

    /// <summary>
    /// Plays a sequence of pinches of the touchpad, wheel rotations and touches of two fingers (see the operations above) on a
    /// page, and checks the visual viewport after each of them: its scale is between 1 and 5, it stays in the layout viewport,
    /// and its size is the size of the layout viewport divided by its scale.
    /// </summary>
    public static void PinchZoom(ReadOnlySpan<byte> data)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => PinchZoomDocumentMarkup);
        context.EventLoop.RunUntilIdle();
        var visualViewport = document.DefaultView!.VisualViewport!;
        var root = document.DocumentElement!;
        var input = context.Input;
        PointerInput[] fingers = [input.CreatePointer("touch"), input.CreatePointer("touch")];

        for (var operation = 0; operation < PinchZoomMaxOperations && !data.IsEmpty; operation++)
        {
            switch (FuzzInput.ReadByte(ref data) % PinchZoomOperationCount)
            {
                case PinchZoomPinch:
                    input.Pinch(ReadX(ref data), ReadY(ref data), (FuzzInput.ReadByte(ref data) + 1) / 32d);
                    break;

                case PinchZoomWheel:
                    input.Wheel(ReadX(ref data), ReadY(ref data), ReadDelta(ref data), ReadDelta(ref data));
                    break;

                case PinchZoomTouchDown:
                    {
                        var finger = fingers[FuzzInput.ReadByte(ref data) % fingers.Length];
                        finger.Move(ReadX(ref data), ReadY(ref data));
                        finger.Down();
                        break;
                    }

                case PinchZoomTouchMove:
                    fingers[FuzzInput.ReadByte(ref data) % fingers.Length].Move(ReadX(ref data), ReadY(ref data));
                    break;

                case PinchZoomTouchUp:
                    fingers[FuzzInput.ReadByte(ref data) % fingers.Length].Up();
                    break;

                default:
                    context.EventLoop.RunUntilIdle();
                    break;
            }

            var scale = visualViewport.Scale;
            FuzzAssert.True(scale >= 1 - PinchZoomTolerance && scale <= 5 + PinchZoomTolerance, $"The scale {scale} is not between 1 and 5");

            var layoutWidth = root.ClientWidth;
            var layoutHeight = root.ClientHeight;
            FuzzAssert.True(Math.Abs((visualViewport.Width * scale) - layoutWidth) <= PinchZoomTolerance, $"The width {visualViewport.Width} at the scale {scale} is not the width of the layout viewport {layoutWidth}");
            FuzzAssert.True(Math.Abs((visualViewport.Height * scale) - layoutHeight) <= PinchZoomTolerance, $"The height {visualViewport.Height} at the scale {scale} is not the height of the layout viewport {layoutHeight}");
            FuzzAssert.True(visualViewport.OffsetLeft >= 0 && visualViewport.OffsetLeft + visualViewport.Width <= layoutWidth + PinchZoomTolerance, $"The visual viewport at {visualViewport.OffsetLeft} is outside of the layout viewport");
            FuzzAssert.True(visualViewport.OffsetTop >= 0 && visualViewport.OffsetTop + visualViewport.Height <= layoutHeight + PinchZoomTolerance, $"The visual viewport at {visualViewport.OffsetTop} is outside of the layout viewport");
        }

        foreach (var finger in fingers)
        {
            finger.Up();
        }

        context.EventLoop.RunUntilIdle();

        // The positions cover the viewport (800x600) and a margin outside of it
        static double ReadX(ref ReadOnlySpan<byte> data) => FuzzInput.ReadByte(ref data) * 4d;

        static double ReadY(ref ReadOnlySpan<byte> data) => FuzzInput.ReadByte(ref data) * 3d;

        static double ReadDelta(ref ReadOnlySpan<byte> data) => (sbyte)FuzzInput.ReadByte(ref data) * 8d;
    }

    /// <summary>The seeds of the pinch zoom target: the gestures of the unit tests of the pinch zoom (<c>UserScrollingTests</c>).</summary>
    private static IEnumerable<FuzzSeed> GetPinchZoomSeeds()
    {
        byte[][] scenarios =
        [
            // A pinch of the touchpad at the center of the viewport, zoomed in to the maximum, then out
            [PinchZoomPinch, 100, 100, 63, PinchZoomFrame, PinchZoomPinch, 100, 100, 255, PinchZoomPinch, 100, 100, 0],

            // The wheel scrolls the zoomed visual viewport, then the layout viewport
            [PinchZoomPinch, 0, 0, 63, PinchZoomWheel, 2, 2, 0, 63, PinchZoomFrame, PinchZoomWheel, 2, 2, 0, 0xC0],

            // Two fingers move apart (a pinch), then are released
            [PinchZoomTouchDown, 0, 75, 170, PinchZoomTouchDown, 1, 125, 170, PinchZoomTouchMove, 1, 175, 170, PinchZoomFrame, PinchZoomTouchUp, 1, PinchZoomTouchUp, 0, PinchZoomFrame],

            // Two fingers in the area where touch-action prevents the pinch zoom, then a pan of one finger
            [PinchZoomTouchDown, 0, 75, 100, PinchZoomTouchDown, 1, 125, 100, PinchZoomTouchMove, 1, 175, 100, PinchZoomTouchUp, 1, PinchZoomTouchMove, 0, 75, 20, PinchZoomTouchUp, 0],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed($"scenario-{i}", scenarios[i]);
        }
    }
}
