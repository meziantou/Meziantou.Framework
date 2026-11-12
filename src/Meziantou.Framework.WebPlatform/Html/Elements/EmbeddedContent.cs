using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

// The media elements are in MediaElements.cs, the iframe and frame elements in NavigableContainers.cs.

public partial class HtmlCanvasElement : ICanvasHost
{
    /// <summary>
    /// The rendering context of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-context-mode): null
    /// (the context mode is none), a <see cref="CanvasRenderingContext2D"/> or an <see cref="ImageBitmapRenderingContext"/>.
    /// </summary>
    internal object? RenderingContext { get; private set; }

    /// <summary>
    /// Whether the canvas is the placeholder of an <see cref="OffscreenCanvas"/> (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-placeholder):
    /// it displays the frames committed by the <see cref="OffscreenCanvas"/> and has no context.
    /// </summary>
    internal bool IsPlaceholder { get; private set; }

    /// <summary>The last frame committed by the <see cref="OffscreenCanvas"/> of a placeholder canvas, and its image for the rendering backend, or null.</summary>
    internal (BitmapPixels Pixels, Rendering.PaintImage? Image)? PlaceholderFrame { get; private set; }

    /// <summary>
    /// The size of the bitmap of a placeholder canvas: the size of the canvas when its control was transferred, then the size of
    /// the frames committed by its <see cref="OffscreenCanvas"/>. The <c>width</c> and <c>height</c> content attributes do not
    /// change it (https://html.spec.whatwg.org/multipage/canvas.html#attr-canvas-width: "do nothing" for a placeholder).
    /// </summary>
    private (int Width, int Height) _placeholderBitmapSize;

    /// <summary>
    /// The size of the bitmap of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element), which gives
    /// its natural dimensions: the size of the bitmap of the <see cref="OffscreenCanvas"/> for a placeholder canvas, else its
    /// <c>width</c> and <c>height</c>.
    /// </summary>
    internal (int Width, int Height) BitmapSize => IsPlaceholder ? _placeholderBitmapSize : ((int)Math.Min(Width, int.MaxValue), (int)Math.Min(Height, int.MaxValue));

    int ICanvasHost.BitmapWidth => BitmapSize.Width;

    int ICanvasHost.BitmapHeight => BitmapSize.Height;

