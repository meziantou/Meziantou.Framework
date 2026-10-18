using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.WebVtt;

// The rendering of the WebVTT cues (https://w3c.github.io/webvtt/#rendering). The cues displayed over a video element are
// rendered by the layout engine of the library:
// - the tree of WebVTT node objects of a cue is mirrored by a tree of elements (WebVttElement) that are not in the document
//   and never exposed to the scripts: the root of the list of node objects, an element per internal node object, and text
//   nodes; a region is an element whose children are the roots of its cues;
// - the styles of the elements are computed by the style engine with the cascade of the WebVTT specification (the
//   properties of https://w3c.github.io/webvtt/#applying-css-properties at the user agent level, then the ::cue, ::cue() and
//   ::cue-region rules that match the video element, then the rules of the STYLE blocks of the WebVTT file);
// - the box tree builder generates the boxes of the elements as children of the replaced box of the video element, and
//   WebVttCueLayout lays them out and positions them in the rendering area of the video (the processing model of
//   https://w3c.github.io/webvtt/#processing-model and https://w3c.github.io/webvtt/#processing-cue-settings).

/// <summary>The kinds of the elements that mirror the WebVTT objects that are rendered.</summary>
internal enum WebVttElementKind
{
    /// <summary>The (root) list of WebVTT node objects of a cue.</summary>
    CueRoot,

    /// <summary>A WebVTT region object.</summary>
    Region,

    /// <summary>A WebVTT internal node object.</summary>
    Node,
}

/// <summary>
/// An element that stands for a WebVTT object in the rendering of the cues of a video element: the root list of WebVTT node
/// objects of a cue, a WebVTT internal node object or a WebVTT region object
/// (https://w3c.github.io/webvtt/#obtaining-css-boxes: "WebVTT Internal Node Objects are equivalent to elements with the
/// same contents"). The elements are in no namespace, are not in the document, and inherit from the video element.
/// </summary>
internal sealed class WebVttElement : Element
{
    /// <summary>
    /// The pseudo-element of the WebVTT cue background box (https://w3c.github.io/webvtt/#webvtt-cue-background-box): the
    /// anonymous inline box that wraps the children of the root of a cue, to which the background of <c>::cue</c> applies.
    /// </summary>
    internal const string BackgroundBoxPseudoElement = "-internal-cue-background";

    public WebVttElement(HtmlVideoElement host, WebVttElementKind kind, string localName)
        : base(host.NodeDocument, localName, @namespace: null, prefix: null)
    {
        Host = host;
        Kind = kind;
    }

    /// <summary>The video element the object is rendered for: the originating element of the pseudo-elements that match it.</summary>
    public HtmlVideoElement Host { get; }

    public WebVttElementKind Kind { get; }

    /// <summary>The concrete class of the WebVTT internal node object.</summary>
    public WebVttNodeKind NodeKind { get; init; }

    /// <summary>The text track of the cue (the rules of the STYLE blocks of its WebVTT file apply to the object), or null.</summary>
    public TextTrack? Track { get; init; }

    /// <summary>The cue of a root list of WebVTT node objects.</summary>
    public VttCue? Cue { get; init; }

    /// <summary>The region of a WebVTT region object, or the region the root of a cue is rendered in.</summary>
    public VttRegion? Region { get; init; }

    /// <summary>The applicable language of the WebVTT internal node object, or null.</summary>
    public string? Language { get; init; }

    /// <summary>The declarations of the properties the WebVTT specification sets on the object at the user agent level.</summary>
    public string UserAgentStyle { get; set; } = "";

    /// <summary>The declarations of the properties the WebVTT specification sets on the WebVTT cue background box of the root of a cue.</summary>
    public const string BackgroundBoxUserAgentStyle = "background: rgba(0, 0, 0, 0.8)";

    /// <summary>The smallest time of the WebVTT timestamp objects that are entirely after the object in the list of node objects of its cue.</summary>
    public double PastThreshold { get; set; } = double.PositiveInfinity;

    /// <summary>The largest time of the WebVTT timestamp objects that are entirely before the object in the list of node objects of its cue.</summary>
    public double FutureThreshold { get; set; } = double.NegativeInfinity;

    /// <summary>
    /// Whether the object is in the past (https://w3c.github.io/webvtt/#the-past-and-future-pseudo-classes): there is a WebVTT
    /// timestamp object whose value is less than the current playback position of the media element entirely after it.
    /// </summary>
    public bool IsPast => PastThreshold < Host.CuePlaybackPosition;

