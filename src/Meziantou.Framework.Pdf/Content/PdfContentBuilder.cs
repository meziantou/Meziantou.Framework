using System.Globalization;
using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Writes the operators of a content stream (ISO 32000-2, 8 and 9): graphics state, paths, colors, text, images and
/// marked content. The resources the content uses (fonts, images) are added to the resource dictionary of the page.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are in user space: by default the origin is the lower-left corner of the page, x increases to the right
/// and y upward, in 1/72 inch. The methods return the builder, so that calls can be chained.
/// </para>
/// <para>
/// The builder checks the structure of the content: text operators are only allowed between <see cref="BeginText"/>
/// and <see cref="EndText"/>, <see cref="RestoreGraphicsState"/> and <see cref="EndMarkedContent"/> need a matching
/// call before them, and a text is only shown once a font is set.
/// </para>
/// </remarks>
public sealed class PdfContentBuilder
{
    // The control point distance of a quarter circle drawn with one cubic Bezier curve
    private const double Kappa = 0.5522847498307936;

    private readonly PdfDocument _document;
    private readonly PdfDictionary _resources;
    private readonly PdfStream _stream;
    private readonly PdfByteWriter _buffer = new(256);
    private int _graphicsStateDepth;
    private int _markedContentDepth;
    private readonly Stack<(PdfFont? Font, double FontSize, double TextRise)> _savedTextStates = new();
    private bool _inText;
    private PdfFont? _font;
    private double _fontSize;
    private double _textRise;

    internal PdfContentBuilder(PdfDocument document, PdfDictionary resources, PdfStream stream)
    {
        _document = document;
        _resources = resources;
        _stream = stream;
    }

    /// <summary>Gets the number of <see cref="SaveGraphicsState"/> calls that are not matched by a <see cref="RestoreGraphicsState"/> call.</summary>
    public int GraphicsStateDepth => _graphicsStateDepth;

    /// <summary>Saves the graphics state (<c>q</c>).</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SaveGraphicsState()
    {
        EnsureNotInText();
        _graphicsStateDepth++;
        _savedTextStates.Push((_font, _fontSize, _textRise));
        return Operator("q");
    }

    /// <summary>Restores the graphics state saved by the matching <see cref="SaveGraphicsState"/> call (<c>Q</c>).</summary>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">No graphics state is saved.</exception>
    public PdfContentBuilder RestoreGraphicsState()
    {
        EnsureNotInText();
        if (_graphicsStateDepth == 0)
            throw new InvalidOperationException("No graphics state is saved");

        _graphicsStateDepth--;
        (_font, _fontSize, _textRise) = _savedTextStates.Pop();
        return Operator("Q");
    }

    /// <summary>Concatenates a matrix with the current transformation matrix (<c>cm</c>).</summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Transform(PdfMatrix matrix)
    {
        EnsureNotInText();
        return Operator("cm", matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
    }

    /// <summary>Moves the origin of the user space.</summary>
    /// <param name="x">The horizontal translation.</param>
    /// <param name="y">The vertical translation.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Translate(double x, double y) => Transform(PdfMatrix.CreateTranslation(x, y));

    /// <summary>Scales the user space.</summary>
    /// <param name="x">The horizontal scale.</param>
    /// <param name="y">The vertical scale.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Scale(double x, double y) => Transform(PdfMatrix.CreateScale(x, y));

    /// <summary>Rotates the user space counterclockwise.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Rotate(double degrees) => Transform(PdfMatrix.CreateRotation(degrees));

    /// <summary>Sets the line width (<c>w</c>).</summary>
    /// <param name="width">The width, 0 or more.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetLineWidth(double width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        return Operator("w", width);
    }

    /// <summary>Sets the line cap style (<c>J</c>).</summary>
    /// <param name="cap">The style.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetLineCap(PdfLineCap cap) => Operator("J", (int)cap);

    /// <summary>Sets the line join style (<c>j</c>).</summary>
    /// <param name="join">The style.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetLineJoin(PdfLineJoin join) => Operator("j", (int)join);

