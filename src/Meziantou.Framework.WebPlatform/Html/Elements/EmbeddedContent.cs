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

    int ICanvasHost.BitmapWidth => (int)Math.Min(Width, int.MaxValue);

    int ICanvasHost.BitmapHeight => (int)Math.Min(Height, int.MaxValue);

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
        _ => BitmapPixels.CreateTransparent((int)Math.Min(Width, int.MaxValue), (int)Math.Min(Height, int.MaxValue)),
    };

    /// <summary>Displays a frame committed by the <see cref="OffscreenCanvas"/> of a placeholder canvas.</summary>
    internal void SetPlaceholderFrame(BitmapPixels pixels, Rendering.PaintImage? image)
    {
        PlaceholderFrame = (pixels, image);

        // The size of the placeholder follows the size of the frames of the OffscreenCanvas
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
    private bool _forceAsync = true;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async"/>
    [IdlMember("async", "https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async")]
    public bool Async
    {
        get => _forceAsync || HasAttributeWithLocalName("async");
        set
        {
            _forceAsync = false;
            Reflection.SetBoolean(this, "async", value);
        }
    }

    /// <summary>The "force async" flag is unset by the parser (https://html.spec.whatwg.org/multipage/scripting.html#script-force-async).</summary>
    internal bool ForceAsync
    {
        get => _forceAsync;
        set => _forceAsync = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#parser-document: set by the HTML and XML parsers on the script elements they insert.</summary>
    internal Document? ParserDocument { get; set; }

    /// <summary>
    /// The line (1-based) and the column (0-based) in its document of the source text of a script created by the HTML parser,
    /// for the positions of the errors of an inline script; (1, 0) for the other scripts.
    /// </summary>
    internal (int Line, int Column) SourceTextPosition
    {
        get => _sourceTextPosition ?? (1, 0);
        set => _sourceTextPosition = value;
    }

    private (int Line, int Column)? _sourceTextPosition;

    /// <summary>Whether <see cref="SourceTextPosition"/> is the position of the source text in the document (the script was created by the HTML parser).</summary>
    internal bool HasSourceTextPosition => _sourceTextPosition is not null;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#parser-inserted</summary>
    internal bool IsParserInserted => ParserDocument is not null;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#preparation-time-document</summary>
    internal Document? PreparationTimeDocument { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#already-started</summary>
    internal bool AlreadyStarted { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-external</summary>
    internal bool FromExternalFile { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-type: determined when the element is prepared.</summary>
    internal ScriptElementType? ScriptType { get; private set; }

    /// <summary>
    /// The script text (https://w3c.github.io/trusted-types/dist/spec/#htmlscriptelement-script-text): the child text content
    /// set by a Trusted Types compliant sink. When it differs from the child text content, the default policy is applied to the
    /// child text content when the element is prepared.
    /// </summary>
    internal string ScriptText { get; set; } = "";

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

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#prepare-script-text: applies the default policy to the child text content
    /// when it was not set by a Trusted Types compliant sink. Returns false when the script must not run.
    /// </summary>
    private bool PrepareScriptText()
    {
        var childTextContent = GetChildTextContent();
        if (ScriptText == childTextContent)
            return true;

        try
        {
            ScriptText = TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, childTextContent, "HTMLScriptElement text");
            return true;
        }
#pragma warning disable CA1031 // The exceptions of the default policy are reported and the script does not run
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // "If that algorithm threw an error, then return": the error of the default policy is reported
            if (ex is not TypeErrorException)
            {
                Scripting.ScriptErrors.Report(ex);
            }

            return false;
        }
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
        if (@namespace is not null)
            return;

        // https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model: setting async unsets force async
        if (localName == "async")
        {
            _forceAsync = false;
        }
        else if (localName == "src" && value is not null && IsConnected)
        {
            ScriptPostConnectionSteps();
        }
        else if (localName == "blocking" && !IsPotentiallyRenderBlocking)
        {
            // https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism: the blocking attribute "is changed so
            // that el is no longer potentially render-blocking"
            StopBlockingRendering();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#potentially-render-blocking: the blocking attribute of the
    /// element contains "render", or the element is implicitly potentially render-blocking
    /// (https://html.spec.whatwg.org/multipage/scripting.html#the-script-element: a parser-inserted classic script without
    /// async or defer attribute).
    /// </summary>
    internal bool IsPotentiallyRenderBlocking => Document.HasRenderBlockingToken(this)
        || (ScriptType == ScriptElementType.Classic && IsParserInserted && !HasAttributeWithLocalName("async") && !HasAttributeWithLocalName("defer"));

    /// <summary>The document whose rendering is blocked by the element (https://html.spec.whatwg.org/multipage/dom.html#block-rendering).</summary>
    private Document? _documentWhoseRenderingIsBlocked;

    private void BlockRendering(Document document)
    {
        _documentWhoseRenderingIsBlocked = document;
        document.BlockRendering(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#unblock-rendering</summary>
    internal void StopBlockingRendering()
    {
        if (_documentWhoseRenderingIsBlocked is not { } document)
            return;

        _documentWhoseRenderingIsBlocked = null;
        document.UnblockRendering(this);
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
        ScriptPostConnectionSteps();
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
            ScriptPostConnectionSteps();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model: the script HTML element post-connection steps.</summary>
    private void ScriptPostConnectionSteps()
    {
        if (IsParserInserted)
            return;

        Prepare();
    }

    /// <summary>
    /// The result of the script element (https://html.spec.whatwg.org/multipage/scripting.html#concept-script-result): the
    /// script (a <see cref="Scripting.ClassicScript"/> or a <see cref="Scripting.ModuleScript"/>), or null when it could not
    /// be fetched. It is "uninitialized" until <see cref="HasResult"/> is true.
    /// </summary>
    internal object? Result { get; private set; }

    /// <summary>Whether the result is not "uninitialized".</summary>
    internal bool HasResult { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#steps-to-run-when-the-result-is-ready</summary>
    private Action? _stepsToRunWhenTheResultIsReady;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#ready-to-be-parser-executed</summary>
    internal bool ReadyToBeParserExecuted { get; private set; }

    /// <summary>Called when <see cref="ReadyToBeParserExecuted"/> becomes true (the parser waiting for the script resumes).</summary>
    internal Action? ReadyToBeParserExecutedCallback { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#concept-script-delay-load</summary>
    private bool _delayingTheLoadEvent;

    private void SetDelayingTheLoadEvent(bool value)
    {
        if (_delayingTheLoadEvent == value || PreparationTimeDocument is not { } document)
            return;

        _delayingTheLoadEvent = value;
        document.ScriptsDelayingTheLoadEvent += value ? 1 : -1;
        if (!value)
        {
            document.TryQueueLoadEvent();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#mark-as-ready</summary>
    private void MarkAsReady(object? result)
    {
        Result = result;
        HasResult = true;
        var steps = _stepsToRunWhenTheResultIsReady;
        _stepsToRunWhenTheResultIsReady = null;
        steps?.Invoke();
        SetDelayingTheLoadEvent(false);
    }

    private void SetReadyToBeParserExecuted()
    {
        ReadyToBeParserExecuted = true;
        var callback = ReadyToBeParserExecutedCallback;
        ReadyToBeParserExecutedCallback = null;
        callback?.Invoke();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element. The script is fetched by the
    /// <see cref="BrowsingContextOptions.DocumentLoader"/> and run by the <see cref="BrowsingContextOptions.ScriptEngine"/>:
    /// nothing is fetched or run when the browsing context has no script engine.
    /// </summary>
    internal void Prepare()
    {
        if (AlreadyStarted)
            return;

        var parserDocument = ParserDocument;
        ParserDocument = null;
        if (parserDocument is not null && !HasAttributeWithLocalName("async"))
        {
            _forceAsync = true;
        }

        // https://w3c.github.io/trusted-types/dist/spec/#slot-value-verification
        if (!PrepareScriptText())
            return;

        var sourceText = ScriptText;
        var src = GetAttributeValue("src");
        if (src is null && sourceText.Length == 0)
            return;

        if (!IsConnected)
            return;

        if (GetScriptTypeFromAttributes() is not { } scriptType)
            return;

        ScriptType = scriptType;

        if (parserDocument is not null)
        {
            ParserDocument = parserDocument;
            _forceAsync = false;
        }

        AlreadyStarted = true;
        var document = NodeDocument;
        PreparationTimeDocument = document;
        if (parserDocument is not null && !ReferenceEquals(parserDocument, document))
            return;

        // https://html.spec.whatwg.org/multipage/webappapis.html#concept-n-noscript: scripting is disabled for the documents
        // that have no browsing context (e.g. the documents created by DOMParser or createHTMLDocument()), and for the
        // sandboxed documents without allow-scripts
        if (document.BrowsingContext is not { Options.ScriptingEnabled: true } browsingContext || !document.ScriptingEnabled)
            return;

        if (ScriptType == ScriptElementType.Classic && HasAttributeWithLocalName("nomodule"))
            return;

        // https://w3c.github.io/webappsec-csp/#should-block-inline
        if (src is null && ContentSecurityPolicy.ShouldBlockInline(this, ContentSecurityPolicyInlineType.Script, sourceText, IsParserInserted))
            return;

        if (ScriptType == ScriptElementType.Classic && GetAttributeValue("event") is { } eventValue && GetAttributeValue("for") is { } forValue)
        {
            if (!InfraAscii.StripLeadingAndTrailingAsciiWhitespace(forValue).Equals("window", StringComparison.OrdinalIgnoreCase))
                return;

            var @event = InfraAscii.StripLeadingAndTrailingAsciiWhitespace(eventValue);
            if (!@event.Equals("onload", StringComparison.OrdinalIgnoreCase) && !@event.Equals("onload()", StringComparison.OrdinalIgnoreCase))
                return;
        }

        string? url = null;
        if (src is not null)
        {
            // External import maps and speculation rules are not supported
            if (ScriptType is ScriptElementType.ImportMap or ScriptElementType.SpeculationRules || src.Length == 0)
            {
                QueueErrorEvent();
                return;
            }

            FromExternalFile = true;
            url = Reflection.EncodingParseAndSerializeUrl(document, src);
            if (url is null)
            {
                QueueErrorEvent();
                return;
            }
        }

        // https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element, step 32: the result of an inline
        // speculation rules script is ready immediately. No script engine is needed.
        if (ScriptType == ScriptElementType.SpeculationRules)
        {
            if (!browsingContext.Options.EffectiveFeatures.SpeculationRulesEnabled || document.RelevantWindow is null)
                return;

            MarkAsReady(CreateSpeculationRulesResult(sourceText, document));
            if (parserDocument is not null && parserDocument.HasStyleSheetThatIsBlockingScripts && parserDocument.ActiveParser is not Html.Parsing.HtmlScriptingParser { TreeBuilder.ScriptNestingLevel: > 1 })
            {
                parserDocument.PendingParsingBlockingScript = this;
                ReadyToBeParserExecuted = true;
                return;
            }

            Execute();
            return;
        }

        // The module scripts need an engine that supports them
        if (browsingContext.Options.ScriptEngine is not { } engine || document.RelevantWindow is not { } window)
            return;

        if (ScriptType == ScriptElementType.Module && !engine.SupportsModuleScripts)
            return;

        // https://html.spec.whatwg.org/multipage/webappapis.html#register-an-import-map: an inline import map is registered immediately
        if (ScriptType == ScriptElementType.ImportMap)
        {
            RegisterImportMap(window, sourceText);
            return;
        }

        if (url is null && ScriptType == ScriptElementType.Classic)
        {
            // An inline classic script is ready immediately. A script of the parser waits for the style sheets that are
            // blocking scripts (the style sheets of the link and style elements and of the xml-stylesheet processing
            // instructions created by the parser).
            MarkAsReady(new Scripting.ClassicScript(window, sourceText, document.BaseUri) { Url = document.Url, Element = this, SourceTextLine = SourceTextPosition.Line, SourceTextColumn = SourceTextPosition.Column });
            if (parserDocument is not null && parserDocument.HasStyleSheetThatIsBlockingScripts && parserDocument.ActiveParser is not Html.Parsing.HtmlScriptingParser { TreeBuilder.ScriptNestingLevel: > 1 })
            {
                parserDocument.PendingParsingBlockingScript = this;
                ReadyToBeParserExecuted = true;
                return;
            }

            Execute();
            return;
        }

        // https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element: "If el is potentially
        // render-blocking, then block rendering on el" (an external script, or an inline module script)
        if (IsPotentiallyRenderBlocking)
        {
            BlockRendering(document);
        }

        SetDelayingTheLoadEvent(true);
        var completedSynchronously = false;
        Scripting.ClassicScript? fetchedScript = null;
        if (ScriptType == ScriptElementType.Classic)
        {
            // A script that is not run by the parser is marked as ready by a task (see below)
            var markedAsReadyByTheParser = parserDocument is not null && !HasAttributeWithLocalName("async") && !_forceAsync && parserDocument.ActiveParser is not Html.Parsing.ScriptingParser { IsScriptCreated: true };
            completedSynchronously = Scripting.ScriptFetching.FetchClassicScript(this, browsingContext, window, url!, MarkAsReady, out fetchedScript, reportTimingFromTask: !markedAsReadyByTheParser);
        }

        bool executedByTheParser;
        if (HasAttributeWithLocalName("async") || _forceAsync)
        {
            // The set of scripts that will execute as soon as possible
            var scripts = document.ScriptsToExecuteAsSoonAsPossible;
            scripts.Add(this);
            _stepsToRunWhenTheResultIsReady = () =>
            {
                Execute();
                scripts.Remove(this);
                document.TryQueueLoadEvent();
            };
            executedByTheParser = false;
        }
        else if (parserDocument is null)
        {
            // The list of scripts that will execute in order as soon as possible
            var scripts = document.ScriptsToExecuteInOrderAsSoonAsPossible;
            scripts.Add(this);
            _stepsToRunWhenTheResultIsReady = () =>
            {
                if (!ReferenceEquals(scripts[0], this))
                    return;

                while (scripts.Count > 0 && scripts[0].HasResult)
                {
                    var script = scripts[0];
                    script.Execute();
                    scripts.RemoveAt(0);
                }

                document.TryQueueLoadEvent();
            };
            executedByTheParser = false;
        }
        else if (HasAttributeWithLocalName("defer") || ScriptType == ScriptElementType.Module)
        {
            // The list of scripts that will execute when the document has finished parsing (the module scripts are deferred)
            parserDocument.ScriptsToExecuteWhenParsingFinished.Add(this);
            _stepsToRunWhenTheResultIsReady = SetReadyToBeParserExecuted;
            executedByTheParser = true;
        }
        else
        {
            parserDocument.PendingParsingBlockingScript = this;
            BlockRendering(document);
            _stepsToRunWhenTheResultIsReady = SetReadyToBeParserExecuted;
            executedByTheParser = true;
        }

        // The module scripts are fetched once they are scheduled: the result is ready from a task or a microtask
        if (ScriptType == ScriptElementType.Module)
        {
            FetchModuleScriptGraph(engine, window, url, sourceText);
            return;
        }

        if (completedSynchronously)
        {
            // A fetch completes from a task. The scripts executed by the parser are marked as ready immediately, so that
            // the parser does not pause when the loader completes synchronously, except the ones of a script-created parser
            // (document.open()): the call to document.write() that inserted the script returns before the script runs.
            if (executedByTheParser && parserDocument?.ActiveParser is not Html.Parsing.ScriptingParser { IsScriptCreated: true })
            {
                MarkAsReady(fetchedScript);
            }
            else
            {
                browsingContext.EventLoop.QueueTask(() => MarkAsReady(fetchedScript));
            }
        }

        void QueueErrorEvent() => NodeDocument.QueueElementTask(() => FireEvent(new Event("error")));
    }

    /// <summary>
    /// Fetches the graph of a module script (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-script-tree
    /// and https://html.spec.whatwg.org/multipage/webappapis.html#fetch-an-inline-module-script-graph): the engine fetches
    /// the root of an external script (with its module map), and loads the descendants. The result is null when a module of
    /// the graph cannot be fetched.
    /// </summary>
    private void FetchModuleScriptGraph(Scripting.ScriptEngine engine, Window window, string? url, string sourceText)
    {
        var document = NodeDocument;
        var options = new Scripting.ModuleFetchOptions
        {
            IncludeCredentials = InfraAscii.EqualsIgnoreAsciiCase(GetAttributeValue("crossorigin") ?? "", "use-credentials"),
            ReferrerPolicy = ReferrerPolicy,
            Nonce = ContentSecurityPolicy.GetNonceMetadata(this),
            ParserInserted = IsParserInserted,
            RenderBlocking = NodeDocument.IsRenderBlocking(this),
        };

        try
        {
            // The script is marked as ready from a task (https://github.com/whatwg/html/issues/9864), as in the browsers:
            // the graph is loaded by promise jobs (or synchronously for an inline script, or a root already in the module
            // map), and the script runs (then the microtasks it queues, before its load event) outside of the microtask checkpoint
            if (url is null)
            {
                var script = new Scripting.ModuleScript(window, sourceText, document.DocumentBaseUrl.Serialize()) { Url = document.Url, IsInline = true, Element = this, FetchOptions = options, SourceTextLine = SourceTextPosition.Line, SourceTextColumn = SourceTextPosition.Column };
                engine.LoadModuleScriptGraph(script, success => window.QueueGlobalTask(() => MarkAsReady(success ? script : null)));
            }
            else
            {
                engine.FetchModuleScriptGraph(window, url, options, this, script => window.QueueGlobalTask(() => MarkAsReady(script)));
            }
        }
#pragma warning disable CA1031 // An exception thrown by the engine is a failure of the graph
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Scripting.ScriptErrors.Report(ex, window);
            MarkAsReady(null);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#register-an-import-map: the import map is parsed and merged in
    /// the import map of the window; its errors are reported.
    /// </summary>
    private void RegisterImportMap(Window window, string sourceText)
    {
        Scripting.ImportMap importMap;
        try
        {
            importMap = Scripting.ModuleSpecifierResolution.Parse(sourceText, NodeDocument.DocumentBaseUrl);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Scripting.ScriptErrors.Report(new JavaScriptSyntaxErrorException("The import map is not valid JSON: " + ex.Message), window);
            return;
        }
        catch (TypeErrorException ex)
        {
            Scripting.ScriptErrors.Report(ex, window);
            return;
        }

        Scripting.ModuleSpecifierResolution.Merge(window.ImportMap, importMap);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#execute-the-script-element
    /// </summary>
    internal void Execute()
    {
        var document = NodeDocument;
        if (!ReferenceEquals(PreparationTimeDocument, document))
            return;

        StopBlockingRendering();

        if (Result is SpeculationRulesParseResult speculationRules)
        {
            if (document.RelevantWindow is { } rulesWindow)
            {
                RegisterSpeculationRules(speculationRules, rulesWindow);
            }

            return;
        }

        if (Result is not { } result || document.BrowsingContext?.Options.ScriptEngine is not { } engine)
        {
            FireEvent(new Event("error"));
            return;
        }

        // A module script runs without currentScript, and cannot blow away the document with document.write() (the
        // ignore-destructive-writes counter)
        if (result is Scripting.ModuleScript moduleScript)
        {
            var previousCurrentScript = document.CurrentScript;
            document.CurrentScript = null;
            document.IgnoreDestructiveWritesCounter++;
            try
            {
                Scripting.ScriptExecution.RunModuleScript(engine, moduleScript);
            }
            finally
            {
                document.CurrentScript = previousCurrentScript;
                document.IgnoreDestructiveWritesCounter--;
            }

            if (FromExternalFile)
            {
                FireEvent(new Event("load"));
            }

            return;
        }

        var script = (Scripting.ClassicScript)result;

        // https://html.spec.whatwg.org/multipage/scripting.html#execute-the-script-element: an external script cannot blow
        // away the document with document.write() (the ignore-destructive-writes counter)
        if (FromExternalFile)
        {
            document.IgnoreDestructiveWritesCounter++;
        }

        var oldCurrentScript = document.CurrentScript;
        document.CurrentScript = GetRoot() is ShadowRoot ? null : this;
        try
        {
            Scripting.ScriptExecution.RunClassicScript(engine, script);
        }
        finally
        {
            document.CurrentScript = oldCurrentScript;
            if (FromExternalFile)
            {
                document.IgnoreDestructiveWritesCounter--;
            }
        }

        if (FromExternalFile)
        {
            FireEvent(new Event("load"));
        }
    }

    /// <summary>
    /// The type of the script given its type and language attributes (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element,
    /// steps 8 to 13), or null when the script block's type string is not supported.
    /// </summary>
    internal ScriptElementType? GetScriptTypeFromAttributes()
    {
        var type = GetAttributeValue("type");
        var language = GetAttributeValue("language");
        string typeString;
        if (type is { Length: 0 } || (type is null && language is null or { Length: 0 }))
        {
            typeString = "text/javascript";
        }
        else if (type is not null)
        {
            typeString = type;
        }
        else
        {
            typeString = "text/" + language;
        }

        // Only the JavaScript MIME type check strips the whitespace of the type attribute: " module " is not a module.
        if (IsJavaScriptMimeTypeEssenceMatch(typeString) || (type is not null && IsJavaScriptMimeTypeEssenceMatch(InfraAscii.StripLeadingAndTrailingAsciiWhitespace(type).ToString())))
            return ScriptElementType.Classic;

        if (typeString.Equals("module", StringComparison.OrdinalIgnoreCase))
            return ScriptElementType.Module;

        if (typeString.Equals("importmap", StringComparison.OrdinalIgnoreCase))
            return ScriptElementType.ImportMap;

        if (typeString.Equals("speculationrules", StringComparison.OrdinalIgnoreCase))
            return ScriptElementType.SpeculationRules;

        return null;
    }

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