    /// <summary>Whether the object is in the future: there is a WebVTT timestamp object whose value is greater than the current playback position entirely before it.</summary>
    public bool IsFuture => FutureThreshold > Host.CuePlaybackPosition;
}

/// <summary>
/// The text track cue display state of a WebVTT cue (https://html.spec.whatwg.org/multipage/media.html#text-track-cue-display-state):
/// the boxes of the cue (the elements they are generated from) and their position, kept while the cue is displayed so that
/// the cue stays at a consistent position.
/// </summary>
internal sealed class WebVttCueDisplayState
{
    /// <summary>The root list of WebVTT node objects of the cue.</summary>
    public required WebVttElement Root { get; init; }

    /// <summary>The region the cue was rendered in, or null.</summary>
    public VttRegion? Region { get; init; }

    /// <summary>The size of the box of the cue in the axis of its writing direction, a percentage of the rendering area of the video.</summary>
    public double Size { get; init; }

    /// <summary>The position of the cue box in the axis of its writing direction before it is adjusted, a percentage of the rendering area.</summary>
    public double PositionOffset { get; init; }

    /// <summary>The position of the box of the cue in the rendering area of the video, once the positions of its boxes are adjusted; null before.</summary>
    public (double X, double Y)? Position { get; set; }

    /// <summary>
    /// Whether the boxes of the cue were added to the output of the rules for updating the display (the layout placed them):
    /// they are added first by the next runs of the rules, before the boxes of the cues that are not displayed yet.
    /// </summary>
    public bool IsDisplayed { get; set; }

    /// <summary>Whether the cue is not displayed: it has no line box, or no position where it does not overlap the other cues.</summary>
    public bool IsIgnored { get; set; }

    /// <summary>Whether the cue text contains WebVTT timestamp objects (the :past and :future pseudo-classes depend on the playback position).</summary>
    public bool HasTimestamps { get; init; }
}

/// <summary>An item of the output of the rules for updating the display of WebVTT text tracks: a region, or a cue that is not in a region.</summary>
internal readonly record struct WebVttDisplayItem(WebVttElement Element, VttCue? Cue, VttRegion? Region, IReadOnlyList<VttCue> RegionCues);

/// <summary>
/// The display of the WebVTT text tracks of a video element (https://w3c.github.io/webvtt/#rules-for-updating-the-display-of-webvtt-text-tracks):
/// the elements of the cues and of the regions that are rendered over the video, for a size of its rendering area.
/// </summary>
internal sealed class WebVttDisplay(HtmlVideoElement video)
{
    private readonly List<WebVttDisplayItem> _items = [];
    private double _width = double.NaN;
    private double _height = double.NaN;

    /// <summary>The regions and the cues that are rendered, in the order of the output.</summary>
    public IReadOnlyList<WebVttDisplayItem> Items => _items;

    /// <summary>Incremented when the elements that are rendered change: the boxes of the video element are built again.</summary>
    public int Generation { get; private set; }

    /// <summary>The replaced box of the video element whose children are the boxes of <see cref="Items"/>, and their generation.</summary>
    public object? BuiltBox { get; set; }

    public int BuiltGeneration { get; set; } = -1;

    /// <summary>Whether a cue that is displayed has WebVTT timestamp objects: its styles depend on the current playback position.</summary>
    public bool HasTimestamps { get; private set; }

    /// <summary>Updates the elements that are rendered, for the last size of the rendering area (the default object size before the first layout).</summary>
    public bool UpdateWithLastSize() => Update(double.IsNaN(_width) ? 300 : _width, double.IsNaN(_height) ? 150 : _height);

