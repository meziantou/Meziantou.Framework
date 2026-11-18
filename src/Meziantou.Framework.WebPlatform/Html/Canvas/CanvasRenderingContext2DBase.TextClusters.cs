using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Rendering;

#pragma warning disable WEBPLATEXP0024 // The members of the proposal use each other
#pragma warning disable WEBPLATEXP0025 // The members of the proposal use each other

namespace Meziantou.Framework.WebPlatform.Dom;

// The text clusters and the selection APIs of TextMetrics (https://github.com/whatwg/html/pull/11000,
// https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md) and the shaped text that
// Chromium implements behind the ExtendedTextMetrics flag: a text prepared once (measureText(), shapeText()) keeps its glyphs,
// its clusters, its font, its text styles, its direction, its alignment and its baseline, and its clusters are drawn later
// with the fill or stroke style, the transformation and the clip of the context at that time.
public abstract partial class CanvasRenderingContext2DBase
{
    private const string TextClustersHref = "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal";
    private const string ShapedTextHref = "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText";

    /// <summary>
    /// Fills a cluster of a measured text (https://github.com/whatwg/html/pull/11000) with the fill style: the glyphs of the
    /// cluster, with the font and the text styles of the measurement, are drawn with their alignment point at
    /// (<paramref name="x"/> + <see cref="TextCluster.X"/>, <paramref name="y"/> + <see cref="TextCluster.Y"/>). Drawing all the
    /// clusters of a text at the same point draws the text as <see cref="FillText"/> at that point.
    /// </summary>
    /// <param name="textCluster">The cluster, returned by <see cref="TextMetrics.GetTextClusters(TextClusterOptions?)"/>.</param>
    /// <param name="x">The x coordinate the position of the cluster is relative to.</param>
    /// <param name="y">The y coordinate the position of the cluster is relative to.</param>
    /// <param name="options">The alignment, the baseline and the position (<c>x</c>, <c>y</c>) that replace the ones of the cluster.</param>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("fillTextCluster", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public void FillTextCluster(TextCluster textCluster, double x, double y, TextClusterOptions? options = null) => DrawTextCluster(textCluster, x, y, options, stroke: false);

    /// <summary>
    /// Strokes a cluster of a measured text (https://github.com/whatwg/html/pull/11000) with the stroke style and the line
    /// styles: see <see cref="FillTextCluster"/>.
    /// </summary>
    /// <param name="textCluster">The cluster, returned by <see cref="TextMetrics.GetTextClusters(TextClusterOptions?)"/>.</param>
    /// <param name="x">The x coordinate the position of the cluster is relative to.</param>
    /// <param name="y">The y coordinate the position of the cluster is relative to.</param>
    /// <param name="options">The alignment, the baseline and the position (<c>x</c>, <c>y</c>) that replace the ones of the cluster.</param>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("strokeTextCluster", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public void StrokeTextCluster(TextCluster textCluster, double x, double y, TextClusterOptions? options = null) => DrawTextCluster(textCluster, x, y, options, stroke: true);

