using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

#pragma warning disable WEBPLATEXP0022 // The scroll performance timing is a proposal

namespace Meziantou.Framework.WebPlatform.Fuzz;

public static partial class WebPlatformFuzzTargets
{
    // The operations of the user scroll target: an operation byte, then its arguments (one byte each). The first byte of the
    // input tells whether the scrolls of the user are smooth.
    private const byte UserScrollWheel = 0; // x, y, deltaX, deltaY
    private const byte UserScrollKey = 1; // key
    private const byte UserScrollTouchDown = 2; // x, y
    private const byte UserScrollTouchMove = 3; // x, y
    private const byte UserScrollTouchUp = 4;
    private const byte UserScrollPenMove = 5; // x, y
    private const byte UserScrollInkStyle = 6; // color, diameter
    private const byte UserScrollFrame = 7; // frames
    private const byte UserScrollFocus = 8;
    private const int UserScrollOperationCount = 9;

    /// <summary>The maximum number of operations of an input of the user scroll target.</summary>
    private const int UserScrollMaxOperations = 64;

    /// <summary>
    /// The page of the user scroll target: a focusable scroll container in the presentation area of the ink trails, in a
    /// document taller than the viewport.
    /// </summary>
    private const string UserScrollDocumentMarkup = """
        <!DOCTYPE html>
        <style>body { margin: 0 } #scroller { width: 200px; height: 200px; overflow: auto } #content { width: 400px; height: 1000px }</style>
        <div id="area"><div id="scroller" tabindex="0"><div id="content"></div></div></div>
        <div style="height: 2000px"></div>
        """;

    private static readonly string[] UserScrollKeys = ["ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "PageDown", "PageUp", "Home", "End", " ", "a"];

    // The colors of the ink trails, and whether they are valid CSS colors
    private static readonly (string Color, bool IsValid)[] UserScrollInkColors =
    [
        ("green", true), ("#0f08", true), ("rgb(1 2 3 / 50%)", true), ("transparent", true), ("currentcolor", true), ("color-mix(in oklab, red, blue)", true),
        ("bad-color", false), ("", false), ("rgb(1, 2)", false), ("red blue", false),
    ];

    private static readonly double[] UserScrollInkDiameters = [6, 0.5, 0, -0d, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300, double.Epsilon];

    // (a property: the targets of the other part of the class read it when they are initialized, before the fields of this part)
    private static string[] UserScrollTokens => ["\u0000\u0010\u0010\u0000\u0008", "\u0001\u0000", "\u0001\u0004", "\u0002\u0010\u0010", "\u0003\u0010 ", "\u0004", "\u0005\u0010\u0010", "\u0006\u0000\u0000", "\u0007\u0008", "\u0008"];

    /// <summary>
    /// Plays a sequence of wheel rotations, keys, touches and moves of a pen that start delegated ink trails (see the
    /// operations above), and checks the scroll performance timing: the entries have valid attributes and, once the scrolls
    /// ended, the deltas of the entries of a scrolling box add up to its scroll position. The ink presenter throws only the
    /// errors of its arguments: <c>NotSupportedError</c> for a diameter that is not greater than 0, a <c>TypeError</c> for an
    /// invalid color.
    /// </summary>
    public static void UserScroll(ReadOnlySpan<byte> data)
    {
        var smooth = !data.IsEmpty && (FuzzInput.ReadByte(ref data) & 1) != 0;
        var clock = new SteppedTimeProvider();
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = clock, Viewport = new ViewportOptions { Width = 400, Height = 300, SmoothUserScrolling = smooth } });
        var document = context.Navigate("https://example.com/", _ => UserScrollDocumentMarkup);
        context.EventLoop.RunUntilIdle();
        var window = document.DefaultView!;
        var scroller = (HtmlElement)document.GetElementById("scroller")!;
        var entries = new List<PerformanceScrollTiming>();
        new PerformanceObserver(window, (list, _, _) => entries.AddRange(list.GetEntries().Cast<PerformanceScrollTiming>())).Observe(new PerformanceObserverInit { Type = "scroll" });

