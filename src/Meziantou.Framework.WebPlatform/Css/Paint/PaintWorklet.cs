using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The paint worklet of a document (https://drafts.css-houdini.org/css-paint-api-1/#paint-worklet, <c>CSS.paintWorklet</c>):
/// its global scopes register the classes that paint the <c>paint()</c> images, and the document keeps what it needs to
/// know about them (the document paint definitions).
/// </summary>
/// <remarks>
/// A paint callback is invoked, synchronously, when a <c>paint()</c> image is painted (when the display list of the document
/// is built) and no image of the same paint, size, input property values and arguments was drawn before: the drawing
/// operations of the callback are recorded and cached.
/// </remarks>
internal sealed class PaintWorklet : Worklet
{
    // The number of recorded images kept (the cache is emptied when it is full)
    private const int MaxCachedImages = 256;

    // The paints of a global scope before another global scope is selected (https://drafts.css-houdini.org/css-paint-api-1/#global-scope-selection:
    // a global scope must not be used more than 1000 times in a row)
    private const int PaintsPerGlobalScope = 8;

    private readonly Dictionary<string, DocumentPaintDefinition?> _documentPaintDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DisplayList?> _images = new(StringComparer.Ordinal);
    private int _paintCount;
    private bool _isPainting;

    public PaintWorklet(Document document)
        : base(document)
    {
    }

    internal override RequestDestination Destination => RequestDestination.PaintWorklet;

    // https://drafts.css-houdini.org/css-paint-api-1/#global-scope-selection: the paint callbacks are invoked in at least two
    // global scopes, so that the classes cannot rely on the state of a global scope
    private protected override int GlobalScopeCount => 2;

    /// <summary>Whether a <c>paint()</c> image of the document was resolved: the layout and the painting depend on the registered paints.</summary>
    public bool HasPaintImages { get; set; }

    private protected override WorkletGlobalScope CreateGlobalScopeCore() => new PaintWorkletGlobalScope(this);

    private protected override void OnTerminated()
    {
        _documentPaintDefinitions.Clear();
        _images.Clear();
    }

    /// <summary>
    /// The task queued by <c>registerPaint()</c> (https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-registerpaint):
    /// the definition becomes invalid when the global scopes do not register equivalent classes.
    /// </summary>
    public void RegisterDocumentPaintDefinition(string name, DocumentPaintDefinition definition)
    {
        if (_documentPaintDefinitions.TryGetValue(name, out var existing))
        {
            if (existing is null || existing.IsEquivalent(definition))
                return;

            SetDocumentPaintDefinition(name, null);
            ReportError($"The paint '{name}' was registered with different inputProperties, inputArguments or contextOptions in the global scopes of the paint worklet.");
            return;
        }

        SetDocumentPaintDefinition(name, definition);
    }

    private void SetDocumentPaintDefinition(string name, DocumentPaintDefinition? definition)
    {
        _documentPaintDefinitions[name] = definition;
        _images.Clear();

        // The paint() images of the name are drawn again (they may also be used by the layout, e.g. as generated content)
        if (HasPaintImages)
        {
            Document.InvalidateLayout(rebuildBoxTree: true);
            Document.InvalidatePaint();
        }
    }

    /// <summary>Logs an error to the console of the document ("log an error to the debugging console").</summary>
    private void ReportError(string message)
        => Document.RelevantWindow?.ReportConsoleMessage(new ConsoleMessage("error", ConsoleMessageLevel.Error, message));

    /// <summary>
    /// Draws a paint image (https://drafts.css-houdini.org/css-paint-api-1/#draw-a-paint-image): returns the recorded drawing
    /// operations of the paint callback for an image of <paramref name="width"/> x <paramref name="height"/> CSS pixels (in
    /// the coordinates of the callback), or null for an invalid image.
    /// </summary>
    /// <param name="style">The computed style of the box (or of the pseudo-element) that draws the image.</param>
    /// <param name="function">The <c>paint()</c> function.</param>
    /// <param name="width">The width of the concrete object size.</param>
    /// <param name="height">The height of the concrete object size.</param>
    public DisplayList? DrawPaintImage(ComputedStyle style, CssFunction function, double width, double height)
    {
        if (!TryParseFunction(function, out var name, out var arguments))
            return null;

        if (!_documentPaintDefinitions.TryGetValue(name, out var documentDefinition) || documentDefinition is null)
            return null;

        // The arguments must match the registered grammar; they are passed as computed values
        var syntaxes = documentDefinition.InputArgumentSyntaxes;
        if (arguments.Count != syntaxes.Count)
            return null;

        var baseUrl = Document.DocumentBaseUrl.Serialize();
        var argumentValues = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            if (syntaxes[i].Compute(style, arguments[i], baseUrl) is not { } computed)
                return null;

            argumentValues[i] = computed;
        }

        if (!(width > 0) || !(height > 0) || IsTerminated || GlobalScopes.Count == 0 || Document.EnvironmentOptions?.RenderingBackend is not { } backend)
            return null;

        // The values of the input properties: an image of the same paint, size, style map and arguments is reused
        var styleMap = new PaintStylePropertyMap(style, documentDefinition.InputProperties);
        var devicePixelRatio = Document.RelevantWindow?.DevicePixelRatio ?? 1;
        var key = CreateKey(name, width, height, devicePixelRatio, styleMap, argumentValues);
        if (_images.TryGetValue(key, out var cached))
            return cached;

        // A paint callback cannot draw an image while another one runs (the display list is not built by a paint callback)
        if (_isPainting)
            return null;

        DisplayList? image;
        _isPainting = true;
        try
        {
            var globalScope = (PaintWorkletGlobalScope)GlobalScopes[_paintCount++ / PaintsPerGlobalScope % GlobalScopes.Count];
            image = InvokePaintCallback(globalScope, name, styleMap, syntaxes, argumentValues, width, height, backend);
        }
        finally
        {
            _isPainting = false;
        }

        if (_images.Count >= MaxCachedImages)
        {
            _images.Clear();
        }

        _images[key] = image;
        return image;
    }

    /// <summary>https://drafts.css-houdini.org/css-paint-api-1/#invoke-a-paint-callback</summary>
    private DisplayList? InvokePaintCallback(PaintWorkletGlobalScope globalScope, string name, PaintStylePropertyMap styleMap, IReadOnlyList<CustomPropertySyntax> syntaxes, string[] argumentValues, double width, double height, RenderingBackend backend)
    {
        if (!globalScope.CanRunScript)
            return null;

        if (!globalScope.PaintDefinitions.TryGetValue(name, out var definition))
        {
            // The class is not registered in all the global scopes
            globalScope.QueueGlobalTask(() =>
            {
                if (_documentPaintDefinitions.GetValueOrDefault(name) is not null)
                {
                    SetDocumentPaintDefinition(name, null);
                    ReportError($"The paint '{name}' is not registered in all the global scopes of the paint worklet.");
                }
            });
            return null;
        }

        var classConstructor = definition.ClassConstructor;
        try
        {
            if (!globalScope.PaintClassInstances.TryGetValue(name, out var paintInstance))
            {
                if (!definition.IsConstructorValid)
                    return null;

                try
                {
                    paintInstance = classConstructor.Construct();
                }
                catch (Exception)
                {
                    definition.IsConstructorValid = false;
                    throw;
                }

                globalScope.PaintClassInstances[name] = paintInstance;
            }

            // The output bitmap records the drawing operations, in CSS pixels
            var bitmap = CanvasBitmap.CreateRecording(backend, (int)Math.Min(Math.Ceiling(width), int.MaxValue), (int)Math.Min(Math.Ceiling(height), int.MaxValue), new RenderingSurfaceOptions(definition.Alpha));
            var renderingContext = new PaintRenderingContext2D(globalScope, bitmap, definition.Alpha);
            var arguments = new List<CssStyleValue>(argumentValues.Length);
            for (var i = 0; i < argumentValues.Length; i++)
            {
                var values = CssStyleValueReifier.ReifyWithSyntax("", syntaxes[i], argumentValues[i]);
                arguments.Add(values is [var single] ? single : new CssStyleValue("", argumentValues[i]));
            }

            using (AuthorCodeScope.Enter())
            {
                classConstructor.Invoke(definition.PaintFunction, paintInstance, [renderingContext, new PaintSize(width, height), styleMap, arguments]);
            }

            var recording = bitmap.TakeRecording();
            if (definition.Alpha)
                return recording;

            // A context without alpha is opaque: what the callback left transparent is black
            var builder = new DisplayListBuilder();
            builder.AddList(recording);
            builder.Add(new DrawRectItem(new PhysicalRect(0, 0, width, height), new DisplayPaint(DisplayColor.Black) { BlendMode = BlendMode.DestinationOver }));
            return builder.ToDisplayList(new PhysicalSize(width, height));
        }
#pragma warning disable CA1031 // An exception of the constructor or of the callback makes the image invalid, and is reported
        catch (Exception ex)
#pragma warning restore CA1031
        {
            classConstructor.ReportException(ex);
            return null;
        }
    }

    /// <summary>
    /// Parses <c>paint( &lt;ident&gt;, &lt;declaration-value&gt;? )</c> (https://drafts.css-houdini.org/css-paint-api-1/#paint-notation):
    /// the name of the paint, and the text of the arguments separated by commas.
    /// </summary>
    internal static bool TryParseFunction(CssFunction function, out string name, out List<string> arguments)
    {
        name = "";
        arguments = [];
        var parts = ValueMatcher.SplitAtCommas(function.Value);
        if (parts.Count == 0 || ValueMatcher.RemoveWhitespace(parts[0]) is not [CssPreservedToken { Type: CssTokenType.Ident } ident])
            return false;

        name = ident.Token.Value;
        for (var i = 1; i < parts.Count; i++)
        {
            arguments.Add(CssSerializer.SerializeValue(parts[i], property: null).Trim(' ', '\t', '\n'));
        }

        return true;
    }

    private static string CreateKey(string name, double width, double height, double devicePixelRatio, PaintStylePropertyMap styleMap, string[] arguments)
    {
        var builder = new StringBuilder();
        builder.Append(name).Append('\n')
            .Append(width.ToString("R", CultureInfo.InvariantCulture)).Append('x').Append(height.ToString("R", CultureInfo.InvariantCulture))
            .Append('@').Append(devicePixelRatio.ToString("R", CultureInfo.InvariantCulture));
        foreach (var argument in arguments)
        {
            builder.Append("\na:").Append(argument);
        }

        styleMap.AppendKey(builder);
        return builder.ToString();
    }
}

