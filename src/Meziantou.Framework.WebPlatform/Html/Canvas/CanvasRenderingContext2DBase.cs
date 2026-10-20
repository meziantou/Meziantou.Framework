using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The members shared by the 2D rendering contexts of the canvases (https://html.spec.whatwg.org/multipage/canvas.html#2dcontext):
/// <see cref="CanvasRenderingContext2D"/> (<c>canvas.getContext("2d")</c>) and <see cref="OffscreenCanvasRenderingContext2D"/>.
/// </summary>
/// <remarks>
/// <para>
/// The library implements the state, the paths, the styles, the text layout (with the managed font reader and text shaping) and
/// the drawing model of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#drawing-model); each drawing operation is
/// a small display list (the clip region, the shadow, the compositing operator and the filters, then the shape in the
/// coordinates of the current transformation matrix), rasterized on the bitmap of the canvas by the rendering backend
/// (<see cref="BrowsingContextOptions.RenderingBackend"/>). Without a backend there is no context: <c>getContext("2d")</c> returns null.
/// </para>
/// <para>
/// The paths and the text are stored as the specification describes them: the current default path in the coordinates of the
/// canvas (its points are transformed when they are added), a <see cref="Path2D"/> in the coordinates where it is drawn.
/// </para>
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#canvasstate"/>
[IdlInterface("CanvasState", "https://html.spec.whatwg.org/multipage/canvas.html#canvasstate")]
public abstract partial class CanvasRenderingContext2DBase
{
    private readonly List<DrawingState> _stack = [];
    private readonly CanvasPath _path = new();
    private DrawingState _state = new();

    private protected CanvasRenderingContext2DBase(CanvasBitmap bitmap, CanvasRenderingContext2DSettings settings)
    {
        Bitmap = bitmap;
        Settings = settings;
    }

    /// <summary>The output bitmap of the context.</summary>
    internal CanvasBitmap Bitmap { get; }

    /// <summary>The settings the context was created with.</summary>
    internal CanvasRenderingContext2DSettings Settings { get; }

    /// <summary>The canvas element of the context, or null for an <see cref="OffscreenCanvas"/>.</summary>
    internal abstract HtmlCanvasElement? CanvasElement { get; }

    /// <summary>The global object of the context (for the fonts of a worker, the settings, the feature toggles), or null.</summary>
    internal abstract GlobalScope? Global { get; }

    /// <summary>Called when the pixels of the bitmap changed (the canvas is painted again).</summary>
    private protected abstract void OnBitmapChanged();

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasSettings, CanvasState (https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-state)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the settings of the context (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getcontextattributes).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getcontextattributes"/>
    [IdlMember("getContextAttributes", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getcontextattributes")]
    public CanvasRenderingContext2DSettings GetContextAttributes() => new()
    {
        Alpha = Settings.Alpha,
        Desynchronized = Settings.Desynchronized,
        ColorSpace = Settings.ColorSpace,
        ColorType = Settings.ColorType,
        WillReadFrequently = Settings.WillReadFrequently,
    };

    /// <summary>Pushes a copy of the drawing state on the stack (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-save).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-save"/>
    [IdlMember("save", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-save")]
    public void Save() => _stack.Add(_state.Clone());

    /// <summary>Pops the drawing state from the stack (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-restore); does nothing when the stack is empty.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-restore"/>
    [IdlMember("restore", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-restore")]
    public void Restore()
    {
        if (_stack.Count == 0)
            return;

        _state = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
    }

    /// <summary>
    /// Resets the context to its default state (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-reset): the
    /// bitmap is cleared, the drawing state and its stack, and the current default path are reset.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-reset"/>
    [IdlMember("reset", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-reset")]
    public void Reset()
    {
        ResetState();
        Bitmap.Clear();
        Bitmap.IsOriginClean = true;
        OnBitmapChanged();
    }

    /// <summary>Whether the context is lost (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-iscontextlost): never.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-iscontextlost"/>
    [IdlMember("isContextLost", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-iscontextlost")]
    public bool IsContextLost() => false;

    /// <summary>Resets the drawing state, its stack and the current default path (the bitmap is reset by the caller).</summary>
    internal void ResetState()
    {
        _state = new DrawingState();
        _stack.Clear();
        _path.Clear();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasTransform (https://html.spec.whatwg.org/multipage/canvas.html#transformations)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-scale</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-scale"/>
    [IdlMember("scale", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-scale")]
    public void Scale(double x, double y)
    {
        if (double.IsFinite(x) && double.IsFinite(y))
        {
            _state.Transform = _state.Transform.PreConcat(DisplayMatrix.Scale(x, y));
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rotate: a clockwise rotation by an angle in radians.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rotate"/>
    [IdlMember("rotate", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rotate")]
    public void Rotate(double angle)
    {
        if (double.IsFinite(angle))
        {
            var (sin, cos) = Math.SinCos(angle);
            _state.Transform = _state.Transform.PreConcat(new DisplayMatrix(cos, sin, -sin, cos, 0, 0));
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-translate</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-translate"/>
    [IdlMember("translate", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-translate")]
    public void Translate(double x, double y)
    {
        if (double.IsFinite(x) && double.IsFinite(y))
        {
            _state.Transform = _state.Transform.PreConcat(DisplayMatrix.Translation(x, y));
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-transform</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-transform"/>
    [IdlMember("transform", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-transform")]
    public void Transform(double a, double b, double c, double d, double e, double f)
    {
        if (double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c) && double.IsFinite(d) && double.IsFinite(e) && double.IsFinite(f))
        {
            _state.Transform = _state.Transform.PreConcat(new DisplayMatrix(a, b, c, d, e, f));
        }
    }

    /// <summary>Returns a copy of the current transformation matrix (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-gettransform).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-gettransform"/>
    [IdlMember("getTransform", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-gettransform")]
    public DomMatrix GetTransform()
    {
        var m = _state.Transform;
        return new DomMatrix([m.A, m.B, m.C, m.D, m.E, m.F]);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-settransform</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-settransform"/>
    [IdlMember("setTransform", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-settransform")]
    public void SetTransform(double a, double b, double c, double d, double e, double f)
    {
        if (double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c) && double.IsFinite(d) && double.IsFinite(e) && double.IsFinite(f))
        {
            _state.Transform = new DisplayMatrix(a, b, c, d, e, f);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-settransform-matrix</summary>
    /// <exception cref="TypeErrorException">The members of the dictionary are inconsistent.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-settransform-matrix"/>
    [IdlMember("setTransform", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-settransform-matrix")]
    public void SetTransform(DomMatrix2DInit? transform = null)
    {
        if (ToMatrix(transform) is { } matrix)
        {
            _state.Transform = matrix;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-resettransform</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-resettransform"/>
    [IdlMember("resetTransform", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-resettransform")]
    public void ResetTransform() => _state.Transform = DisplayMatrix.Identity;

    /// <summary>
    /// Creates a 2D matrix from a dictionary (https://drafts.fxtf.org/geometry/#create-a-dommatrix-from-the-2d-dictionary), or
    /// returns null when one of its components is not finite.
    /// </summary>
    internal static DisplayMatrix? ToMatrix(DomMatrix2DInit? init)
    {
        var matrix = DomMatrixReadOnly.FromMatrixInit(init);
        var result = new DisplayMatrix(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M41, matrix.M42);
        return double.IsFinite(result.A) && double.IsFinite(result.B) && double.IsFinite(result.C) && double.IsFinite(result.D) && double.IsFinite(result.E) && double.IsFinite(result.F) ? result : null;
    }

    /// <summary>Returns the inverse of a 2D matrix, or null when it is not invertible.</summary>
    internal static DisplayMatrix? Invert(DisplayMatrix m)
    {
        var determinant = (m.A * m.D) - (m.B * m.C);
        if (determinant == 0 || !double.IsFinite(determinant))
            return null;

        return new DisplayMatrix(m.D / determinant, -m.B / determinant, -m.C / determinant, m.A / determinant, ((m.C * m.F) - (m.D * m.E)) / determinant, ((m.B * m.E) - (m.A * m.F)) / determinant);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasCompositing, CanvasImageSmoothing (https://html.spec.whatwg.org/multipage/canvas.html#compositing)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets or sets the alpha applied to the drawing operations (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-globalalpha): the values outside of [0, 1] are ignored.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-globalalpha"/>
    [IdlMember("globalAlpha", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-globalalpha")]
    public double GlobalAlpha
    {
        get => _state.GlobalAlpha;
        set
        {
            if (double.IsFinite(value) && value is >= 0 and <= 1)
            {
                _state.GlobalAlpha = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the compositing operator or the blend mode of the drawing operations (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-globalcompositeoperation):
    /// a value of <c>&lt;blend-mode&gt;</c> or of the Porter-Duff operators of Compositing and Blending; the other values are ignored.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-globalcompositeoperation"/>
    [IdlMember("globalCompositeOperation", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-globalcompositeoperation")]
    public string GlobalCompositeOperation
    {
        get => _state.CompositeOperation;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ParseCompositeOperation(value) is { } mode)
            {
                _state.CompositeOperation = value;
                _state.BlendMode = mode;
            }
        }
    }

    /// <summary>Gets or sets whether the images are smoothed when they are scaled (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingenabled).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingenabled"/>
    [IdlMember("imageSmoothingEnabled", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingenabled")]
    public bool ImageSmoothingEnabled
    {
        get => _state.ImageSmoothingEnabled;
        set => _state.ImageSmoothingEnabled = value;
    }

    /// <summary>Gets or sets the quality of the image smoothing (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingquality).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingquality"/>
    [IdlMember("imageSmoothingQuality", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingquality")]
    public ImageSmoothingQuality ImageSmoothingQuality
    {
        get => _state.ImageSmoothingQuality;
        set => _state.ImageSmoothingQuality = value;
    }

    /// <summary>The sampling of the images drawn with the current image smoothing settings.</summary>
    private ImageSampling ImageSampling => !_state.ImageSmoothingEnabled ? ImageSampling.Pixelated : _state.ImageSmoothingQuality == ImageSmoothingQuality.High ? ImageSampling.HighQuality : ImageSampling.Smooth;

    /// <summary>https://drafts.fxtf.org/compositing/#ltblendmodegt and https://drafts.fxtf.org/compositing/#compositemode</summary>
    private static BlendMode? ParseCompositeOperation(string value) => value switch
    {
        "source-over" => BlendMode.Normal,
        "source-in" => BlendMode.SourceIn,
        "source-out" => BlendMode.SourceOut,
        "source-atop" => BlendMode.SourceAtop,
        "destination-over" => BlendMode.DestinationOver,
        "destination-in" => BlendMode.DestinationIn,
        "destination-out" => BlendMode.DestinationOut,
        "destination-atop" => BlendMode.DestinationAtop,
        "lighter" => BlendMode.Lighter,
        "copy" => BlendMode.Copy,
        "xor" => BlendMode.Xor,
        "clear" => BlendMode.Clear,
        "plus-lighter" => BlendMode.PlusLighter,
        "normal" => BlendMode.Normal,
        "multiply" => BlendMode.Multiply,
        "screen" => BlendMode.Screen,
        "overlay" => BlendMode.Overlay,
        "darken" => BlendMode.Darken,
        "lighten" => BlendMode.Lighten,
        "color-dodge" => BlendMode.ColorDodge,
        "color-burn" => BlendMode.ColorBurn,
        "hard-light" => BlendMode.HardLight,
        "soft-light" => BlendMode.SoftLight,
        "difference" => BlendMode.Difference,
        "exclusion" => BlendMode.Exclusion,
        "hue" => BlendMode.Hue,
        "saturation" => BlendMode.Saturation,
        "color" => BlendMode.Color,
        "luminosity" => BlendMode.Luminosity,
        _ => null,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasFillStrokeStyles (https://html.spec.whatwg.org/multipage/canvas.html#fill-and-stroke-styles)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the style of the strokes (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokestyle): a
    /// CSS color (serialized when it is read), a <see cref="CanvasGradient"/> or a <see cref="CanvasPattern"/>. The strings that
    /// are not colors are ignored.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokestyle"/>
    [IdlMember("strokeStyle", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokestyle")]
    [AllowNull]
    public object StrokeStyle
    {
        get => _state.StrokeStyle.Value;
        set
        {
            if (ParseStyle(value) is { } style)
            {
                _state.StrokeStyle = style;
            }
        }
    }

    /// <summary>
    /// Gets or sets the style of the fills (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillstyle): a CSS
    /// color (serialized when it is read), a <see cref="CanvasGradient"/> or a <see cref="CanvasPattern"/>. The strings that are
    /// not colors are ignored.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillstyle"/>
    [IdlMember("fillStyle", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillstyle")]
    [AllowNull]
    public object FillStyle
    {
        get => _state.FillStyle.Value;
        set
        {
            if (ParseStyle(value) is { } style)
            {
                _state.FillStyle = style;
            }
        }
    }

    /// <summary>Creates a linear gradient from (x0, y0) to (x1, y1) (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createlineargradient).</summary>
    /// <exception cref="TypeErrorException">An argument is not finite.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createlineargradient"/>
    [IdlMember("createLinearGradient", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createlineargradient")]
    public CanvasGradient CreateLinearGradient(double x0, double y0, double x1, double y1)
    {
        CheckFinite(x0, y0, x1, y1);
        return new CanvasGradient(CanvasGradientKind.Linear, [x0, y0, x1, y1], this);
    }

    /// <summary>Creates a radial gradient between two circles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createradialgradient).</summary>
    /// <exception cref="TypeErrorException">An argument is not finite.</exception>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when a radius is negative.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createradialgradient"/>
    [IdlMember("createRadialGradient", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createradialgradient")]
    public CanvasGradient CreateRadialGradient(double x0, double y0, double r0, double x1, double y1, double r1)
    {
        CheckFinite(x0, y0, r0, x1, y1, r1);
        if (r0 < 0 || r1 < 0)
            throw new DomException("The radii must not be negative", "IndexSizeError");

        return new CanvasGradient(CanvasGradientKind.Radial, [x0, y0, r0, x1, y1, r1], this);
    }

    /// <summary>Creates a conic gradient around (x, y) (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createconicgradient).</summary>
    /// <exception cref="TypeErrorException">An argument is not finite.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createconicgradient"/>
    [IdlMember("createConicGradient", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createconicgradient")]
    public CanvasGradient CreateConicGradient(double startAngle, double x, double y)
    {
        CheckFinite(startAngle, x, y);
        return new CanvasGradient(CanvasGradientKind.Conic, [startAngle, x, y], this);
    }

    private static void CheckFinite(params ReadOnlySpan<double> values)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new TypeErrorException("The value is not a finite number");
        }
    }

    /// <summary>Parses a value of <c>fillStyle</c> or <c>strokeStyle</c>, or returns null when it is ignored.</summary>
    private CanvasStyle? ParseStyle(object? value)
    {
        switch (value)
        {
            case CanvasGradient gradient:
                return new CanvasStyle(gradient, default);

            case CanvasPattern pattern:
                // https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillstyle: a pattern that is not origin-clean taints the canvas
                if (!pattern.IsOriginClean)
                {
                    Bitmap.IsOriginClean = false;
                }

                return new CanvasStyle(pattern, default);

            default:
                var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return CanvasColors.Parse(text, CanvasElement, Bitmap.Options.ColorSpace) is { } color ? new CanvasStyle(color.Serialization, color.Color) : null;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasShadowStyles (https://html.spec.whatwg.org/multipage/canvas.html#shadows)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets or sets the horizontal offset of the shadows (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowoffsetx).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowoffsetx"/>
    [IdlMember("shadowOffsetX", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowoffsetx")]
    public double ShadowOffsetX
    {
        get => _state.ShadowOffsetX;
        set
        {
            if (double.IsFinite(value))
            {
                _state.ShadowOffsetX = value;
            }
        }
    }

    /// <summary>Gets or sets the vertical offset of the shadows (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowoffsety).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowoffsety"/>
    [IdlMember("shadowOffsetY", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowoffsety")]
    public double ShadowOffsetY
    {
        get => _state.ShadowOffsetY;
        set
        {
            if (double.IsFinite(value))
            {
                _state.ShadowOffsetY = value;
            }
        }
    }

    /// <summary>Gets or sets the blur of the shadows (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowblur): the negative values are ignored.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowblur"/>
    [IdlMember("shadowBlur", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowblur")]
    public double ShadowBlur
    {
        get => _state.ShadowBlur;
        set
        {
            if (double.IsFinite(value) && value >= 0)
            {
                _state.ShadowBlur = value;
            }
        }
    }

    /// <summary>Gets or sets the color of the shadows (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowcolor): the strings that are not colors are ignored.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowcolor"/>
    [IdlMember("shadowColor", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-shadowcolor")]
    public string ShadowColor
    {
        get => _state.ShadowColorText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (CanvasColors.Parse(value, CanvasElement, Bitmap.Options.ColorSpace) is { } color)
            {
                _state.ShadowColorText = color.Serialization;
                _state.ShadowColor = color.Color;
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#when-shadows-are-drawn</summary>
    private bool ShadowsAreDrawn => _state.ShadowColor.A > 0 && (_state.ShadowBlur > 0 || _state.ShadowOffsetX != 0 || _state.ShadowOffsetY != 0);

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasPathDrawingStyles (https://html.spec.whatwg.org/multipage/canvas.html#line-styles)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets or sets the width of the lines (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linewidth): zero, negative and non-finite values are ignored.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linewidth"/>
    [IdlMember("lineWidth", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linewidth")]
    public double LineWidth
    {
        get => _state.LineWidth;
        set
        {
            if (double.IsFinite(value) && value > 0)
            {
                _state.LineWidth = value;
            }
        }
    }

    /// <summary>Gets or sets the shape of the ends of the lines (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linecap).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linecap"/>
    [IdlMember("lineCap", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linecap")]
    public CanvasLineCap LineCap
    {
        get => _state.LineCap;
        set => _state.LineCap = value;
    }

    /// <summary>Gets or sets the shape of the corners of the lines (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linejoin).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linejoin"/>
    [IdlMember("lineJoin", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linejoin")]
    public CanvasLineJoin LineJoin
    {
        get => _state.LineJoin;
        set => _state.LineJoin = value;
    }

    /// <summary>Gets or sets the miter limit ratio (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-miterlimit): zero, negative and non-finite values are ignored.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-miterlimit"/>
    [IdlMember("miterLimit", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-miterlimit")]
    public double MiterLimit
    {
        get => _state.MiterLimit;
        set
        {
            if (double.IsFinite(value) && value > 0)
            {
                _state.MiterLimit = value;
            }
        }
    }

    /// <summary>
    /// Sets the dash list of the lines (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-setlinedash): a list
    /// with a negative or non-finite value is ignored, a list with an odd number of values is repeated.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-setlinedash"/>
    [IdlMember("setLineDash", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-setlinedash")]
    public void SetLineDash(IReadOnlyList<double> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        foreach (var segment in segments)
        {
            if (!double.IsFinite(segment) || segment < 0)
                return;
        }

        _state.LineDash = segments.Count % 2 == 0 ? [.. segments] : [.. segments, .. segments];
    }

    /// <summary>Returns a copy of the dash list (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getlinedash).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getlinedash"/>
    [IdlMember("getLineDash", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getlinedash")]
    public IReadOnlyList<double> GetLineDash() => [.. _state.LineDash];

    /// <summary>Gets or sets the phase of the dash list (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linedashoffset): non-finite values are ignored.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linedashoffset"/>
    [IdlMember("lineDashOffset", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linedashoffset")]
    public double LineDashOffset
    {
        get => _state.LineDashOffset;
        set
        {
            if (double.IsFinite(value))
            {
                _state.LineDashOffset = value;
            }
        }
    }

    /// <summary>The stroke properties of the current line styles.</summary>
    private Rendering.StrokeStyle GetStrokeStyle()
    {
        var dashes = _state.LineDash.Length > 0 && Array.Exists(_state.LineDash, dash => dash > 0) ? _state.LineDash : null;
        return new Rendering.StrokeStyle(
            _state.LineWidth,
            _state.LineCap switch { CanvasLineCap.Round => Rendering.LineCap.Round, CanvasLineCap.Square => Rendering.LineCap.Square, _ => Rendering.LineCap.Butt },
            _state.LineJoin switch { CanvasLineJoin.Round => Rendering.LineJoin.Round, CanvasLineJoin.Bevel => Rendering.LineJoin.Bevel, _ => Rendering.LineJoin.Miter },
            _state.MiterLimit,
            dashes,
            _state.LineDashOffset);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasRect (https://html.spec.whatwg.org/multipage/canvas.html#drawing-rectangles-to-the-bitmap)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Clears a rectangle to transparent black (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clearrect): the
    /// clip region and the current transformation matrix apply, the shadows, the global alpha, the compositing and the filters do not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clearrect"/>
    [IdlMember("clearRect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clearrect")]
    public void ClearRect(double x, double y, double w, double h)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h) || w == 0 || h == 0 || !Bitmap.HasPixels)
            return;

        var builder = Bitmap.Builder;
        builder.Save();
        AddClip(builder);
        builder.Transform(_state.Transform);
        builder.Add(new DrawRectItem(NormalizeRect(x, y, w, h), new DisplayPaint(DisplayColor.Transparent) { BlendMode = BlendMode.Clear }));
        builder.Restore();
        OnDrawn();
    }

    /// <summary>Fills a rectangle with the fill style (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillrect).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillrect"/>
    [IdlMember("fillRect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillrect")]
    public void FillRect(double x, double y, double w, double h)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h) || w == 0 || h == 0)
            return;

        if (CreatePaint(_state.FillStyle, stroke: false) is not { } paint)
            return;

        var rect = NormalizeRect(x, y, w, h);
        DrawInUserSpace(new DrawRectItem(rect, paint));
    }

    /// <summary>Strokes the outline of a rectangle with the stroke style and the line styles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokerect).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokerect"/>
    [IdlMember("strokeRect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokerect")]
    public void StrokeRect(double x, double y, double w, double h)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h) || (w == 0 && h == 0))
            return;

        if (CreatePaint(_state.StrokeStyle, stroke: true) is not { } paint)
            return;

        // A rectangle of zero width or height is a closed line (it has joins and no caps, as in the browsers), else a closed subpath
        var path = new DisplayPath();
        if (w == 0 || h == 0)
        {
            path.MoveTo(x, y);
            path.LineTo(x + w, y + h);
            path.Close();
        }
        else
        {
            path.MoveTo(x, y);
            path.LineTo(x + w, y);
            path.LineTo(x + w, y + h);
            path.LineTo(x, y + h);
            path.Close();
        }

        DrawInUserSpace(new DrawPathItem(path, paint));
    }

    private static PhysicalRect NormalizeRect(double x, double y, double w, double h)
    {
        if (w < 0)
        {
            x += w;
            w = -w;
        }

        if (h < 0)
        {
            y += h;
            h = -h;
        }

        return new PhysicalRect(x, y, w, h);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasPath on the current default path (https://html.spec.whatwg.org/multipage/canvas.html#building-paths)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-closepath</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-closepath"/>
    [IdlMember("closePath", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-closepath")]
    public void ClosePath() => _path.ClosePath();

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-moveto</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-moveto"/>
    [IdlMember("moveTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-moveto")]
    public void MoveTo(double x, double y) => _path.MoveTo(x, y, _state.Transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lineto</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lineto"/>
    [IdlMember("lineTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lineto")]
    public void LineTo(double x, double y) => _path.LineTo(x, y, _state.Transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-quadraticcurveto</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-quadraticcurveto"/>
    [IdlMember("quadraticCurveTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-quadraticcurveto")]
    public void QuadraticCurveTo(double cpx, double cpy, double x, double y) => _path.QuadraticCurveTo(cpx, cpy, x, y, _state.Transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beziercurveto</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beziercurveto"/>
    [IdlMember("bezierCurveTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beziercurveto")]
    public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y) => _path.BezierCurveTo(cp1x, cp1y, cp2x, cp2y, x, y, _state.Transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arcto</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the radius is negative.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arcto"/>
    [IdlMember("arcTo", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arcto")]
    public void ArcTo(double x1, double y1, double x2, double y2, double radius) => _path.ArcTo(x1, y1, x2, y2, radius, _state.Transform, Invert(_state.Transform));

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rect</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rect"/>
    [IdlMember("rect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-rect")]
    public void Rect(double x, double y, double w, double h) => _path.Rect(x, y, w, h, _state.Transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect</summary>
    /// <param name="x">The x coordinate of the rectangle.</param>
    /// <param name="y">The y coordinate of the rectangle.</param>
    /// <param name="w">The width of the rectangle.</param>
    /// <param name="h">The height of the rectangle.</param>
    /// <param name="radii">A radius (a number or a <see cref="DomPointInit"/>), or a list of 1 to 4 radii.</param>
    /// <exception cref="RangeErrorException">The number of radii is not between 1 and 4, or a radius is negative.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect"/>
    [IdlMember("roundRect", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-roundrect")]
    public void RoundRect(double x, double y, double w, double h, object? radii = null)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h))
            return;

        if (CanvasPath.NormalizeRoundRectRadii(radii ?? 0d) is { } normalized)
        {
            _path.RoundRect(x, y, w, h, normalized, _state.Transform);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arc</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the radius is negative.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arc"/>
    [IdlMember("arc", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-arc")]
    public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise = false) => _path.Arc(x, y, radius, startAngle, endAngle, counterclockwise, _state.Transform);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ellipse</summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when a radius is negative.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ellipse"/>
    [IdlMember("ellipse", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ellipse")]
    public void Ellipse(double x, double y, double radiusX, double radiusY, double rotation, double startAngle, double endAngle, bool counterclockwise = false)
        => _path.Ellipse(x, y, radiusX, radiusY, rotation, startAngle, endAngle, counterclockwise, _state.Transform);

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasDrawPath (https://html.spec.whatwg.org/multipage/canvas.html#drawing-paths-to-the-canvas)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Empties the current default path (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beginpath).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beginpath"/>
    [IdlMember("beginPath", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-beginpath")]
    public void BeginPath() => _path.Clear();

    /// <summary>Fills the current default path with the fill style (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fill).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fill"/>
    [IdlMember("fill", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fill")]
    public void Fill(CanvasFillRule fillRule = CanvasFillRule.NonZero) => FillPath(_path, isDefaultPath: true, fillRule);

    /// <summary>Fills a path with the fill style (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fill).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fill"/>
    [IdlMember("fill", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fill")]
    public void Fill(Path2D path, CanvasFillRule fillRule = CanvasFillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        FillPath(path.Path, isDefaultPath: false, fillRule);
    }

    /// <summary>Strokes the current default path with the stroke style and the line styles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroke).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroke"/>
    [IdlMember("stroke", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroke")]
    public void Stroke() => StrokePath(_path, isDefaultPath: true);

    /// <summary>Strokes a path with the stroke style and the line styles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroke).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroke"/>
    [IdlMember("stroke", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroke")]
    public void Stroke(Path2D path)
    {
        ArgumentNullException.ThrowIfNull(path);
        StrokePath(path.Path, isDefaultPath: false);
    }

    /// <summary>Intersects the clip region with the current default path (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clip).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clip"/>
    [IdlMember("clip", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clip")]
    public void Clip(CanvasFillRule fillRule = CanvasFillRule.NonZero) => ClipPath(_path, isDefaultPath: true, fillRule);

    /// <summary>Intersects the clip region with a path (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clip).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clip"/>
    [IdlMember("clip", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-clip")]
    public void Clip(Path2D path, CanvasFillRule fillRule = CanvasFillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        ClipPath(path.Path, isDefaultPath: false, fillRule);
    }

    /// <summary>
    /// Returns whether a point (in the coordinates of the canvas, not transformed) is inside the current default path
    /// (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinpath); the points of the path are inside.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinpath"/>
    [IdlMember("isPointInPath", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinpath")]
    public bool IsPointInPath(double x, double y, CanvasFillRule fillRule = CanvasFillRule.NonZero) => IsPointInPath(_path, isDefaultPath: true, x, y, fillRule);

    /// <summary>Returns whether a point (in the coordinates of the canvas, not transformed) is inside a path transformed by the current transformation matrix.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinpath"/>
    [IdlMember("isPointInPath", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinpath")]
    public bool IsPointInPath(Path2D path, double x, double y, CanvasFillRule fillRule = CanvasFillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsPointInPath(path.Path, isDefaultPath: false, x, y, fillRule);
    }

    /// <summary>
    /// Returns whether a point (in the coordinates of the canvas, not transformed) is inside the stroke of the current default path
    /// with the line styles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinstroke).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinstroke"/>
    [IdlMember("isPointInStroke", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinstroke")]
    public bool IsPointInStroke(double x, double y) => IsPointInStroke(_path, isDefaultPath: true, x, y);

    /// <summary>Returns whether a point (in the coordinates of the canvas, not transformed) is inside the stroke of a path with the line styles.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinstroke"/>
    [IdlMember("isPointInStroke", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-ispointinstroke")]
    public bool IsPointInStroke(Path2D path, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsPointInStroke(path.Path, isDefaultPath: false, x, y);
    }

    /// <summary>
    /// Returns a path in user space: the current default path (stored in the coordinates of the canvas) mapped by the inverse of
    /// the current transformation matrix, or a <see cref="Path2D"/> as is; null when the matrix is not invertible.
    /// </summary>
    private DisplayPath? GetUserSpacePath(CanvasPath path, bool isDefaultPath, FillRule fillRule, bool stroke = false)
    {
        if (!isDefaultPath)
            return path.ToDisplayPath(fillRule, transform: null, pruneZeroLength: stroke);

        return Invert(_state.Transform) is { } inverse ? path.ToDisplayPath(fillRule, inverse, pruneZeroLength: stroke) : null;
    }

    private void FillPath(CanvasPath path, bool isDefaultPath, CanvasFillRule fillRule)
    {
        if (!path.HasSubpaths || CreatePaint(_state.FillStyle, stroke: false) is not { } paint)
            return;

        if (GetUserSpacePath(path, isDefaultPath, ToFillRule(fillRule)) is { } userPath)
        {
            DrawInUserSpace(new DrawPathItem(userPath, paint));
        }
    }

    private void StrokePath(CanvasPath path, bool isDefaultPath)
    {
        if (!path.HasSubpaths || CreatePaint(_state.StrokeStyle, stroke: true) is not { } paint)
            return;

        if (GetUserSpacePath(path, isDefaultPath, FillRule.NonZero, stroke: true) is { } userPath)
        {
            DrawInUserSpace(new DrawPathItem(userPath, paint));
        }
    }

    private void ClipPath(CanvasPath path, bool isDefaultPath, CanvasFillRule fillRule)
    {
        // The clip region is stored in the coordinates of the canvas (a Path2D is transformed by the current transformation matrix)
        var devicePath = isDefaultPath ? path.ToDisplayPath(ToFillRule(fillRule)) : path.ToDisplayPath(ToFillRule(fillRule), _state.Transform);
        _state.Clip = _state.Clip is { } clip ? [.. clip, devicePath] : [devicePath];
    }

    private bool IsPointInPath(CanvasPath path, bool isDefaultPath, double x, double y, CanvasFillRule fillRule)
    {
        // As in the browsers, nothing is inside a path with a non-invertible transformation matrix
        if (!double.IsFinite(x) || !double.IsFinite(y) || !path.HasSubpaths || Invert(_state.Transform) is null)
            return false;

        var commands = isDefaultPath ? path.ToSvgCommands() : path.ToSvgCommands(_state.Transform);
        return SvgShapeGeometry.FromCommands(commands).IsPointInFill(new SvgPathData.SvgPoint(x, y), fillRule == CanvasFillRule.EvenOdd);
    }

    private bool IsPointInStroke(CanvasPath path, bool isDefaultPath, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !path.HasSubpaths || Invert(_state.Transform) is not { } inverse)
            return false;

        // The stroke is computed in user space (the line width is in the units of the current transformation matrix)
        var commands = isDefaultPath ? path.ToSvgCommands(inverse) : path.ToSvgCommands();
        var stroke = GetStrokeStyle();
        var svgStroke = new SvgStrokeStyle(
            stroke.Width,
            _state.LineCap switch { CanvasLineCap.Round => "round", CanvasLineCap.Square => "square", _ => "butt" },
            _state.LineJoin switch { CanvasLineJoin.Round => "round", CanvasLineJoin.Bevel => "bevel", _ => "miter" },
            stroke.MiterLimit,
            stroke.Dashes,
            stroke.DashOffset,
            PathLength: null,
            NonScaling: false);
        var point = inverse.Apply(new PhysicalOffset(x, y));
        return SvgShapeGeometry.FromCommands(commands).IsPointInStroke(new SvgPathData.SvgPoint(point.X, point.Y), svgStroke, SvgAffine.Identity);
    }

    private static FillRule ToFillRule(CanvasFillRule fillRule) => fillRule == CanvasFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasUserInterface (https://html.spec.whatwg.org/multipage/canvas.html#drawing-focus-rings)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Draws a focus ring around the current default path when the element is focused and is a descendant of the canvas
    /// (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawfocusifneeded).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawfocusifneeded"/>
    [IdlMember("drawFocusIfNeeded", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawfocusifneeded")]
    public void DrawFocusIfNeeded(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        DrawFocusRing(_path, isDefaultPath: true, element);
    }

    /// <summary>
    /// Draws a focus ring around a path when the element is focused and is a descendant of the canvas
    /// (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawfocusifneeded).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawfocusifneeded"/>
    [IdlMember("drawFocusIfNeeded", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawfocusifneeded")]
    public void DrawFocusIfNeeded(Path2D path, Element element)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(element);
        DrawFocusRing(path.Path, isDefaultPath: false, element);
    }

    private void DrawFocusRing(CanvasPath path, bool isDefaultPath, Element element)
    {
        // 1. The element must be focused, and a descendant of the canvas element
        if (CanvasElement is not { } canvas || !canvas.IsShadowIncludingInclusiveAncestorOf(element) || !ReferenceEquals(element, element.NodeDocument.FocusedArea) || !path.HasSubpaths)
            return;

        // 2. The focus ring follows the system focus ring style (the color of the "auto" outlines): the shadows, the global alpha,
        // the filters and the compositing do not apply
        if (GetUserSpacePath(path, isDefaultPath, FillRule.NonZero) is not { } userPath || !Bitmap.HasPixels)
            return;

        var builder = Bitmap.Builder;
        builder.Save();
        AddClip(builder);
        builder.Transform(_state.Transform);
        builder.Add(new DrawPathItem(userPath, DisplayPaint.ForStroke(new DisplayColor(0x10 / 255f, 0x10 / 255f, 0x10 / 255f, 1), new Rendering.StrokeStyle(2, Rendering.LineCap.Butt, Rendering.LineJoin.Round))));
        builder.Restore();
        OnDrawn();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The drawing model (https://html.spec.whatwg.org/multipage/canvas.html#drawing-model)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the paint of a fill or stroke style with the global alpha (and the line styles for a stroke), or null when nothing
    /// is painted (a gradient without color stop or degenerate, a pattern whose image cannot be drawn).
    /// </summary>
    private DisplayPaint? CreatePaint(CanvasStyle style, bool stroke)
    {
        DisplayPaint paint;
        switch (style.Value)
        {
            case CanvasGradient gradient:
            {
                if (gradient.CreateShader() is not { } shader)
                    return null;

                paint = new DisplayPaint(new DisplayColor(0, 0, 0, (float)_state.GlobalAlpha)) { Shader = shader };
                break;
            }

            case CanvasPattern pattern:
            {
                if (pattern.CreateShader(ImageSampling) is not { } shader)
                    return null;

                paint = new DisplayPaint(new DisplayColor(0, 0, 0, (float)_state.GlobalAlpha)) { Shader = shader };
                break;
            }

            default:
                paint = new DisplayPaint(style.Color.MultiplyAlpha(_state.GlobalAlpha));
                break;
        }

        return stroke ? paint with { Style = PaintStyle.Stroke, Stroke = GetStrokeStyle() } : paint;
    }

    /// <summary>Draws an item whose coordinates are in user space (they are mapped by the current transformation matrix), through the drawing model.</summary>
    private void DrawInUserSpace(DisplayItem item)
    {
        // The browsers draw nothing with a non-invertible transformation matrix
        if (Invert(_state.Transform) is null)
            return;

        DrawShape(builder =>
        {
            builder.Save();
            builder.Transform(_state.Transform);
            builder.Add(item);
            builder.Restore();
        });
    }

    /// <summary>
    /// Draws a shape through the drawing model (https://html.spec.whatwg.org/multipage/canvas.html#drawing-model): the items of
    /// the shape (rendered with the fill or stroke style and the global alpha) are filtered (<c>filter</c>), their shadow is
    /// composited within the clip region with the compositing operator, then the shape itself.
    /// </summary>
    /// <param name="addItems">Adds the items of the shape, in the coordinates of the canvas (they add their own transform).</param>
    private void DrawShape(Action<DisplayListBuilder> addItems)
    {
        if (!Bitmap.HasPixels)
            return;

        var builder = Bitmap.Builder;
        builder.Save();
        AddClip(builder);
        var mode = _state.BlendMode;
        var filters = GetFilters();

        // The shadow is drawn in the coordinates of the canvas: its offset and its blur are not affected by the current
        // transformation matrix (the standard deviation of the Gaussian blur is half the shadow blur)
        if (ShadowsAreDrawn)
        {
            var shadow = new DropShadowFilter(new PhysicalOffset(_state.ShadowOffsetX, _state.ShadowOffsetY), _state.ShadowBlur / 2, _state.ShadowColor) { ShadowOnly = true };
            builder.SaveLayer(new SaveLayerItem(1, mode, filters is null ? [shadow] : [.. filters, shadow]));
            addItems(builder);
            builder.Restore();
        }

        // The operators that are not source-over composite the whole clip region (the image of the shape is transparent outside of the shape)
        if (mode == BlendMode.Normal && filters is null)
        {
            addItems(builder);
        }
        else
        {
            builder.SaveLayer(new SaveLayerItem(1, mode, filters));
            addItems(builder);
            builder.Restore();
        }

        builder.Restore();
        OnDrawn();
    }

    /// <summary>Intersects the clip of the display list with the clip region of the drawing state.</summary>
    private void AddClip(DisplayListBuilder builder)
    {
        if (_state.Clip is not { } clip)
            return;

        foreach (var path in clip)
        {
            builder.Add(new ClipPathItem(path));
        }
    }

    /// <summary>Records that the bitmap changed.</summary>
    private protected void OnDrawn()
    {
        Bitmap.OnChanged();
        OnBitmapChanged();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Drawing state (https://html.spec.whatwg.org/multipage/canvas.html#drawing-state)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A fill or stroke style: a color (its serialization and its value), a <see cref="CanvasGradient"/> or a <see cref="CanvasPattern"/>.</summary>
    private readonly record struct CanvasStyle(object Value, DisplayColor Color)
    {
        public static CanvasStyle Black { get; } = new("#000000", DisplayColor.Black);
    }

    /// <summary>The drawing state saved and restored by <c>save()</c> and <c>restore()</c>.</summary>
    private sealed class DrawingState
    {
        public DisplayMatrix Transform = DisplayMatrix.Identity;

        /// <summary>The paths the clip region is the intersection of (in the coordinates of the canvas), or null for no clip.</summary>
        public DisplayPath[]? Clip;

        public CanvasStyle StrokeStyle = CanvasStyle.Black;
        public CanvasStyle FillStyle = CanvasStyle.Black;
        public double GlobalAlpha = 1;
        public string CompositeOperation = "source-over";
        public BlendMode BlendMode = BlendMode.Normal;
        public bool ImageSmoothingEnabled = true;
        public ImageSmoothingQuality ImageSmoothingQuality = ImageSmoothingQuality.Low;
        public double LineWidth = 1;
        public CanvasLineCap LineCap = CanvasLineCap.Butt;
        public CanvasLineJoin LineJoin = CanvasLineJoin.Miter;
        public double MiterLimit = 10;
        public double[] LineDash = [];
        public double LineDashOffset;
        public double ShadowOffsetX;
        public double ShadowOffsetY;
        public double ShadowBlur;
        public string ShadowColorText = "rgba(0, 0, 0, 0)";
        public DisplayColor ShadowColor = DisplayColor.Transparent;
        public string Filter = "none";
        public CanvasFont? Font;
        public CanvasTextAlign TextAlign = CanvasTextAlign.Start;
        public CanvasTextBaseline TextBaseline = CanvasTextBaseline.Alphabetic;
        public CanvasDirection Direction = CanvasDirection.Inherit;
        public string Lang = "inherit";
        public string LetterSpacing = "0px";
        public string WordSpacing = "0px";
        public CanvasFontKerning FontKerning = CanvasFontKerning.Auto;
        public CanvasFontStretch FontStretch = CanvasFontStretch.Normal;
        public CanvasFontVariantCaps FontVariantCaps = CanvasFontVariantCaps.Normal;
        public CanvasTextRendering TextRendering = CanvasTextRendering.Auto;

        public DrawingState Clone() => (DrawingState)MemberwiseClone();
    }
}