    /// <summary>
    /// Shapes a text with the current font and text styles: the returned <see cref="CanvasShapedText"/> gives its glyph runs
    /// and clusters, drawn later by <see cref="FillGlyphRuns"/> and <see cref="StrokeGlyphRuns"/> with the font, the text
    /// styles, the direction, the alignment and the baseline of the context at this time.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("shapeText", ShapedTextHref)]
    [Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
    public CanvasShapedText ShapeText(string text) => new(PrepareText(text));

    /// <summary>Fills a glyph run of a shaped text with the fill style, the alignment point of the shaped text at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="glyphRun">The glyph run, returned by <see cref="CanvasShapedText.GlyphRuns"/> or <see cref="CanvasShapedText.GlyphClusters()"/>.</param>
    /// <param name="x">The x coordinate of the alignment point of the shaped text.</param>
    /// <param name="y">The y coordinate of the baseline of the shaped text.</param>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("fillGlyphRun", ShapedTextHref)]
    [Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
    public void FillGlyphRun(CanvasGlyphRun glyphRun, double x, double y) => DrawGlyphRuns([glyphRun], x, y, stroke: false);

    /// <summary>Strokes a glyph run of a shaped text with the stroke style and the line styles, the alignment point of the shaped text at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="glyphRun">The glyph run, returned by <see cref="CanvasShapedText.GlyphRuns"/> or <see cref="CanvasShapedText.GlyphClusters()"/>.</param>
    /// <param name="x">The x coordinate of the alignment point of the shaped text.</param>
    /// <param name="y">The y coordinate of the baseline of the shaped text.</param>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("strokeGlyphRun", ShapedTextHref)]
    [Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
    public void StrokeGlyphRun(CanvasGlyphRun glyphRun, double x, double y) => DrawGlyphRuns([glyphRun], x, y, stroke: true);

    /// <summary>Fills glyph runs of shaped texts with the fill style, the alignment point of each shaped text at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="glyphRuns">The glyph runs, returned by <see cref="CanvasShapedText.GlyphRuns"/> or <see cref="CanvasShapedText.GlyphClusters()"/>.</param>
    /// <param name="x">The x coordinate of the alignment point of the shaped texts.</param>
    /// <param name="y">The y coordinate of the baseline of the shaped texts.</param>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("fillGlyphRuns", ShapedTextHref)]
    [Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
    public void FillGlyphRuns(IReadOnlyList<CanvasGlyphRun> glyphRuns, double x, double y) => DrawGlyphRuns(glyphRuns, x, y, stroke: false);

    /// <summary>Strokes glyph runs of shaped texts with the stroke style and the line styles, the alignment point of each shaped text at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="glyphRuns">The glyph runs, returned by <see cref="CanvasShapedText.GlyphRuns"/> or <see cref="CanvasShapedText.GlyphClusters()"/>.</param>
    /// <param name="x">The x coordinate of the alignment point of the shaped texts.</param>
    /// <param name="y">The y coordinate of the baseline of the shaped texts.</param>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("strokeGlyphRuns", ShapedTextHref)]
    [Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
    public void StrokeGlyphRuns(IReadOnlyList<CanvasGlyphRun> glyphRuns, double x, double y) => DrawGlyphRuns(glyphRuns, x, y, stroke: true);

    private void DrawTextCluster(TextCluster textCluster, double x, double y, TextClusterOptions? options, bool stroke)
    {
        var layout = textCluster.Layout;
        var cluster = layout.Clusters[textCluster.Index];
        var align = options?.Align ?? textCluster.Align;
        var baseline = options?.Baseline ?? textCluster.Baseline;
        var pointX = x + (options?.X ?? textCluster.X);
        var pointY = y + (options?.Y ?? textCluster.Y);
        if (!double.IsFinite(pointX) || !double.IsFinite(pointY))
            return;

        if (CreatePaint(stroke ? _state.StrokeStyle : _state.FillStyle, stroke) is not { } paint || Invert(_state.Transform) is null)
            return;

        // The alignment point of the cluster is at the point: the glyphs are positioned from the left of the cluster
        var originX = pointX - layout.GetAlignmentOffset(align, cluster.Width) - cluster.Left;
        var originY = pointY + layout.GetBaselineOffset(baseline);
        DrawGlyphs(layout, layout.GetGlyphs(textCluster.Index), originX, originY, scaleX: 1, paint, stroke);
    }

    private void DrawGlyphRuns(IReadOnlyList<CanvasGlyphRun> glyphRuns, double x, double y, bool stroke)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || glyphRuns.Count == 0)
            return;

        if (CreatePaint(stroke ? _state.StrokeStyle : _state.FillStyle, stroke) is not { } paint || Invert(_state.Transform) is null)
            return;

        // The runs are one drawing operation (e.g. with the copy operator, a run does not erase the previous ones)
        var runs = new List<(GlyphRun Run, FontPalette? Palette)>();
        foreach (var glyphRun in glyphRuns)
        {
            // The position of a run is relative to the alignment point and to the baseline of its shaped text
            var layout = glyphRun.Layout;
            var originX = x + glyphRun.X - glyphRun.Left;
            var originY = y + glyphRun.Y + layout.BaselineOffset;
            AddGlyphRuns(runs, layout, layout.GetGlyphs(glyphRun.Clusters), originX, originY, scaleX: 1);
        }

        DrawGlyphItems(runs, paint, stroke);
    }
}

public sealed partial class TextMetrics
{
    private const string TextClustersHref = "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal";

    /// <summary>The prepared text, or null for the metrics created by .NET code.</summary>
    private CanvasTextLayout? _layout;

    private CanvasTextLayout Layout => _layout ?? throw new InvalidOperationException("The metrics were not returned by measureText()");

    /// <summary>Creates the metrics of a prepared text.</summary>
    internal static TextMetrics Create(CanvasTextLayout layout)
    {
        var alignment = layout.AlignmentOffset;
        var baseline = layout.BaselineOffset;

        // The bounding box of the glyphs (the union of the outlines of the visible glyphs), relative to the alignment point
        var hasInk = layout.TryGetInkBounds(clusters: null, out var left, out var right, out var top, out var bottom);
        return new TextMetrics
        {
            _layout = layout,
            Width = layout.Width,
            ActualBoundingBoxLeft = hasInk ? alignment - left : 0,
            ActualBoundingBoxRight = hasInk ? right - alignment : 0,
            ActualBoundingBoxAscent = hasInk ? top - baseline : -baseline,
            ActualBoundingBoxDescent = hasInk ? baseline - bottom : baseline,
            FontBoundingBoxAscent = layout.Ascent - baseline,
            FontBoundingBoxDescent = layout.Descent + baseline,
            EmHeightAscent = layout.EmAscent - baseline,
            EmHeightDescent = layout.EmDescent + baseline,
            HangingBaseline = layout.Hanging - baseline,
            AlphabeticBaseline = -baseline,
            IdeographicBaseline = layout.Ideographic - baseline,
        };
    }

    /// <summary>
    /// Returns the index (in UTF-16 code units) of the caret position closest to a horizontal offset from the alignment point
    /// (https://github.com/whatwg/html/pull/11000): the position before the character under the offset in the reading
    /// direction when the offset is on its first half, else the position after it.
    /// </summary>
    /// <param name="offset">The offset from the alignment point, in CSS px.</param>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("getIndexFromOffset", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public uint GetIndexFromOffset(double offset) => double.IsNaN(offset) ? 0 : Layout.GetIndexFromX(offset + Layout.AlignmentOffset);

    /// <summary>
    /// Returns the rectangles of the selection of a range of the text (https://github.com/whatwg/html/pull/11000), relative to
    /// the alignment point and to the baseline: one per bidi run of the range, of the height of the font bounding box.
    /// </summary>
    /// <param name="start">The index (in UTF-16 code units) of the start of the range.</param>
    /// <param name="end">The index (in UTF-16 code units) of the end of the range: a range that ends at or before its start is a rectangle without width at the caret position of the end.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when an index is after the end of the text.</exception>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("getSelectionRects", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public IReadOnlyList<DomRectReadOnly> GetSelectionRects(uint start, uint end) => Layout.GetSelectionRects(start, end);

    /// <summary>
    /// Returns the bounding box of the glyphs of a range of the text (https://github.com/whatwg/html/pull/11000), relative to
    /// the alignment point and to the baseline: the box that the <c>actualBoundingBox</c> metrics give for the text where the
    /// other characters are not drawn.
    /// </summary>
    /// <param name="start">The index (in UTF-16 code units) of the start of the range.</param>
    /// <param name="end">The index (in UTF-16 code units) of the end of the range.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the start is not the index of a character, or the end is after the end of the text.</exception>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("getActualBoundingBox", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public DomRectReadOnly GetActualBoundingBox(uint start, uint end) => Layout.GetActualBoundingBox(start, end);

    /// <summary>
    /// Returns the clusters of the text (https://github.com/whatwg/html/pull/11000), in logical order: its grapheme clusters
    /// (merged when a glyph spans several of them), each drawn by <see cref="CanvasRenderingContext2DBase.FillTextCluster"/>
    /// at the position that draws the text as <c>fillText()</c>.
    /// </summary>
    /// <param name="options">The alignment and the baseline of the positions of the clusters (by default, the ones of the context when the text was measured).</param>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("getTextClusters", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public IReadOnlyList<TextCluster> GetTextClusters(TextClusterOptions? options = null) => CreateClusters(Layout.GetClusters(0, Layout.Text.Length), options);

    /// <summary>Returns the clusters of the text that have characters in a range (https://github.com/whatwg/html/pull/11000): see <see cref="GetTextClusters(TextClusterOptions?)"/>.</summary>
    /// <param name="start">The index (in UTF-16 code units) of the start of the range.</param>
    /// <param name="end">The index (in UTF-16 code units) of the end of the range.</param>
    /// <param name="options">The alignment and the baseline of the positions of the clusters (by default, the ones of the context when the text was measured).</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the start is not the index of a character, or the end is after the end of the text.</exception>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("getTextClusters", TextClustersHref)]
    [Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
    public IReadOnlyList<TextCluster> GetTextClusters(uint start, uint end, TextClusterOptions? options = null)
    {
        Layout.ThrowIfInvalidClusterRange(start, end);
        return CreateClusters(Layout.GetClusters((int)start, (int)end), options);
    }

    private List<TextCluster> CreateClusters(List<int> clusters, TextClusterOptions? options)
    {
        var layout = Layout;
        var align = options?.Align ?? layout.Align;
        var baseline = options?.Baseline ?? layout.Baseline;

        // The alignment point of a cluster (for the alignment of the options) relative to the alignment point of the text, and
        // its baseline (for the baseline of the options) relative to the baseline of the text
        var y = layout.BaselineOffset - layout.GetBaselineOffset(baseline);
        var result = new List<TextCluster>(clusters.Count);
        foreach (var index in clusters)
        {
            var cluster = layout.Clusters[index];
            var x = cluster.Left + layout.GetAlignmentOffset(align, cluster.Width) - layout.AlignmentOffset;
            result.Add(new TextCluster(layout, index, x, y, align, baseline));
        }

        return result;
    }
}

/// <summary>
/// A cluster of a measured text (https://github.com/whatwg/html/pull/11000), returned by
/// <see cref="TextMetrics.GetTextClusters(TextClusterOptions?)"/>: a range of the text (a grapheme cluster, or several when a
/// glyph spans them) and the position of its alignment point relative to the alignment point and to the baseline of the text.
/// </summary>
/// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
[IdlInterface("TextCluster", "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal")]
[Experimental(ExperimentalApis.CanvasTextClusters, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class TextCluster
{
    private const string Href = "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal";

    internal TextCluster(CanvasTextLayout layout, int index, double x, double y, CanvasTextAlign align, CanvasTextBaseline baseline)
    {
        Layout = layout;
        Index = index;
        X = x;
        Y = y;
        Align = align;
        Baseline = baseline;
    }

    internal CanvasTextLayout Layout { get; }

    internal int Index { get; }

    /// <summary>Gets the horizontal distance of the alignment point of the cluster (for <see cref="Align"/>) from the alignment point of the text.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("x", Href)]
    public double X { get; }

    /// <summary>Gets the vertical distance of the baseline of the cluster (for <see cref="Baseline"/>) below the baseline of the text.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("y", Href)]
    public double Y { get; }

    /// <summary>Gets the index (in UTF-16 code units) of the first character of the cluster.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("start", Href)]
    public uint Start => (uint)Layout.Clusters[Index].Start;

    /// <summary>Gets the index (in UTF-16 code units) after the last character of the cluster.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("end", Href)]
    public uint End => (uint)Layout.Clusters[Index].End;

    /// <summary>Gets the alignment the cluster is drawn with.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("align", Href)]
    public CanvasTextAlign Align { get; }

    /// <summary>Gets the baseline the cluster is drawn with.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("baseline", Href)]
    public CanvasTextBaseline Baseline { get; }
}

/// <summary>
/// The options of <see cref="TextMetrics.GetTextClusters(TextClusterOptions?)"/> and of
/// <see cref="CanvasRenderingContext2DBase.FillTextCluster"/> (https://github.com/whatwg/html/pull/11000): the alignment and the
/// baseline of the clusters, and for the drawing, the position that replaces the one of the cluster.
/// </summary>
/// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
[IdlInterface("TextClusterOptions", "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal")]
public sealed class TextClusterOptions
{
    /// <summary>Gets the alignment of the clusters.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("align", "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal")]
    public CanvasTextAlign? Align { get; init; }

    /// <summary>Gets the baseline of the clusters.</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("baseline", "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal")]
    public CanvasTextBaseline? Baseline { get; init; }

    /// <summary>Gets the horizontal position of a drawn cluster, which replaces <see cref="TextCluster.X"/> (ignored by <c>getTextClusters()</c>).</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("x", "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal")]
    public double? X { get; init; }

    /// <summary>Gets the vertical position of a drawn cluster, which replaces <see cref="TextCluster.Y"/> (ignored by <c>getTextClusters()</c>).</summary>
    /// <seealso href="https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal"/>
    [IdlMember("y", "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal")]
    public double? Y { get; init; }
}

/// <summary>
/// A text shaped by <see cref="CanvasRenderingContext2DBase.ShapeText"/> with the font, the text styles, the direction, the
/// alignment and the baseline of the context: its glyph runs and clusters, the caret positions and the selection rectangles.
/// The distances are in CSS px, relative to the alignment point and to the baseline of the text.
/// </summary>
/// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
[IdlInterface("ShapedText", "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText")]
[Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CanvasShapedText
{
    private const string Href = "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText";

    private readonly CanvasTextLayout _layout;

    internal CanvasShapedText(CanvasTextLayout layout) => _layout = layout;

    /// <summary>Gets the advance width of the text.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("width", Href)]
    public double Width => _layout.Width;

    /// <summary>Returns the glyph runs of the text, from left to right: the glyphs of a bidi run with the same font.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("glyphRuns", Href)]
    public IReadOnlyList<CanvasGlyphRun> GlyphRuns()
    {
        // The clusters grouped by the bidi run of their fragment (see CanvasTextLayout.GetFragment), then by font
        var groups = new List<(int Run, object? Font, List<int> Clusters)>();
        for (var i = 0; i < _layout.Clusters.Count; i++)
        {
            var run = _layout.GetFragment(_layout.Clusters[i]).Run;
            object? font = _layout.GetGlyphs(i) is [var first, ..] ? first.Font : null;
            var group = groups.FindLastIndex(g => g.Run == run);
            if (group >= 0 && (font is null || groups[group].Font is null || ReferenceEquals(font, groups[group].Font)) && group == groups.Count - 1)
            {
                groups[group].Clusters.Add(i);
                if (groups[group].Font is null && font is not null)
                {
                    groups[group] = groups[group] with { Font = font };
                }

                continue;
            }

            groups.Add((run, font, [i]));
        }

        var result = groups.ConvertAll(group => CreateRun(group.Clusters, useFragments: true));
        result.Sort((a, b) => a.Left.CompareTo(b.Left));
        return result;
    }

    /// <summary>Returns the clusters of the text, in logical order, as glyph runs: its grapheme clusters, merged when a glyph spans several of them.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("glyphClusters", Href)]
    public IReadOnlyList<CanvasGlyphRun> GlyphClusters() => CreateClusters(_layout.GetClusters(0, _layout.Text.Length));

    /// <summary>Returns the clusters of the text that have characters in a range, in logical order, as glyph runs.</summary>
    /// <param name="start">The index (in UTF-16 code units) of the start of the range.</param>
    /// <param name="end">The index (in UTF-16 code units) of the end of the range.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the start is not the index of a character, or the end is after the end of the text.</exception>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("glyphClusters", Href)]
    public IReadOnlyList<CanvasGlyphRun> GlyphClusters(uint start, uint end)
    {
        _layout.ThrowIfInvalidClusterRange(start, end);
        return CreateClusters(_layout.GetClusters((int)start, (int)end));
    }

    /// <summary>Returns the rectangles of the selection of a range of the text: see <see cref="TextMetrics.GetSelectionRects"/>.</summary>
    /// <param name="start">The index (in UTF-16 code units) of the start of the range.</param>
    /// <param name="end">The index (in UTF-16 code units) of the end of the range.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when an index is after the end of the text.</exception>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("selectionRects", Href)]
    public IReadOnlyList<DomRectReadOnly> SelectionRects(uint start, uint end) => _layout.GetSelectionRects(start, end);

    /// <summary>Returns the bounding box of the glyphs of a range of the text: see <see cref="TextMetrics.GetActualBoundingBox"/>.</summary>
    /// <param name="start">The index (in UTF-16 code units) of the start of the range.</param>
    /// <param name="end">The index (in UTF-16 code units) of the end of the range.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the start is not the index of a character, or the end is after the end of the text.</exception>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("actualBoundingBox", Href)]
    public DomRectReadOnly ActualBoundingBox(uint start, uint end) => _layout.GetActualBoundingBox(start, end);

    /// <summary>Returns the index (in UTF-16 code units) of the caret position closest to a horizontal offset from the alignment point: see <see cref="TextMetrics.GetIndexFromOffset"/>.</summary>
    /// <param name="advance">The offset from the alignment point, in CSS px.</param>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("advanceToPosition", Href)]
    public uint AdvanceToPosition(double advance) => double.IsNaN(advance) ? 0 : _layout.GetIndexFromX(advance + _layout.AlignmentOffset);

    /// <summary>Returns the horizontal offset from the alignment point of the caret position before a character (or at the end of the text).</summary>
    /// <param name="position">The index (in UTF-16 code units) of the caret position.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the index is after the end of the text.</exception>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText"/>
    [IdlMember("positionToAdvance", Href)]
    public double PositionToAdvance(uint position)
    {
        if (position > _layout.Text.Length)
            throw new DomException("The position is outside of the text", "IndexSizeError");

        return _layout.GetCaretX((int)position) - _layout.AlignmentOffset;
    }

    private List<CanvasGlyphRun> CreateClusters(List<int> clusters)
    {
        var result = new List<CanvasGlyphRun>(clusters.Count);
        foreach (var index in clusters)
        {
            result.Add(CreateRun([index], useFragments: false));
        }

        return result;
    }

    /// <summary>
    /// Creates the glyph run of a set of clusters. A run of the fragments of the text (<paramref name="useFragments"/>) places a
    /// cluster without advance at the start edge of the character after it, whose fragment it is in (e.g. a bidi control that
    /// closes a run of the other direction).
    /// </summary>
    private CanvasGlyphRun CreateRun(List<int> clusters, bool useFragments)
    {
        var (start, end, left, right) = (int.MaxValue, 0, double.PositiveInfinity, double.NegativeInfinity);
        foreach (var index in clusters)
        {
            var cluster = _layout.Clusters[index];
            var (_, clusterLeft, clusterRight) = useFragments ? _layout.GetFragment(cluster) : (cluster.Run, cluster.Low, cluster.High);
            start = Math.Min(start, cluster.Start);
            end = Math.Max(end, cluster.End);
            left = Math.Min(left, clusterLeft);
            right = Math.Max(right, clusterRight);
        }

        return new CanvasGlyphRun(_layout, clusters, (uint)start, (uint)end, left, right - left);
    }
}

/// <summary>
/// A glyph run or a cluster of a <see cref="CanvasShapedText"/>: a range of the text and its glyphs, positioned relative to the
/// alignment point and to the baseline of the shaped text.
/// </summary>
/// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
[IdlInterface("GlyphRun", "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun")]
[Experimental(ExperimentalApis.CanvasShapedText, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CanvasGlyphRun
{
    private const string Href = "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun";

    internal CanvasGlyphRun(CanvasTextLayout layout, List<int> clusters, uint start, uint end, double left, double width)
    {
        Layout = layout;
        Clusters = clusters;
        Start = start;
        End = end;
        Left = left;
        Width = width;
    }

    internal CanvasTextLayout Layout { get; }

    /// <summary>The indexes of the clusters of the run in <see cref="CanvasTextLayout.Clusters"/>.</summary>
    internal List<int> Clusters { get; }

    /// <summary>The distance of the left of the run from the left of the shaped text.</summary>
    internal double Left { get; }

    /// <summary>Gets the index (in UTF-16 code units) of the first character of the run.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
    [IdlMember("start", Href)]
    public uint Start { get; }

    /// <summary>Gets the index (in UTF-16 code units) after the last character of the run.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
    [IdlMember("end", Href)]
    public uint End { get; }

    /// <summary>Gets the horizontal distance of the left of the run from the alignment point of the shaped text.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
    [IdlMember("x", Href)]
    public double X => Left - Layout.AlignmentOffset;

    /// <summary>Gets the vertical distance of the baseline of the run below the baseline of the shaped text (always 0).</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
    [IdlMember("y", Href)]
    public double Y => 0;

    /// <summary>Gets the advance width of the run.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
    [IdlMember("width", Href)]
    public double Width { get; }

    /// <summary>Returns the metrics of the run, as <c>measureText()</c> gives them for its glyphs alone, with the alignment and the baseline of the shaped text.</summary>
    /// <seealso href="https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun"/>
    [IdlMember("textMetrics", Href)]
    public TextMetrics GetTextMetrics() => TextMetrics.Create(Layout.Slice(Clusters, Left, Width));
}