    /// <summary>
    /// Returns the rendering context of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-getcontext):
    /// a <see cref="CanvasRenderingContext2D"/> for <c>"2d"</c> and an <see cref="ImageBitmapRenderingContext"/> for
    /// <c>"bitmaprenderer"</c> (created the first time), or null for a context type that is not supported (<c>"webgl"</c>,
    /// <c>"webgl2"</c>, <c>"webgpu"</c>) or that does not match the context of the canvas. The 2D context needs a rendering
    /// backend (<see cref="BrowsingContextOptions.RenderingBackend"/>): without one, <c>getContext("2d")</c> returns null.
    /// </summary>
    /// <param name="contextId">The type of the context.</param>
    /// <param name="options">The options of the context.</param>
    /// <returns>The context, or null.</returns>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is the placeholder of an <see cref="OffscreenCanvas"/>.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-getcontext"/>
    [IdlMember("getContext", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-getcontext")]
    public object? GetContext(string contextId, CanvasRenderingContext2DSettings? options = null)
    {
        ArgumentNullException.ThrowIfNull(contextId);
        if (IsPlaceholder)
            throw new DomException("The canvas has transferred its control to an OffscreenCanvas", "InvalidStateError");

        switch (RenderingContext)
        {
            case null when contextId == "2d":
            {
                var settings = options ?? new CanvasRenderingContext2DSettings();
                var global = RelevantGlobal ?? GlobalScope.Current;
                if (CanvasContexts.CreateBitmap(global, NodeDocument.RenderingBackend ?? global?.Options.RenderingBackend, (int)Math.Min(Width, int.MaxValue), (int)Math.Min(Height, int.MaxValue), settings) is not { } bitmap)
                    return null;

                RenderingContext = new CanvasRenderingContext2D(this, bitmap, settings);
                return RenderingContext;
            }

            case null when contextId == "bitmaprenderer":
                RenderingContext = new ImageBitmapRenderingContext(this, options?.Alpha ?? true);
                return RenderingContext;

            case CanvasRenderingContext2D context when contextId == "2d":
                return context;

            case ImageBitmapRenderingContext context when contextId == "bitmaprenderer":
                return context;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns a <c>data:</c> URL of the bitmap serialized as an image file (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-todataurl):
    /// PNG, JPEG or WebP (another type is serialized as PNG), or <c>data:,</c> when the canvas has no pixels.
    /// </summary>
    /// <param name="type">The MIME type of the file.</param>
    /// <param name="quality">The quality of the lossy formats from 0 to 1 (another value is the default quality), or null for the default quality.</param>
    /// <exception cref="DomException">A <c>SecurityError</c> when the canvas is not origin-clean.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-todataurl"/>
    [IdlMember("toDataURL", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-todataurl")]
    public string ToDataUrl(string type = "image/png", double? quality = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        (RenderingContext as CanvasRenderingContext2D)?.ThrowIfLayersAreOpen();
        var pixels = GetBitmapSnapshot();
        if (!pixels.IsOriginClean)
            throw new DomException("The canvas has been tainted by cross-origin data", "SecurityError");

        return CanvasContexts.ToDataUrl(CanvasContexts.Serialize(pixels, type, quality, GetRenderingBackend()));
    }

    /// <summary>
    /// Serializes the bitmap as an image file (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-toblob) and invokes
    /// the callback with a <see cref="FileApi.Blob"/> of the file in a task (null when the canvas has no pixels).
    /// </summary>
    /// <param name="callback">The callback.</param>
    /// <param name="type">The MIME type of the file (PNG, JPEG or WebP; another type is serialized as PNG).</param>
    /// <param name="quality">The quality of the lossy formats: a number from 0 to 1, else the default quality.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> for the placeholder of an <see cref="OffscreenCanvas"/>, a <c>SecurityError</c> when the canvas is not origin-clean.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-toblob"/>
    [IdlMember("toBlob", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-toblob")]
    public void ToBlob(BlobCallback callback, string type = "image/png", double? quality = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(type);

        // 1-2. A placeholder canvas, a canvas that is not origin-clean
        if (IsPlaceholder)
            throw new DomException("The canvas has transferred its control to an OffscreenCanvas", "InvalidStateError");

        (RenderingContext as CanvasRenderingContext2D)?.ThrowIfLayersAreOpen();
        var pixels = GetBitmapSnapshot();
        if (!pixels.IsOriginClean)
            throw new DomException("The canvas has been tainted by cross-origin data", "SecurityError");

        // 3-4. The serialization (null without pixels), then the callback in a task of the canvas blob serialization task source
        var file = CanvasContexts.Serialize(pixels, type, quality, GetRenderingBackend());
        var blob = file is var (data, mimeType) ? new FileApi.Blob([data], new FileApi.BlobPropertyBag { Type = mimeType }) : null;
        void Invoke()
        {
            try
            {
                callback(blob);
            }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
            catch (Exception ex)
#pragma warning restore CA1031
            {
                if (RelevantGlobal is { } global)
                {
                    Scripting.ScriptErrors.Report(ex, global);
                }
            }
        }

        if (RelevantGlobal is { } relevantGlobal)
        {
            relevantGlobal.QueueGlobalTask(Invoke);
        }
        else
        {
            Invoke();
        }
    }

    /// <summary>
    /// Transfers the control of the canvas to an <see cref="OffscreenCanvas"/> (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-transfercontroltooffscreen):
    /// the canvas becomes its placeholder and displays the frames drawn by its rendering context (e.g. in a worker).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas has a context or was already transferred.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-transfercontroltooffscreen"/>
    [IdlMember("transferControlToOffscreen", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-transfercontroltooffscreen")]
    public OffscreenCanvas TransferControlToOffscreen()
    {
        if (RenderingContext is not null || IsPlaceholder)
            throw new DomException("The canvas has a rendering context", "InvalidStateError");

        var offscreen = new OffscreenCanvas(Width, Height)
        {
            Placeholder = this,
            InheritsRightToLeft = CanvasRenderingContext2DBase.GetElementDirection(this),
            InheritedLanguage = Css.Selectors.HtmlElementStates.GetLanguage(this) ?? "",
        };
        if (RelevantGlobal is { } global)
        {
            offscreen.SetRelevantGlobalCore(global);
        }

        _placeholderBitmapSize = BitmapSize;
        IsPlaceholder = true;
        return offscreen;
    }

    /// <summary>
    /// Returns a copy of the bitmap of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#a-copy-of-the-image-data):
    /// the pixels of its 2D context, the output bitmap of its <see cref="ImageBitmapRenderingContext"/>, the last frame of its
    /// <see cref="OffscreenCanvas"/>, else transparent black pixels of the size of the canvas.
    /// </summary>
    internal BitmapPixels GetBitmapSnapshot() => RenderingContext switch
    {
        CanvasRenderingContext2D context => context.Bitmap.GetPixels(),
        ImageBitmapRenderingContext context => context.GetBitmap(),
        _ when PlaceholderFrame is { } frame => frame.Pixels,
        _ => BitmapPixels.CreateTransparent(BitmapSize.Width, BitmapSize.Height),
    };

    /// <summary>Displays a frame committed by the <see cref="OffscreenCanvas"/> of a placeholder canvas.</summary>
    internal void SetPlaceholderFrame(BitmapPixels pixels, Rendering.PaintImage? image)
    {
        PlaceholderFrame = (pixels, image);
        if (_placeholderBitmapSize != (pixels.Width, pixels.Height))
        {
            // The natural size of the canvas changed
            _placeholderBitmapSize = (pixels.Width, pixels.Height);
            NodeDocument.InvalidateLayout(rebuildBoxTree: false);
        }

        // The size of the placeholder follows the size of the frames of the OffscreenCanvas (its width and height reflect it, as
        // in the browsers)
        if (pixels.Width != Width)
        {
            Reflection.SetUnsignedLong(this, "width", positive: false, positiveWithFallback: false, defaultValue: 300, (uint)pixels.Width);
        }

        if (pixels.Height != Height)
        {
            Reflection.SetUnsignedLong(this, "height", positive: false, positiveWithFallback: false, defaultValue: 150, (uint)pixels.Height);
        }

        OnBitmapChanged();
    }

    private Rendering.RenderingBackend? GetRenderingBackend() => (RenderingContext as CanvasRenderingContext2D)?.Bitmap.Backend ?? NodeDocument.RenderingBackend ?? RelevantGlobal?.Options.RenderingBackend;

    /// <summary>The bitmap of the canvas changed: the document is painted again.</summary>
    internal void OnBitmapChanged() => NodeDocument.LayoutEngineOrNull?.InvalidatePaint();

    void ICanvasHost.OnBitmapChanged() => OnBitmapChanged();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-set-bitmap-dimensions: setting the width or the height
    /// (even to its current value) resets the 2D context and its bitmap.
    /// </summary>
    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName is "width" or "height" && RenderingContext is CanvasRenderingContext2D context)
        {
            context.ResetState();
            context.Bitmap.Resize((int)Math.Min(Width, int.MaxValue), (int)Math.Min(Height, int.MaxValue));
            OnBitmapChanged();
        }
    }
}

/// <summary>The callback of <c>HTMLCanvasElement.toBlob()</c> (https://html.spec.whatwg.org/multipage/canvas.html#blobcallback).</summary>
/// <param name="blob">The image file, or null when the canvas has no pixels.</param>
/// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#blobcallback"/>
[IdlInterface("BlobCallback", "https://html.spec.whatwg.org/multipage/canvas.html#blobcallback")]
public delegate void BlobCallback(FileApi.Blob? blob);

public partial class HtmlScriptElement
{
    /// <summary>The state and the processing model of the script element.</summary>
    internal ScriptElementCore Core => field ??= new ScriptElementCore(this);

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async"/>
    [IdlMember("async", "https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async")]
    public bool Async
    {
        get => Core.Async;
        set => Core.Async = value;
    }

    /// <summary>The "force async" flag is unset by the parser (https://html.spec.whatwg.org/multipage/scripting.html#script-force-async).</summary>
    internal bool ForceAsync
    {
        get => Core.ForceAsync;
        set => Core.ForceAsync = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#parser-document: set by the HTML and XML parsers on the script elements they insert.</summary>
    internal Document? ParserDocument
    {
        get => Core.ParserDocument;
        set => Core.ParserDocument = value;
    }

    /// <summary>
    /// The line (1-based) and the column (0-based) in its document of the source text of a script created by the HTML parser,
    /// for the positions of the errors of an inline script; (1, 0) for the other scripts.
    /// </summary>
    internal (int Line, int Column) SourceTextPosition
    {
        get => Core.SourceTextPosition;
        set => Core.SourceTextPosition = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#parser-inserted</summary>
    internal bool IsParserInserted => Core.IsParserInserted;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#already-started</summary>
    internal bool AlreadyStarted
    {
        get => Core.AlreadyStarted;
        set => Core.AlreadyStarted = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-type: determined when the element is prepared.</summary>
    internal ScriptElementType? ScriptType => Core.ScriptType;

    /// <summary>
    /// The script text (https://w3c.github.io/trusted-types/dist/spec/#htmlscriptelement-script-text): the child text content
    /// set by a Trusted Types compliant sink. When it differs from the child text content, the default policy is applied to the
    /// child text content when the element is prepared.
    /// </summary>
    internal string ScriptText
    {
        get => Core.ScriptText;
        set => Core.ScriptText = value;
    }

    /// <summary>
    /// The result of the script element (https://html.spec.whatwg.org/multipage/scripting.html#concept-script-result), see
    /// <see cref="ScriptElementCore.Result"/>.
    /// </summary>
    internal object? Result
    {
        get => Core.Result;
        private set => Core.Result = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-script-text</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-script-text"/>
    [IdlMember("text", "https://html.spec.whatwg.org/multipage/scripting.html#dom-script-text")]
    public string Text
    {
        get => GetChildTextContent();
        set => SetTextCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, value ?? "", "HTMLScriptElement text"));
    }

    /// <summary>The text setter with a <see cref="TrustedScript"/> value (https://w3c.github.io/trusted-types/dist/spec/#the-text-idl-attribute).</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#the-text-idl-attribute"/>
    [IdlMember("text", "https://w3c.github.io/trusted-types/dist/spec/#the-text-idl-attribute")]
    public void SetText(TrustedScript value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetTextCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, value, "HTMLScriptElement text"));
    }

    private void SetTextCore(string value)
    {
        ScriptText = value;
        StringReplaceAll(value);
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#the-textContent-idl-attribute: the attribute of HTMLScriptElement hides
    /// the one of Node, which does not apply Trusted Types (the default policy is then applied when the script is prepared).
    /// </summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#the-textContent-idl-attribute"/>
    [IdlMember("textContent", "https://w3c.github.io/trusted-types/dist/spec/#the-textContent-idl-attribute")]
    public new string? TextContent
    {
        get => base.TextContent;
        set => SetTextContentCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, value ?? "", "HTMLScriptElement textContent"));
    }

    /// <summary>The textContent setter with a <see cref="TrustedScript"/> value (https://w3c.github.io/trusted-types/dist/spec/#the-textContent-idl-attribute).</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#the-textContent-idl-attribute"/>
    [IdlMember("textContent", "https://w3c.github.io/trusted-types/dist/spec/#the-textContent-idl-attribute")]
    public void SetTextContent(TrustedScript value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetTextContentCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, value, "HTMLScriptElement textContent"));
    }

    private void SetTextContentCore(string value)
    {
        ScriptText = value;
        base.TextContent = value;
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#the-innerText-idl-attribute: the attribute of HTMLScriptElement hides
    /// the one of HTMLElement, which does not apply Trusted Types (the default policy is then applied when the script is prepared).
    /// </summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#the-innerText-idl-attribute"/>
    [IdlMember("innerText", "https://w3c.github.io/trusted-types/dist/spec/#the-innerText-idl-attribute")]
    public new string InnerText
    {
        get => base.InnerText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetInnerTextCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, value, "HTMLScriptElement innerText"));
        }
    }

    /// <summary>The innerText setter with a <see cref="TrustedScript"/> value (https://w3c.github.io/trusted-types/dist/spec/#the-innerText-idl-attribute).</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#the-innerText-idl-attribute"/>
    [IdlMember("innerText", "https://w3c.github.io/trusted-types/dist/spec/#the-innerText-idl-attribute")]
    public void SetInnerText(TrustedScript value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetInnerTextCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, value, "HTMLScriptElement innerText"));
    }

    private void SetInnerTextCore(string value)
    {
        ScriptText = value;
        base.InnerText = value;
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#the-src-idl-attribute</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-script-src"/>
    [IdlMember("src", "https://html.spec.whatwg.org/multipage/scripting.html#dom-script-src")]
    public string Src
    {
        get => Reflection.GetUrl(this, "src");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Reflection.SetString(this, "src", TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScriptUrl, NodeDocument, value, "HTMLScriptElement src"));
        }
    }

