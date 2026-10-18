using Meziantou.Framework.WebPlatform.WebVtt;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/webvtt/#enumdef-autokeyword</summary>
[IdlInterface("AutoKeyword", "https://w3c.github.io/webvtt/#enumdef-autokeyword")]
public enum AutoKeyword
{
    /// <summary>The line or the position of the cue is automatic.</summary>
    [IdlMember("auto", "https://w3c.github.io/webvtt/#dom-autokeyword-auto")]
    Auto,
}

/// <summary>The writing direction of a WebVTT cue (https://w3c.github.io/webvtt/#enumdef-directionsetting).</summary>
[IdlInterface("DirectionSetting", "https://w3c.github.io/webvtt/#enumdef-directionsetting")]
public enum DirectionSetting
{
    /// <summary>A line extends horizontally and is offset vertically from the video viewport's top edge.</summary>
    [IdlMember("", "https://w3c.github.io/webvtt/#webvtt-cue-horizontal-writing-direction")]
    Horizontal,

    /// <summary>A line extends vertically, and the consecutive lines are displayed to the left of each other.</summary>
    [IdlMember("rl", "https://w3c.github.io/webvtt/#dom-directionsetting-rl")]
    Rl,

    /// <summary>A line extends vertically, and the consecutive lines are displayed to the right of each other.</summary>
    [IdlMember("lr", "https://w3c.github.io/webvtt/#dom-directionsetting-lr")]
    Lr,
}

/// <summary>The alignment of the line of a WebVTT cue (https://w3c.github.io/webvtt/#enumdef-linealignsetting).</summary>
[IdlInterface("LineAlignSetting", "https://w3c.github.io/webvtt/#enumdef-linealignsetting")]
public enum LineAlignSetting
{
    /// <summary>The start side of the cue box is aligned at the line.</summary>
    [IdlMember("start", "https://w3c.github.io/webvtt/#dom-linealignsetting-start")]
    Start,

    /// <summary>The cue box is centered at the line.</summary>
    [IdlMember("center", "https://w3c.github.io/webvtt/#dom-linealignsetting-center")]
    Center,

    /// <summary>The end side of the cue box is aligned at the line.</summary>
    [IdlMember("end", "https://w3c.github.io/webvtt/#dom-linealignsetting-end")]
    End,
}

/// <summary>The alignment of the position of a WebVTT cue (https://w3c.github.io/webvtt/#enumdef-positionalignsetting).</summary>
[IdlInterface("PositionAlignSetting", "https://w3c.github.io/webvtt/#enumdef-positionalignsetting")]
public enum PositionAlignSetting
{
    /// <summary>The left side (or the top side of a vertical cue) of the cue box is aligned at the position.</summary>
    [IdlMember("line-left", "https://w3c.github.io/webvtt/#dom-positionalignsetting-line-left")]
    LineLeft,

    /// <summary>The cue box is centered at the position.</summary>
    [IdlMember("center", "https://w3c.github.io/webvtt/#dom-positionalignsetting-center")]
    Center,

    /// <summary>The right side (or the bottom side of a vertical cue) of the cue box is aligned at the position.</summary>
    [IdlMember("line-right", "https://w3c.github.io/webvtt/#dom-positionalignsetting-line-right")]
    LineRight,

    /// <summary>The alignment depends on the text alignment of the cue.</summary>
    [IdlMember("auto", "https://w3c.github.io/webvtt/#dom-positionalignsetting-auto")]
    Auto,
}

/// <summary>The alignment of the text of a WebVTT cue (https://w3c.github.io/webvtt/#enumdef-alignsetting).</summary>
[IdlInterface("AlignSetting", "https://w3c.github.io/webvtt/#enumdef-alignsetting")]
public enum AlignSetting
{
    /// <summary>The text of each line is aligned towards the start side of the box.</summary>
    [IdlMember("start", "https://w3c.github.io/webvtt/#dom-alignsetting-start")]
    Start,

    /// <summary>The text of each line is centered in the box.</summary>
    [IdlMember("center", "https://w3c.github.io/webvtt/#dom-alignsetting-center")]
    Center,

    /// <summary>The text of each line is aligned towards the end side of the box.</summary>
    [IdlMember("end", "https://w3c.github.io/webvtt/#dom-alignsetting-end")]
    End,

    /// <summary>The text of each line is aligned to the left side of the box (or to its top side for a vertical cue).</summary>
    [IdlMember("left", "https://w3c.github.io/webvtt/#dom-alignsetting-left")]
    Left,