    /// <summary>Sets the miter limit (<c>M</c>).</summary>
    /// <param name="limit">The limit, 1 or more.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetMiterLimit(double limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return Operator("M", limit);
    }

    /// <summary>Sets the dash pattern (<c>d</c>). An empty array draws solid lines.</summary>
    /// <param name="dashArray">The lengths of the dashes and the gaps, alternately.</param>
    /// <param name="phase">The distance into the pattern at which the dashes start.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetDashPattern(ReadOnlySpan<double> dashArray, double phase = 0)
    {
        _buffer.WriteByte((byte)'[');
        for (var i = 0; i < dashArray.Length; i++)
        {
            if (dashArray[i] < 0)
                throw new ArgumentOutOfRangeException(nameof(dashArray), "The lengths of a dash pattern must not be negative");

            if (i > 0)
            {
                _buffer.WriteSpace();
            }

            _buffer.WriteNumber(dashArray[i]);
        }

        _buffer.WriteAscii("] ");
        return Operator("d", phase);
    }

    /// <summary>Sets the flatness tolerance (<c>i</c>).</summary>
    /// <param name="flatness">The tolerance, from 0 to 100.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFlatness(double flatness)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(flatness);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(flatness, 100);
        return Operator("i", flatness);
    }

    /// <summary>Sets the color rendering intent (<c>ri</c>), such as <c>RelativeColorimetric</c> or <c>Perceptual</c>.</summary>
    /// <param name="intent">The name of the intent.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetRenderingIntent(string intent)
    {
        ArgumentException.ThrowIfNullOrEmpty(intent);
        WriteName(intent);
        return Operator("ri");
    }

    /// <summary>Begins a new subpath at a point (<c>m</c>).</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder MoveTo(double x, double y) => PathOperator("m", x, y);

    /// <summary>Appends a straight line to a point (<c>l</c>).</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder LineTo(double x, double y) => PathOperator("l", x, y);

    /// <summary>Appends a cubic Bezier curve (<c>c</c>).</summary>
    /// <param name="x1">The x coordinate of the first control point.</param>
    /// <param name="y1">The y coordinate of the first control point.</param>
    /// <param name="x2">The x coordinate of the second control point.</param>
    /// <param name="y2">The y coordinate of the second control point.</param>
    /// <param name="x3">The x coordinate of the end point.</param>
    /// <param name="y3">The y coordinate of the end point.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CurveTo(double x1, double y1, double x2, double y2, double x3, double y3) => PathOperator("c", x1, y1, x2, y2, x3, y3);

    /// <summary>Appends a cubic Bezier curve whose first control point is the current point (<c>v</c>).</summary>
    /// <param name="x2">The x coordinate of the second control point.</param>
    /// <param name="y2">The y coordinate of the second control point.</param>
    /// <param name="x3">The x coordinate of the end point.</param>
    /// <param name="y3">The y coordinate of the end point.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CurveToFromCurrentPoint(double x2, double y2, double x3, double y3) => PathOperator("v", x2, y2, x3, y3);

    /// <summary>Appends a cubic Bezier curve whose second control point is the end point (<c>y</c>).</summary>
    /// <param name="x1">The x coordinate of the first control point.</param>
    /// <param name="y1">The y coordinate of the first control point.</param>
    /// <param name="x3">The x coordinate of the end point.</param>
    /// <param name="y3">The y coordinate of the end point.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CurveToEndPoint(double x1, double y1, double x3, double y3) => PathOperator("y", x1, y1, x3, y3);

    /// <summary>Closes the current subpath with a straight line to its start (<c>h</c>).</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder ClosePath() => PathOperator("h");

    /// <summary>Appends a rectangle as a closed subpath (<c>re</c>).</summary>
    /// <param name="x">The x coordinate of a corner.</param>
    /// <param name="y">The y coordinate of a corner.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Rectangle(double x, double y, double width, double height) => PathOperator("re", x, y, width, height);

    /// <summary>Appends a rectangle as a closed subpath (<c>re</c>).</summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Rectangle(PdfRectangle rectangle) => Rectangle(rectangle.Left, rectangle.Bottom, rectangle.Width, rectangle.Height);

    /// <summary>Appends an ellipse as a closed subpath of four Bezier curves.</summary>
    /// <param name="centerX">The x coordinate of the center.</param>
    /// <param name="centerY">The y coordinate of the center.</param>
    /// <param name="radiusX">The horizontal radius.</param>
    /// <param name="radiusY">The vertical radius.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Ellipse(double centerX, double centerY, double radiusX, double radiusY)
    {
        var kx = radiusX * Kappa;
        var ky = radiusY * Kappa;
        MoveTo(centerX + radiusX, centerY);
        CurveTo(centerX + radiusX, centerY + ky, centerX + kx, centerY + radiusY, centerX, centerY + radiusY);
        CurveTo(centerX - kx, centerY + radiusY, centerX - radiusX, centerY + ky, centerX - radiusX, centerY);
        CurveTo(centerX - radiusX, centerY - ky, centerX - kx, centerY - radiusY, centerX, centerY - radiusY);
        CurveTo(centerX + kx, centerY - radiusY, centerX + radiusX, centerY - ky, centerX + radiusX, centerY);
        return ClosePath();
    }

    /// <summary>Appends a circle as a closed subpath of four Bezier curves.</summary>
    /// <param name="centerX">The x coordinate of the center.</param>
    /// <param name="centerY">The y coordinate of the center.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Circle(double centerX, double centerY, double radius) => Ellipse(centerX, centerY, radius, radius);

    /// <summary>Strokes the path (<c>S</c>).</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Stroke() => PathOperator("S");

    /// <summary>Closes and strokes the path (<c>s</c>).</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CloseAndStroke() => PathOperator("s");

    /// <summary>Fills the path (<c>f</c> or <c>f*</c>).</summary>
    /// <param name="rule">The fill rule.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Fill(PdfFillRule rule = PdfFillRule.NonZero) => PathOperator(rule == PdfFillRule.EvenOdd ? "f*" : "f");

    /// <summary>Fills, then strokes the path (<c>B</c> or <c>B*</c>).</summary>
    /// <param name="rule">The fill rule.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder FillAndStroke(PdfFillRule rule = PdfFillRule.NonZero) => PathOperator(rule == PdfFillRule.EvenOdd ? "B*" : "B");

    /// <summary>Closes, fills, then strokes the path (<c>b</c> or <c>b*</c>).</summary>
    /// <param name="rule">The fill rule.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CloseFillAndStroke(PdfFillRule rule = PdfFillRule.NonZero) => PathOperator(rule == PdfFillRule.EvenOdd ? "b*" : "b");

    /// <summary>Ends the path without painting it (<c>n</c>), after <see cref="Clip"/>.</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder EndPath() => PathOperator("n");

    /// <summary>Intersects the clipping path with the current path (<c>W</c> or <c>W*</c>); the path must then be painted or ended with <see cref="EndPath"/>.</summary>
    /// <param name="rule">The fill rule that decides which points are inside the path.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder Clip(PdfFillRule rule = PdfFillRule.NonZero) => PathOperator(rule == PdfFillRule.EvenOdd ? "W*" : "W");

    /// <summary>Sets the stroking color (<c>G</c>, <c>RG</c> or <c>K</c>).</summary>
    /// <param name="color">The color.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetStrokeColor(PdfColor color) => ColorOperator(color, stroke: true);

    /// <summary>Sets the nonstroking color, used to fill paths and text (<c>g</c>, <c>rg</c> or <c>k</c>).</summary>
    /// <param name="color">The color.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFillColor(PdfColor color) => ColorOperator(color, stroke: false);

    /// <summary>Sets the stroking color in DeviceGray (<c>G</c>).</summary>
    /// <param name="gray">The gray level, from 0 (black) to 1 (white).</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetStrokeGray(double gray) => SetStrokeColor(PdfColor.FromGray(gray));

    /// <summary>Sets the nonstroking color in DeviceGray (<c>g</c>).</summary>
    /// <param name="gray">The gray level, from 0 (black) to 1 (white).</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFillGray(double gray) => SetFillColor(PdfColor.FromGray(gray));

    /// <summary>Sets the stroking color in DeviceRGB (<c>RG</c>).</summary>
    /// <param name="red">The red component, from 0 to 1.</param>
    /// <param name="green">The green component, from 0 to 1.</param>
    /// <param name="blue">The blue component, from 0 to 1.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetStrokeRgb(double red, double green, double blue) => SetStrokeColor(PdfColor.FromRgb(red, green, blue));

    /// <summary>Sets the nonstroking color in DeviceRGB (<c>rg</c>).</summary>
    /// <param name="red">The red component, from 0 to 1.</param>
    /// <param name="green">The green component, from 0 to 1.</param>
    /// <param name="blue">The blue component, from 0 to 1.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFillRgb(double red, double green, double blue) => SetFillColor(PdfColor.FromRgb(red, green, blue));

    /// <summary>Sets the stroking color in DeviceCMYK (<c>K</c>).</summary>
    /// <param name="cyan">The cyan component, from 0 to 1.</param>
    /// <param name="magenta">The magenta component, from 0 to 1.</param>
    /// <param name="yellow">The yellow component, from 0 to 1.</param>
    /// <param name="black">The black component, from 0 to 1.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetStrokeCmyk(double cyan, double magenta, double yellow, double black) => SetStrokeColor(PdfColor.FromCmyk(cyan, magenta, yellow, black));

    /// <summary>Sets the nonstroking color in DeviceCMYK (<c>k</c>).</summary>
    /// <param name="cyan">The cyan component, from 0 to 1.</param>
    /// <param name="magenta">The magenta component, from 0 to 1.</param>
    /// <param name="yellow">The yellow component, from 0 to 1.</param>
    /// <param name="black">The black component, from 0 to 1.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFillCmyk(double cyan, double magenta, double yellow, double black) => SetFillColor(PdfColor.FromCmyk(cyan, magenta, yellow, black));

    /// <summary>Sets the stroking color space (<c>CS</c>): a device color space name, or the name of a color space of the resources.</summary>
    /// <param name="colorSpace">The name of the color space, such as <c>DeviceRGB</c> or <c>Pattern</c>.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetStrokeColorSpace(string colorSpace)
    {
        ArgumentException.ThrowIfNullOrEmpty(colorSpace);
        WriteName(colorSpace);
        return Operator("CS");
    }

    /// <summary>Sets the nonstroking color space (<c>cs</c>): a device color space name, or the name of a color space of the resources.</summary>
    /// <param name="colorSpace">The name of the color space, such as <c>DeviceRGB</c> or <c>Pattern</c>.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFillColorSpace(string colorSpace)
    {
        ArgumentException.ThrowIfNullOrEmpty(colorSpace);
        WriteName(colorSpace);
        return Operator("cs");
    }

    /// <summary>Sets the components of the stroking color in the current stroking color space (<c>SCN</c>).</summary>
    /// <param name="components">The components.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetStrokeColorComponents(params ReadOnlySpan<double> components) => Operator("SCN", components);

    /// <summary>Sets the components of the nonstroking color in the current nonstroking color space (<c>scn</c>).</summary>
    /// <param name="components">The components.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFillColorComponents(params ReadOnlySpan<double> components) => Operator("scn", components);

    /// <summary>Begins a text object (<c>BT</c>): the text matrix is reset to the identity.</summary>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">A text object is already open.</exception>
    public PdfContentBuilder BeginText()
    {
        if (_inText)
            throw new InvalidOperationException("A text object is already open: text objects cannot be nested");

        _inText = true;
        return Operator("BT");
    }

    /// <summary>Ends the text object (<c>ET</c>).</summary>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">No text object is open.</exception>
    public PdfContentBuilder EndText()
    {
        EnsureInText();
        _inText = false;
        return Operator("ET");
    }

    /// <summary>Sets the font and the font size (<c>Tf</c>). The font is added to the resources.</summary>
    /// <param name="font">The font.</param>
    /// <param name="size">The font size, in user space units.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetFont(PdfFont font, double size)
    {
        ArgumentNullException.ThrowIfNull(font);
        var name = AddResource(PdfNames.Font, "F", font.GetReference(_document));
        _font = font;
        _fontSize = size;
        WriteName(name);
        return Operator("Tf", size);
    }

    /// <summary>Sets the character spacing (<c>Tc</c>), added after each glyph, in unscaled text space units.</summary>
    /// <param name="spacing">The spacing.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetCharacterSpacing(double spacing) => Operator("Tc", spacing);

    /// <summary>Sets the word spacing (<c>Tw</c>), added after each single-byte space character, in unscaled text space units.</summary>
    /// <param name="spacing">The spacing.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetWordSpacing(double spacing) => Operator("Tw", spacing);

    /// <summary>Sets the horizontal scaling of the glyphs (<c>Tz</c>), in percent.</summary>
    /// <param name="percent">The scaling; 100 is the normal width.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetHorizontalScaling(double percent) => Operator("Tz", percent);

    /// <summary>Sets the text leading (<c>TL</c>): the distance between the baselines of consecutive lines.</summary>
    /// <param name="leading">The leading, in unscaled text space units.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetTextLeading(double leading) => Operator("TL", leading);

    /// <summary>Sets the text rendering mode (<c>Tr</c>).</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetTextRenderingMode(PdfTextRenderingMode mode) => Operator("Tr", (int)mode);

    /// <summary>Sets the text rise (<c>Ts</c>): the distance to move the baseline up (superscript) or down (subscript).</summary>
    /// <param name="rise">The rise, in unscaled text space units.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetTextRise(double rise)
    {
        _textRise = rise;
        return Operator("Ts", rise);
    }

    /// <summary>Moves to the start of the next line, offset from the start of the current line (<c>Td</c>).</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder MoveTextPosition(double x, double y)
    {
        EnsureInText();
        return Operator("Td", x, y);
    }

    /// <summary>Moves to the start of the next line, offset from the start of the current line, and sets the leading to <c>-y</c> (<c>TD</c>).</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder MoveTextPositionAndSetLeading(double x, double y)
    {
        EnsureInText();
        return Operator("TD", x, y);
    }

    /// <summary>Sets the text matrix and the text line matrix (<c>Tm</c>).</summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetTextMatrix(PdfMatrix matrix)
    {
        EnsureInText();
        return Operator("Tm", matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
    }

    /// <summary>Moves to the start of the next line, using the leading (<c>T*</c>).</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder MoveToNextLine()
    {
        EnsureInText();
        return Operator("T*");
    }

    /// <summary>Shows a text with the current font (<c>Tj</c>).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, or no font is set.</exception>
    public PdfContentBuilder ShowText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_font is EmbeddedPdfFont)
            return ShowShapedText([text], nextLine: false);

        WriteText(text);
        return Operator("Tj");
    }

    /// <summary>Moves to the next line and shows a text (<c>'</c>).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, or no font is set.</exception>
    public PdfContentBuilder ShowTextOnNextLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_font is EmbeddedPdfFont)
            return ShowShapedText([text], nextLine: true);

        WriteText(text);
        return Operator("'");
    }

    /// <summary>Shows texts with individual adjustments of the positions of the glyphs (<c>TJ</c>), for kerning or justification.</summary>
    /// <param name="segments">The texts and the adjustments.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, or no font is set.</exception>
    public PdfContentBuilder ShowText(params ReadOnlySpan<PdfTextSegment> segments)
    {
        EnsureFont();
        if (_font is EmbeddedPdfFont)
            return ShowShapedText(segments, nextLine: false);

        _buffer.WriteByte((byte)'[');
        var first = true;
        foreach (var segment in segments)
        {
            if (!first)
            {
                _buffer.WriteSpace();
            }

            first = false;
            if (segment.Text is { } text)
            {
                PdfObjectSerializer.WriteString(_buffer, _font!.Encode(text), hexadecimal: false);
            }
            else
            {
                _buffer.WriteNumber(segment.Adjustment);
            }
        }

        _buffer.WriteAscii("] ");
        return Operator("TJ");
    }

    /// <summary>Shows a line of text at a position, in its own text object: <c>BT</c>, <c>Tf</c>, <c>Td</c>, <c>Tj</c>, <c>ET</c>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="fontSize">The font size.</param>
    /// <param name="x">The x coordinate of the start of the baseline.</param>
    /// <param name="y">The y coordinate of the baseline.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    public PdfContentBuilder DrawText(string text, PdfFont font, double fontSize, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        // Encode first: a character the font does not have leaves the content unchanged
        font.Encode(text);
        return BeginText().SetFont(font, fontSize).MoveTextPosition(x, y).ShowText(text).EndText();
    }

    /// <summary>Draws an image in a rectangle of the user space: <c>q</c>, <c>cm</c>, <c>Do</c>, <c>Q</c>. The image is added to the resources.</summary>
    /// <param name="image">The image.</param>
    /// <param name="x">The x coordinate of the lower-left corner.</param>
    /// <param name="y">The y coordinate of the lower-left corner.</param>
    /// <param name="width">The width of the image on the page.</param>
    /// <param name="height">The height of the image on the page.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder DrawImage(PdfImage image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        var name = AddResource(PdfNames.XObject, "Im", image.GetReference(_document));
        SaveGraphicsState();
        Transform(new PdfMatrix(width, 0, 0, height, x, y));
        WriteName(name);
        Operator("Do");
        return RestoreGraphicsState();
    }

    /// <summary>Paints a form XObject (<c>Do</c>). The form is added to the resources.</summary>
    /// <param name="form">The form, of the document of the builder.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The form belongs to another document.</exception>
    public PdfContentBuilder DrawForm(PdfForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Document != _document)
            throw new ArgumentException("The form belongs to another document", nameof(form));

        EnsureNotInText();
        WriteName(AddResource(PdfNames.XObject, "Fm", form.Reference));
        return Operator("Do");
    }

    /// <summary>Sets the parameters of a graphics state parameter dictionary (<c>gs</c>), such as the transparency. The dictionary is added to the resources.</summary>
    /// <param name="graphicsState">The parameters.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder SetGraphicsState(PdfExtGState graphicsState)
    {
        ArgumentNullException.ThrowIfNull(graphicsState);
        WriteName(AddResource(PdfNames.ExtGState, "GS", graphicsState.GetReference(_document)));
        return Operator("gs");
    }

    /// <summary>Paints a shading over the clipping region (<c>sh</c>). The shading is added to the resources.</summary>
    /// <param name="shading">The shading, in the current user space.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder PaintShading(PdfShading shading)
    {
        ArgumentNullException.ThrowIfNull(shading);
        EnsureNotInText();
        WriteName(AddResource(PdfNames.Shading, "Sh", shading.GetReference(_document)));
        return Operator("sh");
    }

    /// <summary>Sets a colored pattern as the nonstroking color (<c>/Pattern cs</c>, <c>scn</c>). The pattern is added to the resources.</summary>
    /// <param name="pattern">The pattern: a shading pattern or a colored tiling pattern.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The pattern is an uncolored tiling pattern, or it belongs to another document.</exception>
    public PdfContentBuilder SetFillPattern(PdfPattern pattern) => SetPattern(pattern, color: null, stroke: false);

    /// <summary>Sets a colored pattern as the stroking color (<c>/Pattern CS</c>, <c>SCN</c>). The pattern is added to the resources.</summary>
    /// <param name="pattern">The pattern: a shading pattern or a colored tiling pattern.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The pattern is an uncolored tiling pattern, or it belongs to another document.</exception>
    public PdfContentBuilder SetStrokePattern(PdfPattern pattern) => SetPattern(pattern, color: null, stroke: true);

    /// <summary>Sets an uncolored tiling pattern, painted with a color, as the nonstroking color. The pattern is added to the resources.</summary>
    /// <param name="pattern">The uncolored pattern.</param>
    /// <param name="color">The color the cell is painted with.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The pattern is colored, or it belongs to another document.</exception>
    public PdfContentBuilder SetFillPattern(PdfTilingPattern pattern, PdfColor color) => SetPattern(pattern, color, stroke: false);

    /// <summary>Sets an uncolored tiling pattern, painted with a color, as the stroking color. The pattern is added to the resources.</summary>
    /// <param name="pattern">The uncolored pattern.</param>
    /// <param name="color">The color the cell is painted with.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The pattern is colored, or it belongs to another document.</exception>
    public PdfContentBuilder SetStrokePattern(PdfTilingPattern pattern, PdfColor color) => SetPattern(pattern, color, stroke: true);

    /// <summary>Paints an external object of the resources by its name (<c>Do</c>).</summary>
    /// <param name="name">The name of the object in the <c>/XObject</c> resources.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder PaintXObject(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        EnsureNotInText();
        WriteName(name);
        return Operator("Do");
    }

    /// <summary>Begins a marked-content sequence (<c>BMC</c>).</summary>
    /// <param name="tag">The tag, such as <c>Span</c> or <c>Artifact</c>.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder BeginMarkedContent(string tag)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        _markedContentDepth++;
        WriteName(tag);
        return Operator("BMC");
    }

    /// <summary>Begins a marked-content sequence with a property list (<c>BDC</c>), such as <c>&lt;&lt;/MCID 0&gt;&gt;</c>.</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="properties">The property list, written inline. It must not contain references.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder BeginMarkedContent(string tag, PdfDictionary properties)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        ArgumentNullException.ThrowIfNull(properties);
        _markedContentDepth++;
        WriteName(tag);
        PdfObjectSerializer.Write(_buffer, properties, ReferenceWriter.Identity);
        _buffer.WriteSpace();
        return Operator("BDC");
    }

    /// <summary>Ends the marked-content sequence of the matching <c>BeginMarkedContent</c> call (<c>EMC</c>).</summary>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">No marked-content sequence is open.</exception>
    public PdfContentBuilder EndMarkedContent()
    {
        if (_markedContentDepth == 0)
            throw new InvalidOperationException("No marked-content sequence is open");

        _markedContentDepth--;
        return Operator("EMC");
    }

    /// <summary>Marks a point of the content (<c>MP</c>).</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder MarkPoint(string tag)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        WriteName(tag);
        return Operator("MP");
    }

    /// <summary>Marks a point of the content with a property list (<c>DP</c>).</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="properties">The property list, written inline.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder MarkPoint(string tag, PdfDictionary properties)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        ArgumentNullException.ThrowIfNull(properties);
        WriteName(tag);
        PdfObjectSerializer.Write(_buffer, properties, ReferenceWriter.Identity);
        _buffer.WriteSpace();
        return Operator("DP");
    }

    /// <summary>Appends content as it is, for operators the builder has no method for. The builder does not check it.</summary>
    /// <param name="content">The content, in the syntax of content streams.</param>
    /// <returns>The builder.</returns>
    public PdfContentBuilder AppendRawContent(ReadOnlySpan<byte> content)
    {
        _stream.AppendData(content);
        if (content.Length > 0 && content[^1] is not ((byte)'\n' or (byte)'\r' or (byte)' '))
        {
            _stream.AppendData("\n"u8);
        }

        return this;
    }

    /// <summary>Adds an object to a category of the resources and returns its name; an object already there keeps its name.</summary>
    internal string AddResource(PdfName category, string prefix, PdfObject reference)
    {
        if (_resources.Get<PdfDictionary>(category) is not { } dictionary)
        {
            dictionary = new PdfDictionary();
            _resources[category] = dictionary;
        }

        foreach (var entry in dictionary)
        {
            if (entry.Value.Equals(reference))
                return entry.Key.Value;
        }

        for (var i = 1; ; i++)
        {
            var name = prefix + i.ToString(CultureInfo.InvariantCulture);
            var key = PdfName.Create(name);
            if (!dictionary.ContainsKey(key))
            {
                dictionary[key] = reference;
                return name;
            }
        }
    }

    /// <summary>
    /// Shows texts with an embedded font: the glyphs the shaping gives, from left to right, in <c>TJ</c> arrays whose
    /// numbers move each glyph to its position, and a text rise (<c>Ts</c>) for the glyphs above or below the baseline.
    /// The text is a replacement text (<c>/ActualText</c>, ISO 32000-2, 14.9.4) when the glyphs cannot give it back.
    /// </summary>
    private PdfContentBuilder ShowShapedText(ReadOnlySpan<PdfTextSegment> segments, bool nextLine)
    {
        EnsureFont();
        var font = (EmbeddedPdfFont)_font!;

        // Shape first: a character the font does not have leaves the content unchanged
        var glyphs = new List<ShapedGlyph>();
        var pendingAdjustment = 0d;
        var needsActualText = false;
        var shapedTexts = new List<ShapedText>();
        var text = new System.Text.StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.Text is null)
            {
                pendingAdjustment += segment.Adjustment;
                continue;
            }

            var shaped = font.Shape(segment.Text);
            shapedTexts.Add(shaped);
            text.Append(segment.Text);
            needsActualText |= shaped.NeedsActualText;
            foreach (var glyph in shaped.Glyphs)
            {
                glyphs.Add(glyph with { Adjustment = glyph.Adjustment + pendingAdjustment });
                pendingAdjustment = 0;
            }

            pendingAdjustment += shaped.TrailingAdjustment;
        }

        var usage = font.GetUsage(_document);
        foreach (var shaped in shapedTexts)
        {
            needsActualText |= !usage.Record(shaped);
        }

        if (needsActualText)
        {
            WriteName("Span");
            PdfObjectSerializer.Write(_buffer, new PdfDictionary { [PdfNames.ActualText] = PdfString.FromText(text.ToString()) }, ReferenceWriter.Identity);
            _buffer.WriteSpace();
            Operator("BDC");
        }

        if (nextLine)
        {
            Operator("T*");
        }

        var rise = 0d;
        var start = 0;
        do
        {
            // The glyphs at one height: one TJ array
            var end = start;
            while (end < glyphs.Count && glyphs[end].Rise == glyphs[start].Rise)
            {
                end++;
            }

            if (end > start && glyphs[start].Rise != rise)
            {
                rise = glyphs[start].Rise;
                Operator("Ts", _textRise + (rise * _fontSize / 1000));
            }

            _buffer.WriteByte((byte)'[');
            var inString = false;
            for (var i = start; i < end; i++)
            {
                var glyph = glyphs[i];
                if (glyph.Adjustment != 0)
                {
                    if (inString)
                    {
                        _buffer.WriteByte((byte)'>');
                        inString = false;
                    }

                    _buffer.WriteNumber(glyph.Adjustment);
                }

                if (!inString)
                {
                    _buffer.WriteByte((byte)'<');
                    inString = true;
                }

                _buffer.WriteAscii(glyph.Glyph.ToString("X4", CultureInfo.InvariantCulture));
            }

            if (inString)
            {
                _buffer.WriteByte((byte)'>');
            }

            if (end == glyphs.Count && pendingAdjustment != 0)
            {
                _buffer.WriteNumber(pendingAdjustment);
            }

            _buffer.WriteAscii("] ");
            Operator("TJ");
            start = end;
        }
        while (start < glyphs.Count);

        if (rise != 0)
        {
            Operator("Ts", _textRise);
        }

        if (needsActualText)
        {
            Operator("EMC");
        }

        return this;
    }

    private PdfContentBuilder SetPattern(PdfPattern pattern, PdfColor? color, bool stroke)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var isUncolored = pattern is PdfTilingPattern { IsColored: false };
        if (isUncolored != color.HasValue)
            throw new ArgumentException(isUncolored ? "An uncolored tiling pattern needs a color" : "A colored pattern has its own colors", nameof(pattern));

        if (pattern is PdfTilingPattern tiling && tiling.Document != _document)
            throw new ArgumentException("The pattern belongs to another document", nameof(pattern));

        var name = AddResource(PdfNames.Pattern, "P", pattern.GetReference(_document));
        WriteName(color is { } value ? GetPatternColorSpace(value.ColorSpace) : "Pattern");
        Operator(stroke ? "CS" : "cs");

        // An uncolored pattern is in a pattern color space whose underlying color space is that of the color (8.7.3.3)
        if (color is { } components)
        {
            foreach (var component in components.GetComponents())
            {
                _buffer.WriteNumber(component);
                _buffer.WriteSpace();
            }
        }

        WriteName(name);
        return Operator(stroke ? "SCN" : "scn");
    }

    /// <summary>Gets the name of the color space resource <c>[/Pattern /DeviceX]</c>, adding it when the resources do not have it.</summary>
    private string GetPatternColorSpace(PdfDeviceColorSpace colorSpace)
    {
        var underlying = colorSpace switch
        {
            PdfDeviceColorSpace.Gray => PdfNames.DeviceGray,
            PdfDeviceColorSpace.Rgb => PdfNames.DeviceRGB,
            _ => PdfNames.DeviceCMYK,
        };
        if (_resources.Get<PdfDictionary>(PdfNames.ColorSpace) is { } colorSpaces)
        {
            foreach (var entry in colorSpaces)
            {
                if (PdfReference.ResolveIfReference(entry.Value) is PdfArray { Count: 2 } array && array[0].Equals(PdfNames.Pattern) && array[1].Equals(underlying))
                    return entry.Key.Value;
            }
        }

        return AddResource(PdfNames.ColorSpace, "CS", new PdfArray(PdfNames.Pattern, underlying));
    }

    private void WriteText(string text)
    {
        EnsureFont();
        PdfObjectSerializer.WriteString(_buffer, _font!.Encode(text), hexadecimal: false);
        _buffer.WriteSpace();
    }

    private void WriteName(string name)
    {
        PdfObjectSerializer.WriteName(_buffer, PdfName.Create(name));
        _buffer.WriteSpace();
    }

    private PdfContentBuilder ColorOperator(PdfColor color, bool stroke)
    {
        var name = color.ColorSpace switch
        {
            PdfDeviceColorSpace.Gray => stroke ? "G" : "g",
            PdfDeviceColorSpace.Rgb => stroke ? "RG" : "rg",
            _ => stroke ? "K" : "k",
        };
        return Operator(name, color.GetComponents());
    }

    private PdfContentBuilder PathOperator(string name, params ReadOnlySpan<double> operands)
    {
        EnsureNotInText();
        return Operator(name, operands);
    }

    private PdfContentBuilder Operator(string name, params ReadOnlySpan<double> operands)
    {
        foreach (var operand in operands)
        {
            _buffer.WriteNumber(operand);
            _buffer.WriteSpace();
        }

        _buffer.WriteAscii(name);
        _buffer.WriteNewLine();
        _stream.AppendData(_buffer.WrittenSpan);
        _buffer.Clear();
        return this;
    }

    private void EnsureInText()
    {
        if (!_inText)
            throw new InvalidOperationException("The operator is only allowed in a text object: call BeginText first");
    }

    private void EnsureNotInText()
    {
        if (_inText)
            throw new InvalidOperationException("The operator is not allowed in a text object: call EndText first");
    }

    private void EnsureFont()
    {
        EnsureInText();
        if (_font is null)
            throw new InvalidOperationException("No font is set: call SetFont first");
    }
}