    /// <summary>The src setter with a <see cref="TrustedScriptUrl"/> value (https://w3c.github.io/trusted-types/dist/spec/#the-src-idl-attribute).</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#the-src-idl-attribute"/>
    [IdlMember("src", "https://w3c.github.io/trusted-types/dist/spec/#the-src-idl-attribute")]
    public void SetSrc(TrustedScriptUrl value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Reflection.SetString(this, "src", TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScriptUrl, NodeDocument, value, "HTMLScriptElement src"));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-script-supports</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-script-supports"/>
    [IdlMember("supports", "https://html.spec.whatwg.org/multipage/scripting.html#dom-script-supports")]
    public static bool Supports(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type is "classic" or "module" or "importmap" || (type == "speculationrules" && IsSpeculationRulesSupported);
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        Core.AttributeChangeSteps(localName, value, @namespace);

        // https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism: the blocking attribute "is changed so
        // that el is no longer potentially render-blocking"
        if (localName == "blocking" && @namespace is null && !Core.IsPotentiallyRenderBlocking)
        {
            Core.StopBlockingRendering();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model: the cloning steps copy already started.</summary>
    internal override void CloningSteps(Node copy, bool subtree)
    {
        base.CloningSteps(copy, subtree);
        ((HtmlScriptElement)copy).AlreadyStarted = AlreadyStarted;
    }

    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        Core.PostConnectionSteps();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model: the script children changed steps. As in
    /// browsers, removing a child never prepares the script (https://github.com/whatwg/html/pull/10188).
    /// </summary>
    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);
        if (childrenInserted && IsConnected)
        {
            Core.PostConnectionSteps();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element</summary>
    internal void Prepare() => Core.Prepare();

    /// <summary>
    /// The type of the script given its type and language attributes (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element,
    /// steps 8 to 13), or null when the script block's type string is not supported.
    /// </summary>
    internal ScriptElementType? GetScriptTypeFromAttributes() => Core.GetScriptTypeFromAttributes();

    /// <summary>https://mimesniff.spec.whatwg.org/#javascript-mime-type-essence-match</summary>
    internal static bool IsJavaScriptMimeTypeEssenceMatch(string value) => InfraAscii.ToAsciiLowercase(value) is
        "application/ecmascript" or "application/javascript" or "application/x-ecmascript" or "application/x-javascript" or
        "text/ecmascript" or "text/javascript" or "text/javascript1.0" or "text/javascript1.1" or "text/javascript1.2" or
        "text/javascript1.3" or "text/javascript1.4" or "text/javascript1.5" or "text/jscript" or "text/livescript" or
        "text/x-ecmascript" or "text/x-javascript";
}

/// <summary>The type of a script element (https://html.spec.whatwg.org/multipage/scripting.html#concept-script-type).</summary>
internal enum ScriptElementType
{
    Classic,
    Module,
    ImportMap,
    SpeculationRules,
}

public partial class HtmlStyleElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#dom-style-disabled: the disabled flag of the associated style sheet.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/semantics.html#dom-style-disabled"/>
    [IdlMember("disabled", "https://html.spec.whatwg.org/multipage/semantics.html#dom-style-disabled")]
    public bool Disabled
    {
        get => GetLinkStyleSheet()?.Disabled ?? false;
        set
        {
            if (GetLinkStyleSheet() is { } sheet)
            {
                sheet.Disabled = value;
            }
        }
    }
}

public partial class HtmlTitleElement
{
    /// <summary>Gets or sets the text content of the <c>title</c> element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/semantics.html#dom-title-text"/>
    [IdlMember("text", "https://html.spec.whatwg.org/multipage/semantics.html#dom-title-text")]
    public string Text
    {
        get => GetChildTextContent();
        set => StringReplaceAll(value ?? "");
    }
}

public partial class HtmlMapElement
{
    private HtmlCollection? _areas;

