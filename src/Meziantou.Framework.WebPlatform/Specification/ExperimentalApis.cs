namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// The diagnostic identifiers of the <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> of the public APIs
/// that implement tentative features: proposals that no specification defines yet (pull requests, explainers, issues), whose
/// web-platform-tests are tentative, and features that the browsers implement but no specification defines. Each feature has
/// its own identifier, documented in docs/experimental-apis.md.
/// </summary>
/// <remarks>
/// The IDL that tools/Meziantou.Framework.WebPlatform.SpecGen/IdlModel.cs adds for the proposals names these constants (<c>ExperimentalApi</c>): SpecGen checks that the .NET types and members of this IDL have the matching attribute.
/// SpecGen also checks that the properties of <c>CssStyleProperties</c> whose CSS property is not defined by webref have the attribute.
/// </remarks>
internal static class ExperimentalApis
{
    /// <summary>The URL of the documentation of the diagnostics.</summary>
    public const string UrlFormat = "https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.Framework.WebPlatform/docs/experimental-apis.md#{0}";

    /// <summary>The declarative partial updates (https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md).</summary>
    public const string DeclarativePartialUpdates = "WEBPLATEXP0003";

    /// <summary>The @private rule of CSS Mixins (https://github.com/w3c/csswg-drafts/issues/14243).</summary>
    public const string CssPrivateRule = "WEBPLATEXP0004";

    /// <summary>The <c>change</c> events of <c>ContainerQueryList</c> (https://github.com/w3c/csswg-drafts/pull/13551).</summary>
    public const string ContainerQueryListChangeEvent = "WEBPLATEXP0005";

    /// <summary>The class prefix selector of Selectors 5.</summary>
    public const string ClassPrefixSelector = "WEBPLATEXP0006";

    /// <summary>The proposals of the File System Standard (https://github.com/whatwg/fs/pull/9, https://github.com/whatwg/fs/pull/10, https://github.com/whatwg/fs/blob/main/proposals/MultipleReadersWriters.md).</summary>
    public const string FileSystemProposals = "WEBPLATEXP0007";

    /// <summary>The <c>pseudoTarget</c> of the UI events (https://github.com/w3c/uievents/pull/413).</summary>
    public const string UIEventPseudoTarget = "WEBPLATEXP0008";

    /// <summary><c>ShadowRoot.getSelection()</c>, <c>elementFromPoint()</c> and <c>elementsFromPoint()</c> (https://github.com/w3c/csswg-drafts/issues/556).</summary>
    public const string ShadowRootExtensions = "WEBPLATEXP0009";

    /// <summary>The <c>oncontentvisibilityautostatechange</c> event handler, which no IDL defines.</summary>
    public const string ContentVisibilityAutoStateChangeEventHandler = "WEBPLATEXP0010";

    /// <summary>The error stack accessor (https://github.com/tc39/proposal-error-stack-accessor).</summary>
    public const string ErrorStackAccessor = "WEBPLATEXP0011";

    /// <summary>The <c>grid-lanes</c>, <c>grid-lanes-direction</c> and <c>grid-lanes-pack</c> CSS properties of the tentative web-platform-tests, which CSS Grid 3 leaves to a "TBD property" (https://github.com/w3c/csswg-drafts/issues/12803).</summary>
    public const string GridLanesOrientation = "WEBPLATEXP0012";

    /// <summary>The CSS properties that the browsers implement but no specification defines (e.g. <c>flow-tolerance</c>, the former name of <c>fit-tolerance</c>).</summary>
    public const string NonStandardCssProperties = "WEBPLATEXP0013";

    /// <summary>The source phase imports (https://github.com/tc39/proposal-source-phase-imports) and the module source of <c>WebAssembly.Module</c> (https://github.com/WebAssembly/esm-integration).</summary>
    public const string SourcePhaseImports = "WEBPLATEXP0014";

    /// <summary>The tentative <c>URLPattern.compareComponent()</c> and <c>URLPattern.prototype.generate()</c> methods.</summary>
    public const string UrlPatternTentativeMethods = "WEBPLATEXP0015";

    /// <summary>The <c>policy</c> attribute of the <c>iframe</c> elements (https://wicg.github.io/document-policy/#iframe-policy-attribute), which no IDL of webref defines.</summary>
    public const string DocumentPolicyIframeAttribute = "WEBPLATEXP0016";

    /// <summary>The canvas layers (https://github.com/whatwg/html/pull/9537): <c>beginLayer()</c> and <c>endLayer()</c> of the 2D contexts.</summary>
    public const string CanvasLayers = "WEBPLATEXP0017";

    /// <summary>The readiness of the WebMCP tools (https://github.com/webmachinelearning/webmcp/pull/373): <c>ModelContext.waitForToolsUntil()</c> and <c>ModelContext.toolsReady</c>.</summary>
    public const string WebMcpToolReadiness = "WEBPLATEXP0018";

    /// <summary>The interpolation methods of the canvas gradients (https://github.com/whatwg/html/pull/10986): <c>colorInterpolationMethod</c> and <c>hueInterpolationMethod</c>.</summary>
    public const string CanvasGradientInterpolation = "WEBPLATEXP0019";

    /// <summary>The <c>async</c> IDL attribute of the SVG <c>script</c> elements, which SVG 2 does not define (the web-platform-tests of <c>svg/interact/scripted</c> test it).</summary>
    public const string SvgScriptAsync = "WEBPLATEXP0020";

    /// <summary>The <c>CanvasFilter</c> objects (https://github.com/whatwg/html/issues/5621): the filters of the 2D contexts and of the canvas layers described by filter primitives.</summary>
    public const string CanvasFilterObjects = "WEBPLATEXP0021";

    /// <summary>The scroll performance timing (https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md): <c>PerformanceScrollTiming</c> and the <c>scroll</c> entry type.</summary>
    public const string ScrollTiming = "WEBPLATEXP0022";

    /// <summary>HTML-in-canvas (https://github.com/WICG/html-in-canvas): the <c>content</c> attribute and the paint events of the canvases, <c>drawElementImage()</c> and the element images and geometries.</summary>
    public const string HtmlInCanvas = "WEBPLATEXP0023";

    /// <summary>The text clusters and the selection APIs of <c>TextMetrics</c> (https://github.com/whatwg/html/pull/11000): <c>getTextClusters()</c>, <c>getSelectionRects()</c>, <c>getActualBoundingBox()</c>, <c>getIndexFromOffset()</c>, <c>fillTextCluster()</c> and <c>strokeTextCluster()</c>.</summary>
    public const string CanvasTextClusters = "WEBPLATEXP0024";

    /// <summary>The shaped text of the 2D contexts, which Chromium implements behind a flag: <c>shapeText()</c>, <c>ShapedText</c>, <c>GlyphRun</c>, <c>fillGlyphRun()</c> and <c>strokeGlyphRun()</c>.</summary>
    public const string CanvasShapedText = "WEBPLATEXP0025";

    /// <summary>The <c>initiatorUrl</c> attribute of <c>PerformanceResourceTiming</c> (https://github.com/w3c/resource-timing/issues/263), which the tentative web-platform-tests of <c>resource-timing/tentative/initiator-url</c> test.</summary>
    public const string ResourceTimingInitiatorUrl = "WEBPLATEXP0026";

    /// <summary><c>WebSocketStream</c> and <c>WebSocketError</c> (https://github.com/whatwg/websockets/pull/48): a WebSocket connection whose messages are read and written with streams.</summary>
    public const string WebSocketStream = "WEBPLATEXP0027";
}
