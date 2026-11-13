using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>The state of the animated images of an element given by <c>image-animation</c>.</summary>
internal enum ImageAnimationMode : byte
{
    /// <summary><c>normal</c>: the images play on the timeline shared by the elements that display them.</summary>
    Normal,

    /// <summary><c>running</c>: the images play on a timeline of the element.</summary>
    Running,

    /// <summary><c>paused</c>: the images display the frame displayed when the value started to apply.</summary>
    Paused,

    /// <summary><c>stopped</c>: the images display their first frame.</summary>
    Stopped,

    /// <summary>The element is not rendered (<c>display: none</c>): its animations are reset.</summary>
    NotRendered,
}

/// <summary>The position of the animation of an image displayed by an element (see <see cref="ImageAnimationTimelines.GetPlayback"/>).</summary>
/// <param name="Position">The position in the animation, in milliseconds from its start.</param>
/// <param name="IsPlaying">Whether the animation plays (the position changes with the time).</param>
internal readonly record struct ImageAnimationPlayback(double Position, bool IsPlaying);

/// <summary>
/// The timelines of the animated images of the elements whose <c>image-animation</c> is not <c>normal</c>
/// (https://drafts.csswg.org/css-image-animation-1/#image-animation). The style engine records the changes of the value of
/// each element (and of its display: an element that is not rendered resets its animations) when its style is computed, on the
/// clock of the event loop; the painter replays them for each image the element displays, from the time the image became
/// available, to find the position of its animation:
/// <list type="bullet">
/// <item><c>normal</c>: the timeline shared by all the elements that display the image (the time since it became available);</item>
/// <item><c>running</c>: a timeline of the element, started when the image is added (or when the element is rendered again),
/// or continuing from the frame displayed when the value changed to <c>running</c>;</item>
/// <item><c>paused</c>: the frame displayed when the value changed to <c>paused</c> (the first frame when the animation was not
/// playing);</item>
/// <item><c>stopped</c>: the first frame.</item>
/// </list>
/// The elements whose value was always <c>normal</c> have no timeline.
/// </summary>
internal sealed class ImageAnimationTimelines
{
    /// <summary>The number of changes kept per element (an image displayed later replays the changes that are kept).</summary>
    private const int MaxChanges = 32;

    private readonly ConditionalWeakTable<Element, Dictionary<string, Timeline>> _elements = [];

    /// <summary>Synchronizes the style engine, which records the changes, and the painters, which replay them.</summary>
    private readonly Lock _lock = new();

    /// <summary>Returns the mode given by the computed value of <c>image-animation</c> of a style, or <see cref="ImageAnimationMode.NotRendered"/> when it is not rendered.</summary>
    public static ImageAnimationMode GetMode(ComputedStyle style)
    {
        if (style.IsDisplayNoneForAnimations())
            return ImageAnimationMode.NotRendered;

        return style.GetComputedValue("image-animation") switch
        {
            "running" => ImageAnimationMode.Running,
            "paused" => ImageAnimationMode.Paused,
            "stopped" => ImageAnimationMode.Stopped,
            _ => ImageAnimationMode.Normal,
        };
    }

    /// <summary>
    /// Records the value of <c>image-animation</c> of a style computed now: a change of the value of the element (or of the
    /// pseudo-element) starts a new segment of its timeline.
    /// </summary>
    /// <param name="style">The computed style.</param>
    /// <param name="beforeChange">The style of the element before the change, or null when it had none (e.g. it was inserted).</param>
    /// <param name="clock">The clock of the event loop of the document.</param>
    public void OnStyleComputed(ComputedStyle style, ComputedStyle? beforeChange, TimeProvider clock)
    {
        var mode = GetMode(style);
        lock (_lock)
        {
            OnStyleComputedCore(style, mode, beforeChange, clock);
        }
    }

    private void OnStyleComputedCore(ComputedStyle style, ImageAnimationMode mode, ComputedStyle? beforeChange, TimeProvider clock)
    {
        var key = style.PseudoElement ?? "";
        Timeline? timeline = null;
        if (_elements.TryGetValue(style.Element, out var timelines))
        {
            timelines.TryGetValue(key, out timeline);
        }

        if (timeline is null)
        {
            // The elements that never had another value than normal have no timeline
            if (mode is ImageAnimationMode.Normal or ImageAnimationMode.NotRendered)
                return;

            timelines ??= _elements.GetOrCreateValue(style.Element);
            timeline = new Timeline();
            timelines[key] = timeline;

            // The value of an element without style before the change applies since the images were added; otherwise, the
            // previous value applied until now
            if (beforeChange is null)
            {
                timeline.Add(long.MinValue, mode);
                return;
            }

            timeline.Add(long.MinValue, GetMode(beforeChange));
        }

        if (timeline.Mode != mode)
        {
            timeline.Add(clock.GetTimestamp(), mode);
        }
    }