    /// <summary>The text of each line is aligned to the right side of the box (or to its bottom side for a vertical cue).</summary>
    [IdlMember("right", "https://w3c.github.io/webvtt/#dom-alignsetting-right")]
    Right,
}

/// <summary>
/// The <c>(double or AutoKeyword)</c> union of the line and of the position of a WebVTT cue
/// (https://w3c.github.io/webvtt/#typedefdef-lineandpositionsetting): a number, or <c>auto</c>.
/// </summary>
public readonly struct LineAndPositionSetting : IUnionValue, IEquatable<LineAndPositionSetting>
{
    private readonly double _value;
    private readonly bool _isNumber;

    private LineAndPositionSetting(double value)
    {
        _value = value;
        _isNumber = true;
    }

    /// <summary>Gets the <c>auto</c> value.</summary>
    [NotInIdl]
    public static LineAndPositionSetting Auto => default;

    /// <summary>Gets a value indicating whether the value is <c>auto</c>.</summary>
    [NotInIdl]
    public bool IsAuto => !_isNumber;

    /// <summary>Gets the number, or NaN when the value is <c>auto</c>.</summary>
    [NotInIdl]
    public double Number => _isNumber ? _value : double.NaN;

    /// <summary>Gets the value: a <see cref="double"/>, or the string <c>auto</c>.</summary>
    [NotInIdl]
    public object Value => _isNumber ? _value : "auto";

    /// <summary>Converts a number to a <c>LineAndPositionSetting</c>.</summary>
    [NotInIdl]
    public static implicit operator LineAndPositionSetting(double value) => new(value);

    /// <summary>Converts the <c>auto</c> keyword to a <c>LineAndPositionSetting</c>.</summary>
    [NotInIdl]
    public static implicit operator LineAndPositionSetting(AutoKeyword value) => value == AutoKeyword.Auto ? default : throw new ArgumentOutOfRangeException(nameof(value));

    /// <summary>Creates a <c>LineAndPositionSetting</c> from a number.</summary>
    [NotInIdl]
    public static LineAndPositionSetting FromDouble(double value) => value;

    /// <summary>Creates a <c>LineAndPositionSetting</c> from the <c>auto</c> keyword.</summary>
    [NotInIdl]
    public static LineAndPositionSetting FromAutoKeyword(AutoKeyword value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(LineAndPositionSetting other) => _isNumber == other._isNumber && _value.Equals(other._value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LineAndPositionSetting other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => HashCode.Combine(_isNumber, _value);

    /// <inheritdoc/>
    [NotInIdl]
    public override string ToString() => _isNumber ? _value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "auto";

    /// <summary>Determines whether two <c>LineAndPositionSetting</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(LineAndPositionSetting left, LineAndPositionSetting right) => left.Equals(right);

    /// <summary>Determines whether two <c>LineAndPositionSetting</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(LineAndPositionSetting left, LineAndPositionSetting right) => !left.Equals(right);
}

/// <summary>
/// A WebVTT cue (https://w3c.github.io/webvtt/#the-vttcue-interface): the cue of a WebVTT file, or a cue created to be added
/// to a text track with <see cref="TextTrack.AddCue"/>.
/// </summary>
[IdlInterface("VTTCue", "https://w3c.github.io/webvtt/#the-vttcue-interface")]
public sealed class VttCue : TextTrackCue
{
    private VttRegion? _region;
    private DirectionSetting _vertical;
    private bool _snapToLines = true;
    private LineAndPositionSetting _line;
    private LineAlignSetting _lineAlign;
    private LineAndPositionSetting _position;
    private PositionAlignSetting _positionAlign = PositionAlignSetting.Auto;
    private double _size = 100;
    private AlignSetting _align = AlignSetting.Center;
    private string _text;

    /// <summary>Creates a cue (https://w3c.github.io/webvtt/#dom-vttcue-vttcue).</summary>
    /// <param name="startTime">The start time of the cue, in seconds.</param>
    /// <param name="endTime">The end time of the cue, in seconds (positive infinity for an unbounded cue).</param>
    /// <param name="text">The cue text.</param>
    /// <exception cref="ArgumentException"><paramref name="endTime"/> is negative infinity or NaN.</exception>
    [IdlMember("constructor", "https://w3c.github.io/webvtt/#dom-vttcue-vttcue")]
    public VttCue(double startTime, double endTime, string text)
        : base(ValidateStartTime(startTime), ValidateEndTime(endTime))
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    private static double ValidateStartTime(double startTime)
        => double.IsFinite(startTime) ? startTime : throw new ArgumentException("The start time of a cue must be a finite number", nameof(startTime));

    private static double ValidateEndTime(double endTime)
        => double.IsNaN(endTime) || double.IsNegativeInfinity(endTime) ? throw new ArgumentException("The end time of a cue cannot be negative infinity or NaN", nameof(endTime)) : endTime;

    /// <summary>
    /// The text track cue display state of the cue (https://html.spec.whatwg.org/multipage/media.html#text-track-cue-display-state):
    /// the boxes of the cue and their position, kept while the cue is displayed.
    /// </summary>
    internal WebVttCueDisplayState? DisplayState { get; set; }

    private protected override void OnDisplayStateEmptied() => DisplayState = null;

    /// <summary>Gets or sets the region of the cue (https://w3c.github.io/webvtt/#dom-vttcue-region), or null.</summary>
    [IdlMember("region", "https://w3c.github.io/webvtt/#dom-vttcue-region")]
    public VttRegion? Region
    {
        get => _region;
        set
        {
            _region = value;
            if (value is not null && Track is { } track && !track.Regions.Contains(value))
            {
                track.Regions.Add(value);
            }

            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the writing direction of the cue (https://w3c.github.io/webvtt/#dom-vttcue-vertical).</summary>
    [IdlMember("vertical", "https://w3c.github.io/webvtt/#dom-vttcue-vertical")]
    public DirectionSetting Vertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            OnCueChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the line of the cue is a number of lines, rather than a percentage of the
    /// dimensions of the video (https://w3c.github.io/webvtt/#dom-vttcue-snaptolines).
    /// </summary>
    [IdlMember("snapToLines", "https://w3c.github.io/webvtt/#dom-vttcue-snaptolines")]
    public bool SnapToLines
    {
        get => _snapToLines;
        set
        {
            _snapToLines = value;
            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the line of the cue (https://w3c.github.io/webvtt/#dom-vttcue-line): a number, or <c>auto</c>.</summary>
    [IdlMember("line", "https://w3c.github.io/webvtt/#dom-vttcue-line")]
    public LineAndPositionSetting Line
    {
        get => _line;
        set
        {
            if (!value.IsAuto && !double.IsFinite(value.Number))
                throw new ArgumentException("The line of a cue must be a finite number or auto", nameof(value));

            _line = value;
            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the alignment of the line of the cue (https://w3c.github.io/webvtt/#dom-vttcue-linealign).</summary>
    [IdlMember("lineAlign", "https://w3c.github.io/webvtt/#dom-vttcue-linealign")]
    public LineAlignSetting LineAlign
    {
        get => _lineAlign;
        set
        {
            _lineAlign = value;
            OnCueChanged();
        }
    }

    /// <summary>
    /// Gets or sets the position of the cue (https://w3c.github.io/webvtt/#dom-vttcue-position): a percentage, or <c>auto</c>.
    /// </summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("position", "https://w3c.github.io/webvtt/#dom-vttcue-position")]
    public LineAndPositionSetting Position
    {
        get => _position;
        set
        {
            if (!value.IsAuto)
            {
                if (!double.IsFinite(value.Number))
                    throw new ArgumentException("The position of a cue must be a finite number or auto", nameof(value));

                if (value.Number is < 0 or > 100)
                    throw DomException.IndexSize("The position of a cue must be in the range [0, 100]");
            }

            _position = value;
            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the alignment of the position of the cue (https://w3c.github.io/webvtt/#dom-vttcue-positionalign).</summary>
    [IdlMember("positionAlign", "https://w3c.github.io/webvtt/#dom-vttcue-positionalign")]
    public PositionAlignSetting PositionAlign
    {
        get => _positionAlign;
        set
        {
            _positionAlign = value;
            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the size of the cue box, a percentage of the video (https://w3c.github.io/webvtt/#dom-vttcue-size).</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the value is negative or greater than 100.</exception>
    [IdlMember("size", "https://w3c.github.io/webvtt/#dom-vttcue-size")]
    public double Size
    {
        get => _size;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The size of a cue must be a finite number", nameof(value));

            if (value is < 0 or > 100)
                throw DomException.IndexSize("The size of a cue must be in the range [0, 100]");

            _size = value;
            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the alignment of the text of the cue (https://w3c.github.io/webvtt/#dom-vttcue-align).</summary>
    [IdlMember("align", "https://w3c.github.io/webvtt/#dom-vttcue-align")]
    public AlignSetting Align
    {
        get => _align;
        set
        {
            _align = value;
            OnCueChanged();
        }
    }

    /// <summary>Gets or sets the cue text, in raw unparsed form (https://w3c.github.io/webvtt/#dom-vttcue-text).</summary>
    [IdlMember("text", "https://w3c.github.io/webvtt/#dom-vttcue-text")]
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _text = value;
            OnCueChanged();
        }
    }

    /// <summary>
    /// Returns the cue text as a document fragment of HTML elements and other nodes (https://w3c.github.io/webvtt/#dom-vttcue-getcueashtml):
    /// the WebVTT cue text parsing rules, then the WebVTT cue text DOM construction rules. The nodes belong to the document
    /// of the current global object.
    /// </summary>
    [IdlMember("getCueAsHTML", "https://w3c.github.io/webvtt/#dom-vttcue-getcueashtml")]
    public DocumentFragment GetCueAsHtml() => GetCueAsHtml(Document.CurrentGlobalDocument);

    /// <summary>Returns the cue text as a document fragment of HTML elements and other nodes that belong to a document.</summary>
    /// <param name="document">The node document of the nodes.</param>
    [NotInIdl]
    public DocumentFragment GetCueAsHtml(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return WebVttCueTextParser.Parse(_text, language: null).ToDocumentFragment(document);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Computed values (https://w3c.github.io/webvtt/#webvtt-cue-computed-line)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/webvtt/#cue-computed-line</summary>
    internal double ComputedLine
    {
        get
        {
            if (!_line.IsAuto)
                return !_snapToLines && _line.Number is < 0 or > 100 ? 100 : _line.Number;

            if (!_snapToLines)
                return 100;

            if (Track is not { MediaElement: { } media } track)
                return -1;

            // The number of showing tracks that precede the track of the cue in the list of text tracks, plus one, negated
            var n = 0;
            foreach (var other in media.TextTracks)
            {
                if (ReferenceEquals(other, track))
                    break;

                if (other.Mode == TextTrackMode.Showing)
                {
                    n++;
                }
            }

            return -(n + 1);
        }
    }

    /// <summary>https://w3c.github.io/webvtt/#cue-computed-position</summary>
    internal double ComputedPosition => !_position.IsAuto
        ? _position.Number
        : _align switch
        {
            AlignSetting.Left => 0,
            AlignSetting.Right => 100,
            _ => 50,
        };

    /// <summary>
    /// https://w3c.github.io/webvtt/#cue-computed-position-alignment. <paramref name="isLeftToRight"/> is whether the base
    /// direction of the cue text is left-to-right.
    /// </summary>
    internal PositionAlignSetting GetComputedPositionAlign(bool isLeftToRight)
    {
        if (_positionAlign != PositionAlignSetting.Auto)
            return _positionAlign;

        return _align switch
        {
            AlignSetting.Left => PositionAlignSetting.LineLeft,
            AlignSetting.Right => PositionAlignSetting.LineRight,
            AlignSetting.Start => isLeftToRight ? PositionAlignSetting.LineLeft : PositionAlignSetting.LineRight,
            AlignSetting.End => isLeftToRight ? PositionAlignSetting.LineRight : PositionAlignSetting.LineLeft,
            _ => PositionAlignSetting.Center,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The values of the WebVTT parser (https://w3c.github.io/webvtt/#cue-timings-and-settings-parsing)
    // ----------------------------------------------------------------------------------------------------------------

    internal void SetParsedId(string id) => Id = id;

    internal void SetParsedText(string text) => _text = text;

    internal void SetParsedTimes(double startTime, double endTime)
    {
        StartTime = startTime;
        EndTime = endTime;
    }

    internal void SetParsedRegion(VttRegion? region) => _region = region;

    internal void SetParsedVertical(DirectionSetting vertical)
    {
        // There are no vertical regions
        _vertical = vertical;
        _region = null;
    }

    internal void SetParsedLine(double line, bool snapToLines, LineAlignSetting? alignment)
    {
        if (alignment is { } lineAlign)
        {
            _lineAlign = lineAlign;
        }

        // The cue is explicitly positioned with a line offset: it drops out of the region
        _line = line;
        _snapToLines = snapToLines;
        _region = null;
    }

    internal void SetParsedPosition(double position, PositionAlignSetting? alignment)
    {
        if (alignment is { } positionAlign)
        {
            _positionAlign = positionAlign;
        }

        _position = position;
    }

    internal void SetParsedSize(double size)
    {
        // A cue that is explicitly sized drops out of the region
        _size = size;
        if (size != 100)
        {
            _region = null;
        }
    }

    internal void SetParsedAlign(AlignSetting align) => _align = align;
}