/// <summary>
/// The style map of a paint callback (https://drafts.css-houdini.org/css-paint-api-1/#invoke-a-paint-callback): the computed
/// values of the input properties of the paint class, read when the callback is invoked (the map is not live). It is also
/// the style map of the callbacks of a layout class and of their children, for the input properties and the child input
/// properties of the class (https://drafts.css-houdini.org/css-layout-api-1/#get-a-style-map).
/// </summary>
internal sealed class PaintStylePropertyMap : StylePropertyMapReadOnly
{
    private readonly Document _document;
    private readonly List<(string Name, string? Value)> _values;

    public PaintStylePropertyMap(ComputedStyle style, IReadOnlyList<string> inputProperties)
    {
        _document = style.Element.NodeDocument;
        _values = new List<(string, string?)>(inputProperties.Count);
        ComputedStyleDeclarations? declarations = null;
        foreach (var property in inputProperties)
        {
            if (_values.Exists(entry => entry.Name == property))
                continue;

            string? value;
            if (CssPropertyRegistry.IsCustomProperty(property))
            {
                value = style.GetCustomPropertyValue(property);
            }
            else if (CssPropertyRegistry.Get(property) is not { } definition)
            {
                value = null;
            }
            else if (definition.IsShorthand)
            {
                declarations ??= ComputedStyleDeclarations.Create(style.Element, style.PseudoElement);
                value = declarations.GetPropertyValue(property);
            }
            else
            {
                value = (style.TryGetAnchorPositionedStyle(definition.Name) ?? style).GetComputedValue(definition.Name);
            }

            _values.Add((property, value));
        }
    }

    /// <summary>Appends the values of the properties to the key of a cached image.</summary>
    public void AppendKey(StringBuilder builder)
    {
        foreach (var (name, value) in _values)
        {
            builder.Append('\n').Append(name).Append(':').Append(value ?? "\0");
        }
    }

    private protected override List<CssStyleValue>? GetValues(string property)
    {
        foreach (var (name, value) in _values)
        {
            if (name != property)
                continue;

            // As in the browsers, a custom property that has no value is an empty list of component values
            if (value is null)
                return CssPropertyRegistry.IsCustomProperty(property) ? CssStyleValueReifier.Reify(property, "", computed: true) : null;

            return CssPropertyRegistry.IsCustomProperty(property)
                ? ComputedStylePropertyMap.ReifyCustomProperty(_document, property, value)
                : CssStyleValueReifier.Reify(property, value, computed: true);
        }

        return null;
    }

    private protected override IEnumerable<string> GetNames() => _values.Where(static entry => entry.Value is not null || CssPropertyRegistry.IsCustomProperty(entry.Name)).Select(static entry => entry.Name);
}