        var presenter = window.Navigator.Ink.RequestPresenter(new InkPresenterParam { PresentationArea = document.GetElementById("area") }).Result;
        var (color, colorIsValid) = UserScrollInkColors[0];
        var diameter = UserScrollInkDiameters[0];
        document.AddEventListener("pointermove", e =>
        {
            if (e is not PointerEvent { PointerType: "pen" } pointerEvent)
                return;

            var style = new InkTrailStyle { Color = color, Diameter = diameter };
            try
            {
                presenter.UpdateInkTrailStartPoint(pointerEvent, style);
                FuzzAssert.True(colorIsValid && diameter > 0, $"The style {FuzzAssert.Format(color)} {diameter} is accepted");
            }
            catch (TypeErrorException)
            {
                FuzzAssert.True(!colorIsValid, $"The valid color {FuzzAssert.Format(color)} is rejected");
            }
            catch (DomException exception) when (exception.Name == "NotSupportedError")
            {
                FuzzAssert.True(colorIsValid && !(diameter > 0), $"The diameter {diameter} is rejected");
            }
        });

        var input = context.Input;
        var finger = input.CreatePointer("touch");
        var pen = input.CreatePointer("pen");
        for (var operation = 0; operation < UserScrollMaxOperations && !data.IsEmpty; operation++)
        {
            switch (FuzzInput.ReadByte(ref data) % UserScrollOperationCount)
            {
                case UserScrollWheel:
                    input.Wheel(ReadX(ref data), ReadY(ref data), ReadDelta(ref data), ReadDelta(ref data));
                    break;

                case UserScrollKey:
                    input.Keyboard.Press(UserScrollKeys[FuzzInput.ReadByte(ref data) % UserScrollKeys.Length]);
                    break;

                case UserScrollTouchDown:
                    finger.Move(ReadX(ref data), ReadY(ref data));
                    finger.Down();
                    break;

                case UserScrollTouchMove:
                    finger.Move(ReadX(ref data), ReadY(ref data));
                    break;

                case UserScrollTouchUp:
                    finger.Up();
                    break;

                case UserScrollPenMove:
                    pen.Move(ReadX(ref data), ReadY(ref data));
                    break;

                case UserScrollInkStyle:
                    (color, colorIsValid) = UserScrollInkColors[FuzzInput.ReadByte(ref data) % UserScrollInkColors.Length];
                    diameter = UserScrollInkDiameters[FuzzInput.ReadByte(ref data) % UserScrollInkDiameters.Length];
                    break;

                case UserScrollFocus:
                    scroller.Focus(new FocusOptions { PreventScroll = true });
                    break;

                default:
                    RunFrames(FuzzInput.ReadByte(ref data) % 16);
                    break;
            }

            CheckEntries();
        }

        // The scrolls end: the gestures are released, and the smooth scrolls and the wheel gestures complete
        finger.Up();
        RunFrames(60);
        CheckEntries();
        CheckDeltas(scroller, scroller.ScrollLeft, scroller.ScrollTop);
        CheckDeltas(document, window.ScrollX, window.ScrollY);

        void RunFrames(int count)
        {
            context.EventLoop.RunUntilIdle();
            for (var i = 0; i < count; i++)
            {
                clock.Advance(17);
                context.EventLoop.RunUntilIdle();
            }
        }

        void CheckEntries()
        {
            foreach (var entry in entries)
            {
                FuzzAssert.True(entry is { Name: "scroll", EntryType: "scroll", ScrollSource: "wheel" or "keyboard" or "touch" or "other", CheckerboardTime: 0 }, $"The entry {entry.ScrollSource} is not valid");
                FuzzAssert.True(entry.Duration >= 0 && entry.FramesExpected >= entry.FramesProduced, $"The entry lasts {entry.Duration} ms with {entry.FramesProduced}/{entry.FramesExpected} frames");
                FuzzAssert.True(entry.FramesProduced == 0 || entry.FirstFrameTime >= entry.StartTime, $"The first frame {entry.FirstFrameTime} is before the start {entry.StartTime}");
                FuzzAssert.True(entry.DeltaX != 0 || entry.DeltaY != 0 || entry.FramesProduced > 0, "The entry of a scroll that did not move is reported");
            }
        }