    /// <summary>
    /// Returns the position of the animation of an image displayed by the element (or the pseudo-element) of a style now, or
    /// null when it plays on the timeline shared by the elements (<c>normal</c>, the default) or has no clock.
    /// </summary>
    /// <param name="style">The style of the element that displays the image.</param>
    /// <param name="image">The image (any variant of its resource).</param>
    public ImageAnimationPlayback? GetPlayback(ComputedStyle style, PaintImage image)
    {
        if (image.AnimationClock is not ({ } clock, var start))
            return null;

        lock (_lock)
        {
            if (!_elements.TryGetValue(style.Element, out var timelines) || !timelines.TryGetValue(style.PseudoElement ?? "", out var timeline))
                return null;

            var state = timeline.GetImageState(image.Root, clock, start);
            return state.Kind switch
            {
                PlaybackKind.Shared => null,
                PlaybackKind.Running => new ImageAnimationPlayback(ToMilliseconds(clock, clock.GetTimestamp()) - state.Value, IsPlaying: true),
                _ => new ImageAnimationPlayback(state.Value, IsPlaying: false),
            };
        }
    }

    private static double ToMilliseconds(TimeProvider clock, long timestamp) => timestamp * 1000.0 / clock.TimestampFrequency;

    private enum PlaybackKind : byte
    {
        /// <summary>The timeline shared by the elements (the time since the image became available).</summary>
        Shared,

        /// <summary>A timeline of the element: <see cref="ImageState.Value"/> is its origin, in milliseconds on the clock.</summary>
        Running,

        /// <summary>A frozen position: <see cref="ImageState.Value"/> is the position, in milliseconds.</summary>
        Frozen,
    }

    /// <summary>The state of the animation of an image of an element after the changes it saw (up to <see cref="Sequence"/>).</summary>
    private sealed class ImageState
    {
        public PlaybackKind Kind { get; set; }

        public double Value { get; set; }

        public long Sequence { get; set; }
    }

    /// <summary>A change of the mode of an element at a timestamp of the clock (<see cref="long.MinValue"/>: since the element is styled).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Change(long Sequence, long Timestamp, ImageAnimationMode Mode);

    /// <summary>The changes of the mode of an element and the states of the animations of its images.</summary>
    private sealed class Timeline
    {
        private readonly List<Change> _changes = [];
        private readonly ConditionalWeakTable<PaintImage, ImageState> _images = [];
        private long _nextSequence;

        public ImageAnimationMode Mode { get; private set; }

        public void Add(long timestamp, ImageAnimationMode mode)
        {
            if (_changes.Count == MaxChanges)
            {
                _changes.RemoveAt(0);
            }

            _changes.Add(new Change(_nextSequence++, timestamp, mode));
            Mode = mode;
        }

        /// <summary>Returns the state of the animation of an image after the changes of the element, which are replayed from the time the image became available.</summary>
        public ImageState GetImageState(PaintImage image, TimeProvider clock, long start)
        {
            var startTime = ToMilliseconds(clock, start);
            if (!_images.TryGetValue(image, out var state))
            {
                // The mode when the image became available (normal before the first change)
                state = new ImageState { Kind = PlaybackKind.Shared, Sequence = -1 };
                foreach (var change in _changes)
                {
                    if (change.Timestamp > start)
                        break;

                    state.Sequence = change.Sequence;
                    (state.Kind, state.Value) = change.Mode switch
                    {
                        ImageAnimationMode.Normal => (PlaybackKind.Shared, 0),
                        ImageAnimationMode.Running => (PlaybackKind.Running, startTime),
                        _ => (PlaybackKind.Frozen, 0),
                    };
                }

                _images.Add(image, state);
            }

            foreach (var change in _changes)
            {
                if (change.Sequence <= state.Sequence)
                    continue;

                var time = ToMilliseconds(clock, change.Timestamp);
                var position = state.Kind switch
                {
                    PlaybackKind.Shared => time - startTime,
                    PlaybackKind.Running => time - state.Value,
                    _ => state.Value,
                };

                (state.Kind, state.Value) = change.Mode switch
                {
                    ImageAnimationMode.Normal => (PlaybackKind.Shared, 0),

                    // "If this property is switched to running from another value, the beginning of this timeline is set so that the
                    // animation continues from the state that was displayed at the time of the switch" (an element rendered again
                    // starts from the first frame: its animation was reset)
                    ImageAnimationMode.Running => state.Kind == PlaybackKind.Running ? (state.Kind, state.Value) : (PlaybackKind.Running, time - position),

                    // "must continue to display the image as it was when this value started to apply"
                    ImageAnimationMode.Paused => (PlaybackKind.Frozen, position),

                    // stopped, and an element that is not rendered: the initial state of the animation
                    _ => (PlaybackKind.Frozen, 0),
                };
                state.Sequence = change.Sequence;
            }

            return state;
        }
    }
}