    /// <summary>
    /// Updates the elements that are rendered for a rendering area: the active cues of the showing tracks whose kind is
    /// subtitles or captions, in text track cue order, and the regions of their cues. Returns whether there is something to render.
    /// </summary>
    public bool Update(double width, double height)
    {
        var sizeChanged = width != _width || height != _height;
        _width = width;
        _height = height;

        // The cues: for each showing track, the cues of its list of cues that have their active flag set
        var cues = new List<VttCue>();
        foreach (var track in video.TextTracks)
        {
            if (track.Mode != TextTrackMode.Showing || track.Kind is not (TextTrackKind.Subtitles or TextTrackKind.Captions))
                continue;

            foreach (var cue in track.CueList)
            {
                if (cue is VttCue { IsActive: true } vttCue)
                {
                    cues.Add(vttCue);
                }
            }
        }

        var items = new List<WebVttDisplayItem>();
        var hasTimestamps = false;

        // The cues that have a text track cue display state are added first (their boxes keep their position), then the
        // other cues in text track cue order
        List<VttRegion>? regions = null;
        Dictionary<VttRegion, List<VttCue>>? regionCues = null;
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var cue in cues)
            {
                // The properties of the boxes depend on the size of the rendering area (the font size is 5vh of the video
                // viewport): the display state is emptied when the size changes
                if (sizeChanged)
                {
                    cue.DisplayState = null;
                }

                // The elements of the boxes belong to the document of the video element
                if (cue.DisplayState is { } previous && !ReferenceEquals(previous.Root.NodeDocument, video.NodeDocument))
                {
                    cue.DisplayState = null;
                }

                if (cue.DisplayState is { } current && !ReferenceEquals(current.Region, GetRenderedRegion(cue)))
                {
                    cue.DisplayState = null;
                }

                if ((cue.DisplayState is { IsDisplayed: true }) != (pass == 0))
                    continue;

                var state = cue.DisplayState ??= CreateDisplayState(cue);
                hasTimestamps |= state.HasTimestamps;
                if (state.Region is { } region)
                {
                    regionCues ??= new(ReferenceEqualityComparer.Instance);
                    if (!regionCues.TryGetValue(region, out var list))
                    {
                        regionCues[region] = list = [];
                        (regions ??= []).Add(region);
                    }

                    list.Add(cue);
                }
                else
                {
                    items.Add(new WebVttDisplayItem(state.Root, cue, Region: null, []));
                }
            }
        }

        // The regions are first in the output: a region is an element whose children are the roots of its cues
        if (regions is not null)
        {
            for (var i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                var list = regionCues![region];
                items.Insert(i, new WebVttDisplayItem(GetRegionElement(region, list), Cue: null, region, list));
            }
        }

        HasTimestamps = hasTimestamps;
        if (!AreSameItems(_items, items))
        {
            _items.Clear();
            _items.AddRange(items);
            Generation++;
        }

        return _items.Count > 0;
    }

    private static bool AreSameItems(List<WebVttDisplayItem> x, List<WebVttDisplayItem> y)
    {
        if (x.Count != y.Count)
            return false;

        for (var i = 0; i < x.Count; i++)
        {
            if (!ReferenceEquals(x[i].Element, y[i].Element))
                return false;
        }

        return true;
    }

    /// <summary>The region a cue is rendered in: its region when the region has an identifier and is a region of the track of the cue.</summary>
    private static VttRegion? GetRenderedRegion(VttCue cue) => cue.Region is { Id.Length: > 0 } region && cue.Vertical == DirectionSetting.Horizontal ? region : null;

    // ----------------------------------------------------------------------------------------------------------------
    // The elements of the cues
    // ----------------------------------------------------------------------------------------------------------------

    private WebVttCueDisplayState CreateDisplayState(VttCue cue)
    {
        var track = cue.Track;
        var language = track?.Language;
        var nodes = WebVttCueTextParser.Parse(cue.Text, string.IsNullOrEmpty(language) ? null : language);
        var region = GetRenderedRegion(cue);
        var root = new WebVttElement(video, WebVttElementKind.CueRoot, "#cue") { Cue = cue, Track = track, Region = region, Language = nodes.Language };
        if (cue.Id.Length > 0)
        {
            root.SetAttribute("id", cue.Id);
        }

        if (!string.IsNullOrEmpty(nodes.Language))
        {
            root.SetAttribute("lang", nodes.Language);
        }

        // The times of the timestamp objects, in a pre-order, depth-first traversal of the list of node objects
        var timestamps = new List<double>();
        var text = new StringBuilder();
        AppendChildren(root, nodes, track, timestamps, text);
        if (timestamps.Count > 0)
        {
            // The largest time before each timestamp, and the smallest time after it
            var index = 0;
            SetTimeThresholds(root, timestamps, ref index);
        }

        // The size and the position of the cue box in the axis of the writing direction (https://w3c.github.io/webvtt/#processing-cue-settings)
        var isLeftToRight = GetBaseDirectionIsLeftToRight(text);
        var position = cue.ComputedPosition;
        var positionAlign = cue.GetComputedPositionAlign(isLeftToRight);
        var maximumSize = positionAlign switch
        {
            PositionAlignSetting.LineLeft => 100 - position,
            PositionAlignSetting.LineRight => position,
            _ => position <= 50 ? position * 2 : (100 - position) * 2,
        };

        var size = Math.Min(cue.Size, maximumSize);
        var offset = positionAlign switch
        {
            PositionAlignSetting.LineLeft => position,
            PositionAlignSetting.LineRight => position - size,
            _ => position - (size / 2),
        };

        var textAlign = cue.Align switch
        {
            AlignSetting.Start => "start",
            AlignSetting.End => "end",
            AlignSetting.Left => "left",
            AlignSetting.Right => "right",
            _ => "center",
        };

        if (region is not null)
        {
            // The children of a WebVTT region object: the offset is a percentage of the width of the region
            var left = position * region.Width / 100;
            if (positionAlign == PositionAlignSetting.Center)
            {
                left -= region.Width / 2;
            }
            else if (positionAlign == PositionAlignSetting.LineRight)
            {
                left -= region.Width;
            }

            root.UserAgentStyle = string.Create(CultureInfo.InvariantCulture, $"display: block; position: relative; unicode-bidi: plaintext; width: auto; left: {left}%; text-align: {textAlign}; pointer-events: none");
        }
        else
        {
            var writingMode = cue.Vertical switch
            {
                DirectionSetting.Rl => "vertical-rl",
                DirectionSetting.Lr => "vertical-lr",
                _ => "horizontal-tb",
            };

            // The font is 5vh of the video viewport (the rendering area of the video)
            root.UserAgentStyle = string.Create(CultureInfo.InvariantCulture, $"display: block; position: absolute; unicode-bidi: plaintext; writing-mode: {writingMode}; overflow-wrap: break-word; text-wrap: balance; text-align: {textAlign}; font: {_height * 0.05}px sans-serif; color: rgba(255, 255, 255, 1); white-space: pre-line; pointer-events: none");
        }

        return new WebVttCueDisplayState { Root = root, Region = region, Size = size, PositionOffset = offset, HasTimestamps = timestamps.Count > 0 };
    }

    private void AppendChildren(WebVttElement parent, WebVttNode node, TextTrack? track, List<double> timestamps, StringBuilder text)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == WebVttNodeKind.Text)
            {
                if (child.Value.Length > 0)
                {
                    parent.AppendChild(parent.NodeDocument.CreateTextNode(child.Value));
                    text.Append(child.Value);
                }

                continue;
            }

            if (child.Kind == WebVttNodeKind.Timestamp)
            {
                // A timestamp object generates no box: a marker keeps its place in the traversal
                timestamps.Add(child.Time);
                parent.AppendChild(parent.NodeDocument.CreateComment(""));
                continue;
            }

            var element = new WebVttElement(video, WebVttElementKind.Node, child.SelectorName!) { NodeKind = child.Kind, Track = track, Language = child.Language, UserAgentStyle = GetUserAgentStyle(child) };
            if (child.Classes.Count > 0)
            {
                element.SetAttribute("class", string.Join(' ', child.Classes));
            }

            if (child.Kind == WebVttNodeKind.Voice)
            {
                element.SetAttribute("voice", child.Value);
            }
            else if (child.Kind == WebVttNodeKind.Language)
            {
                element.SetAttribute("lang", child.Language ?? "");
            }

            parent.AppendChild(element);
            AppendChildren(element, child, track, timestamps, text);
        }
    }

    /// <summary>
    /// Sets the thresholds of the :past and :future pseudo-classes of the elements of a cue: the comments mark the
    /// timestamp objects, in the order of <paramref name="timestamps"/>.
    /// </summary>
    private static void SetTimeThresholds(Node node, List<double> timestamps, ref int index)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Comment)
            {
                index++;
                continue;
            }

            if (child is not WebVttElement element)
                continue;

            // The timestamps entirely before the element
            for (var i = 0; i < index; i++)
            {
                element.FutureThreshold = Math.Max(element.FutureThreshold, timestamps[i]);
            }

            SetTimeThresholds(element, timestamps, ref index);

            // The timestamps entirely after the element
            for (var i = index; i < timestamps.Count; i++)
            {
                element.PastThreshold = Math.Min(element.PastThreshold, timestamps[i]);
            }
        }
    }

    /// <summary>The properties of https://w3c.github.io/webvtt/#applying-css-properties and of the default classes (https://w3c.github.io/webvtt/#default-classes).</summary>
    private static string GetUserAgentStyle(WebVttNode node)
    {
        var style = node.Kind switch
        {
            WebVttNodeKind.Italic => "font-style: italic",
            WebVttNodeKind.Bold => "font-weight: bold",
            WebVttNodeKind.Underline => "text-decoration: underline",
            WebVttNodeKind.Ruby => "display: ruby",
            WebVttNodeKind.RubyText => "display: ruby-text; background: rgba(0, 0, 0, 0.8)",
            _ => "",
        };

        if (node.Classes.Count == 0)
            return style;

        // The order of appearance determines the cascade of the color and background classes
        var builder = new StringBuilder(style);
        foreach (var name in node.Classes)
        {
            var isBackground = name.StartsWith("bg_", StringComparison.Ordinal);
            var color = (isBackground ? name.AsSpan(3) : name) switch
            {
                "white" => "rgba(255, 255, 255, 1)",
                "lime" => "rgba(0, 255, 0, 1)",
                "cyan" => "rgba(0, 255, 255, 1)",
                "red" => "rgba(255, 0, 0, 1)",
                "yellow" => "rgba(255, 255, 0, 1)",
                "magenta" => "rgba(255, 0, 255, 1)",
                "blue" => "rgba(0, 0, 255, 1)",
                "black" => "rgba(0, 0, 0, 1)",
                _ => null,
            };

            if (color is not null)
            {
                builder.Append(builder.Length > 0 ? "; " : "").Append(isBackground ? "background-color: " : "color: ").Append(color);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the base direction of the cue text is left-to-right: the paragraph embedding level of the first paragraph of
    /// the text, determined by its first strong character (the <c>unicode-bidi: plaintext</c> of the cue box).
    /// </summary>
    private static bool GetBaseDirectionIsLeftToRight(StringBuilder text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            switch (UnicodeProperties.GetBidiClass(codePoint))
            {
                case BidiClass.L:
                    return true;

                case BidiClass.R or BidiClass.AL:
                    return false;

                case BidiClass.B:
                    // The end of the first paragraph
                    return true;
            }
        }

        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The elements of the regions
    // ----------------------------------------------------------------------------------------------------------------

    private Dictionary<VttRegion, (WebVttElement Element, VttCue[] Cues)>? _regionElements;

    /// <summary>Returns the element of a WebVTT region object, whose children are the roots of the cues of the region.</summary>
    private WebVttElement GetRegionElement(VttRegion region, List<VttCue> cues)
    {
        _regionElements ??= new(ReferenceEqualityComparer.Instance);
        if (_regionElements.TryGetValue(region, out var existing) && existing.Cues.AsSpan().SequenceEqual(cues.ToArray()) && existing.Element.UserAgentStyle == GetRegionStyle(region))
        {
            var same = true;
            var child = existing.Element.FirstChild;
            foreach (var cue in cues)
            {
                same &= ReferenceEquals(child, cue.DisplayState!.Root);
                child = child?.NextSibling;
            }

            if (same)
                return existing.Element;
        }

        var element = new WebVttElement(video, WebVttElementKind.Region, "#region") { Region = region, Track = cues[0].Track, UserAgentStyle = GetRegionStyle(region) };
        element.SetAttribute("id", region.Id);
        foreach (var cue in cues)
        {
            element.AppendChild(cue.DisplayState!.Root);
        }

        _regionElements[region] = (element, cues.ToArray());
        return element;
    }

    /// <summary>The properties of a WebVTT region object: its lines are 6vh of the video viewport, its font 5vh.</summary>
    private string GetRegionStyle(VttRegion region)
        => string.Create(CultureInfo.InvariantCulture, $"display: flex; position: absolute; writing-mode: horizontal-tb; background: rgba(0, 0, 0, 0.8); overflow-wrap: break-word; font: {_height * 0.05}px sans-serif; color: rgba(255, 255, 255, 1); overflow: hidden; min-height: 0px; max-height: {_height * 0.06 * region.Lines}px; flex-flow: column; justify-content: flex-end; pointer-events: none");
}