        void CheckDeltas(Node target, double scrollX, double scrollY)
        {
            var targetEntries = entries.Where(entry => ReferenceEquals(entry.Target, target)).ToList();
            var tolerance = 0.5 * (targetEntries.Count + 1);
            var deltaX = targetEntries.Sum(entry => (double)entry.DeltaX);
            var deltaY = targetEntries.Sum(entry => (double)entry.DeltaY);
            FuzzAssert.True(Math.Abs(deltaX - scrollX) <= tolerance && Math.Abs(deltaY - scrollY) <= tolerance, $"The deltas ({deltaX}, {deltaY}) of {targetEntries.Count} entries are not the scroll position ({scrollX}, {scrollY})");
        }

        // The positions cover the viewport (400x300) and a margin outside of it
        static double ReadX(ref ReadOnlySpan<byte> data) => FuzzInput.ReadByte(ref data) * 2d;

        static double ReadY(ref ReadOnlySpan<byte> data) => FuzzInput.ReadByte(ref data) * 1.5;

        static double ReadDelta(ref ReadOnlySpan<byte> data) => (sbyte)FuzzInput.ReadByte(ref data) * 4d;
    }

    /// <summary>The seeds of the user scroll target: the scrolls of the unit tests of the scroll timing (<c>UserScrollingTests</c>) and of the delegated ink trails.</summary>
    private static IEnumerable<FuzzSeed> GetUserScrollSeeds()
    {
        byte[][] scenarios =
        [
            // A wheel scrolls the scroll container, then the viewport
            [0, UserScrollWheel, 25, 33, 0, 10, UserScrollFrame, 1, UserScrollWheel, 25, 160, 0, 25, UserScrollFrame, 1],

            // A smooth wheel gesture, then a wheel in the opposite direction
            [1, UserScrollWheel, 25, 33, 0, 10, UserScrollFrame, 2, UserScrollWheel, 25, 33, 0, 10, UserScrollFrame, 2, UserScrollWheel, 25, 33, 0, 0xFE, UserScrollFrame, 10],

            // The keys scroll the focused scroll container (smoothly)
            [1, UserScrollFocus, UserScrollKey, 0, UserScrollFrame, 3, UserScrollKey, 0, UserScrollFrame, 15, UserScrollKey, 7, UserScrollFrame, 15, UserScrollKey, 1],

            // A touch pans the scroll container down and up
            [0, UserScrollTouchDown, 25, 100, UserScrollTouchMove, 25, 60, UserScrollTouchMove, 25, 20, UserScrollTouchMove, 25, 80, UserScrollTouchUp, UserScrollFrame, 1],

            // A pen draws ink trails with valid and invalid styles
            [0, UserScrollPenMove, 10, 10, UserScrollPenMove, 40, 10, UserScrollFrame, 1, UserScrollInkStyle, 6, 0, UserScrollPenMove, 50, 20, UserScrollInkStyle, 0, 3, UserScrollPenMove, 60, 20, UserScrollInkStyle, 0, 6, UserScrollPenMove, 70, 20, UserScrollPenMove, 80, 30],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed($"scenario-{i}", scenarios[i]);
        }
    }

    /// <summary>A clock that the target advances: the frames of the event loop and the end of the wheel gestures depend on the time.</summary>
    private sealed class SteppedTimeProvider : TimeProvider
    {
        private long _milliseconds = 1000;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _milliseconds;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(_milliseconds);

        public void Advance(long milliseconds) => _milliseconds += milliseconds;
    }
}