    /// <summary>Gets the <c>area</c> elements of the image map.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/image-maps.html#dom-map-areas"/>
    [IdlMember("areas", "https://html.spec.whatwg.org/multipage/image-maps.html#dom-map-areas")]
    public HtmlCollection Areas => _areas ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Area));
}

public partial class HtmlMarqueeElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-loop</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-loop"/>
    [IdlMember("loop", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-loop")]
    public int Loop
    {
        get
        {
            var value = GetAttributeValue("loop");
            return value is not null && Microsyntaxes.TryParseInteger(value, out var parsed) && parsed is > 0 and <= int.MaxValue ? (int)parsed : -1;
        }
        set
        {
            if (value <= 0 && value != -1)
                throw DomException.IndexSize("The value must be positive or -1");

            SetAttributeValue("loop", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-start: the marquee is never rendered.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-start"/>
    [IdlMember("start", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-start")]
    public void Start()
    {
    }

    /// <summary>Stops the scrolling of the <c>marquee</c> (no effect, as the marquee is never rendered).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-stop"/>
    [IdlMember("stop", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-marquee-stop")]
    public void Stop()
    {
    }
}

public partial class HtmlProgressElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-progress-position</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-progress-position"/>
    [IdlMember("position", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-progress-position")]
    public double Position
    {
        get
        {
            if (!HasAttributeWithLocalName("value"))
                return -1;

            var maximum = GetAttributeValue("max") is { } max && Microsyntaxes.TryParseFloat(max, out var parsedMax) && parsedMax > 0 ? parsedMax : 1.0;
            return Value / maximum;
        }
    }
}

public partial class HtmlDialogElement
{
    private string _returnValue = "";
    private ToggleTaskTracker? _toggleTaskTracker;
    private Element? _previouslyFocusedElement;
    private string? _requestCloseReturnValue;
    private Element? _requestCloseSourceElement;
    private bool _enableCloseWatcherForRequestClose;

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-close-watcher</summary>
    private InternalCloseWatcher? _closeWatcher;

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-returnvalue</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-returnvalue"/>
    [IdlMember("returnValue", "https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-returnvalue")]
    public string ReturnValue
    {
        get => _returnValue;
        set => _returnValue = value ?? "";
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-show</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-show"/>
    [IdlMember("show", "https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-show")]
    public void Show()
    {
        if (HasAttributeWithLocalName("open"))
        {
            if (!IsModal)
                return;

            throw DomException.InvalidState("The dialog is already open as a modal dialog");
        }

        if (!FireEvent(new ToggleEvent("beforetoggle", new ToggleEventInit { Cancelable = true, OldState = "closed", NewState = "open" })))
            return;

        if (HasAttributeWithLocalName("open"))
            return;

        QueueDialogToggleEventTask("closed", "open", source: null);
        SetAttributeValue("open", "");
        var document = NodeDocument;
        _previouslyFocusedElement = FocusModel.GetFocusedElement(document);
        var hideUntil = GetTopmostPopoverAncestor(this, source: null, isPopover: false);
        document.HidePopoversUntil(hideUntil, focusPreviousElement: false, fireEvents: true);
        FocusModel.RunDialogFocusingSteps(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-showmodal</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-showmodal"/>
    [IdlMember("showModal", "https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-showmodal")]
    public void ShowModal() => ShowModal(source: null);

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-close</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-close"/>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-close")]
    public void Close(string? returnValue = null) => CloseDialog(returnValue, source: null);

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-requestclose</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-requestclose"/>
    [IdlMember("requestClose", "https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-requestclose")]
    public void RequestClose(string? returnValue = null) => RequestCloseDialog(returnValue, source: null);

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#show-a-modal-dialog</summary>
    private void ShowModal(Element? source)
    {
        if (HasAttributeWithLocalName("open"))
        {
            if (IsModal)
                return;

            throw DomException.InvalidState("The dialog is already open as a non-modal dialog");
        }

        if (!NodeDocument.IsFullyActive)
            throw DomException.InvalidState("The document of the dialog is not fully active");

        if (!IsConnected)
            throw DomException.InvalidState("The dialog is not connected");

        if (IsPopoverShowing)
            throw DomException.InvalidState("The dialog is a showing popover");

        if (!FireEvent(new ToggleEvent("beforetoggle", new ToggleEventInit { Cancelable = true, OldState = "closed", NewState = "open", Source = source })))
            return;

        if (HasAttributeWithLocalName("open") || !IsConnected || IsPopoverShowing)
            return;

        QueueDialogToggleEventTask("closed", "open", source);
        SetAttributeValue("open", "");
        IsModal = true;

        // The document is blocked by the modal dialog: the other nodes are inert (see FocusModel.IsInert)
        var document = NodeDocument;
        if (!document.TopLayerContains(this))
        {
            document.AddToTopLayer(this);
        }

        _previouslyFocusedElement = FocusModel.GetFocusedElement(document);
        var hideUntil = GetTopmostPopoverAncestor(this, source: null, isPopover: false);
        document.HidePopoversUntil(hideUntil, focusPreviousElement: false, fireEvents: true);
        FocusModel.RunDialogFocusingSteps(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#close-the-dialog</summary>
    private void CloseDialog(string? result, Element? source)
    {
        if (!HasAttributeWithLocalName("open"))
            return;

        FireEvent(new ToggleEvent("beforetoggle", new ToggleEventInit { OldState = "open", NewState = "closed", Source = source }));
        if (!HasAttributeWithLocalName("open"))
            return;

        QueueDialogToggleEventTask("open", "closed", source);
        RemoveAttributeByLocalName("open");
        if (IsModal)
        {
            NodeDocument.RequestTopLayerRemoval(this);
        }

        var wasModal = IsModal;
        IsModal = false;
        if (result is not null)
        {
            _returnValue = result;
        }

        _requestCloseReturnValue = null;
        _requestCloseSourceElement = null;
        if (_previouslyFocusedElement is { } element)
        {
            _previouslyFocusedElement = null;
            // Not in the spec, but as in the browsers: the focus is also restored when the focused element is a flat tree
            // descendant of the dialog (an element slotted into a dialog of a shadow tree)
            if (wasModal || (NodeDocument.FocusedArea is { } focusedArea && (IsShadowIncludingInclusiveAncestorOf(focusedArea) || IsFlatTreeDescendant(focusedArea, this))))
            {
                FocusModel.Focus(element);
            }
        }

        // Queue an element task on the user interaction task source to fire the close event
        NodeDocument.QueueElementTask(() => FireEvent(new Event("close")));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-request-close</summary>
    private void RequestCloseDialog(string? returnValue, Element? source)
    {
        if (!HasAttributeWithLocalName("open"))
            return;

        if (!IsConnected || !NodeDocument.IsFullyActive)
            return;

        _enableCloseWatcherForRequestClose = true;
        _requestCloseReturnValue = returnValue;
        _requestCloseSourceElement = source;
        try
        {
            _closeWatcher?.RequestClose(requireHistoryActionActivation: false);
        }
        finally
        {
            _enableCloseWatcherForRequestClose = false;

            // Not in the spec, but as in the browsers (and the WPT tests): when the cancel event prevented the close, the
            // return value and the source element of this request are not the ones of the next close request of the user
            _requestCloseReturnValue = null;
            _requestCloseSourceElement = null;
        }
    }

    /// <summary>
    /// The light dismiss of the topmost open dialog (https://html.spec.whatwg.org/multipage/interactive-elements.html#light-dismiss-open-dialogs):
    /// when its computed closed-by state is Any, its close watcher is requested to close.
    /// </summary>
    internal void LightDismiss()
    {
        if (ComputeClosedBy() != "any")
            return;

        _closeWatcher?.RequestClose(requireHistoryActionActivation: false);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interactive-elements.html#set-the-dialog-close-watcher: the cancel event (it
    /// can prevent the close when the close watcher allows it), then the dialog is closed. The close watcher is disabled
    /// when the computed closed-by state is None, except for requestClose().
    /// </summary>
    private void SetDialogCloseWatcher()
    {
        _closeWatcher?.Destroy();
        _closeWatcher = null;
        if (NodeDocument.RelevantWindow is not { } window)
            return;

        _closeWatcher = InternalCloseWatcher.Establish(
            window,
            cancelAction: canPreventClose => FireEvent(new Event("cancel", new EventInit { Cancelable = canPreventClose })),
            closeAction: () => CloseDialog(_requestCloseReturnValue, _requestCloseSourceElement),
            getEnabledState: () => _enableCloseWatcherForRequestClose || ComputeClosedBy() != "none");
    }

    /// <summary>The command steps of the dialog element (https://html.spec.whatwg.org/multipage/interactive-elements.html#the-dialog-element:command-steps).</summary>
    internal void RunCommandSteps(HtmlButtonElement source, ButtonCommand command)
    {
        if (IsPopoverShowing)
            return;

        var isOpen = HasAttributeWithLocalName("open");
        if (command == ButtonCommand.Close && isOpen)
        {
            CloseDialog(source.OptionalValue, source);
        }
        else if (command == ButtonCommand.RequestClose && isOpen)
        {
            RequestCloseDialog(source.OptionalValue, source);
        }
        else if (command == ButtonCommand.ShowModal && !isOpen)
        {
            ShowModal(source);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#queue-a-dialog-toggle-event-task</summary>
    private void QueueDialogToggleEventTask(string oldState, string newState, Element? source)
    {
        _toggleTaskTracker = ToggleTaskTracker.Queue(this, _toggleTaskTracker, tracker => _toggleTaskTracker = tracker, oldState, newState, source);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-setup-steps</summary>
    private void DialogSetupSteps()
    {
        var openDialogs = NodeDocument.OpenDialogs;
        if (!openDialogs.Contains(this))
        {
            openDialogs.Add(this);
        }

        SetDialogCloseWatcher();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-cleanup-steps</summary>
    private void DialogCleanupSteps()
    {
        NodeDocument.OpenDialogs.Remove(this);
        if (_closeWatcher is { } closeWatcher)
        {
            closeWatcher.Destroy();
            _closeWatcher = null;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#the-dialog-element:html-element-insertion-steps</summary>
    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        if (NodeDocument.IsFullyActive && IsConnected && HasAttributeWithLocalName("open"))
        {
            DialogSetupSteps();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#the-dialog-element:html-element-removing-steps</summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        if (HasAttributeWithLocalName("open"))
        {
            DialogCleanupSteps();
        }

        NodeDocument.RemoveFromTopLayer(this);
        IsModal = false;
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null || localName != "open")
            return;

        if (value is null && oldValue is not null)
        {
            DialogCleanupSteps();
        }

        if (!NodeDocument.IsFullyActive || !IsConnected)
            return;

        if (value is not null && oldValue is null)
        {
            DialogSetupSteps();
        }
    }
}

public partial class HtmlAudioElement
{
    /// <summary>Creates an audio element whose preload attribute is "auto" (https://html.spec.whatwg.org/multipage/media.html#dom-audio).</summary>
    /// <param name="document">The document of the element (the associated document of the current global object).</param>
    /// <param name="src">The value of the src attribute, or null.</param>
    /// <returns>The new element.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-audio"/>
    [IdlMember("Audio", "https://html.spec.whatwg.org/multipage/media.html#dom-audio")]
    public static HtmlAudioElement Create(Document document, string? src = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var audio = (HtmlAudioElement)ElementFactory.CreateElement(document, "audio", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        audio.SetAttributeValue("preload", "auto");
        if (src is not null)
        {
            audio.SetAttributeValue("src", src);
        }

        return audio;
    }
}
