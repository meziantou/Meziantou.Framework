using Meziantou.Framework.WebPlatform.Wpt.Runner;

namespace Meziantou.Framework.WebPlatform.Wpt;

/// <summary>Runs the web-platform-tests of the DOM and compares the results with the expectations.</summary>
public sealed class WptTests
{
    public static TheoryData<string> DomTests => GetTests("dom");

    public static TheoryData<string> ShadowDomTests => GetTests("shadow-dom");

    public static TheoryData<string> CssSelectorsTests => GetTests("css/selectors");

    public static TheoryData<string> DomParsingTests => GetTests("domparsing");

    public static TheoryData<string> DomXPathTests => GetTests("domxpath");

    public static TheoryData<string> HtmlDomTests => GetTests("html/dom");

    public static TheoryData<string> SvgTypesTests => GetTests("svg/types");

    public static TheoryData<string> SvgPathTests => GetTests("svg/path");

    public static TheoryData<string> HtmlFormsTests => GetTests("html/semantics/forms");

    public static TheoryData<string> CssCssomTests => GetTests("css/cssom");

    public static TheoryData<string> CssSyntaxTests => GetTests("css/css-syntax");

    public static TheoryData<string> SanitizerApiTests => GetTests("sanitizer-api");

    public static TheoryData<string> TrustedTypesTests => GetTests("trusted-types");

    public static TheoryData<string> CssGeometryTests => GetTests("css/geometry");

    public static TheoryData<string> CssCascadeTests => GetTests("css/css-cascade", "css/css-cascade/parsing");

    public static TheoryData<string> CssContainerQueriesTests => GetTests("css/css-conditional/container-queries");

    public static TheoryData<string> HtmlInfrastructureTests => GetTests("html/infrastructure");

    public static TheoryData<string> HtmlPopoversTests => GetTests("html/semantics/popovers");

    public static TheoryData<string> HtmlCommandTests => GetTests("html/semantics/the-button-element/command-and-commandfor");

    public static TheoryData<string> CustomElementsTests => GetTests("custom-elements");

    public static TheoryData<string> SelectionTests => GetTests("selection");

    public static TheoryData<string> EditingTests => GetTests("editing");

    public static TheoryData<string> HtmlGroupingContentTests => GetTests("html/semantics/grouping-content");

    public static TheoryData<string> HtmlSemanticsSelectorsTests => GetTests("html/semantics/selectors");

    public static TheoryData<string> HtmlTextLevelSemanticsTests => GetTests("html/semantics/text-level-semantics");

    public static TheoryData<string> HtmlTabularDataTests => GetTests("html/semantics/tabular-data");

    public static TheoryData<string> HtmlEditsTests => GetTests("html/semantics/edits");

    public static TheoryData<string> HtmlSectionsTests => GetTests("html/semantics/sections");

    public static TheoryData<string> HtmlInteractiveElementsTests => GetTests("html/semantics/interactive-elements");

    public static TheoryData<string> HtmlMenuTests => GetTests("html/semantics/menu");

    public static TheoryData<string> HtmlDisabledElementsTests => GetTests("html/semantics/disabled-elements");

    public static TheoryData<string> HtmlLinksTests => GetTests("html/semantics/links");

    public static TheoryData<string> HtmlDocumentMetadataTests => GetTests("html/semantics/document-metadata");

    public static TheoryData<string> HtmlEmbeddedContentTests => GetTests("html/semantics/embedded-content");

    public static TheoryData<string> MathMLTests => GetTests("mathml");

    public static TheoryData<string> HtmlInterestForTests => GetTests("html/semantics/interestfor");

    public static TheoryData<string> HtmlScriptingTests => GetTests("html/semantics/scripting-1");

    // The files at the root of html/semantics (interfaces.html, rellist-feature-detection.html): its subdirectories are
    // run by their own groups, or not run
    public static TheoryData<string> HtmlSemanticsTests => GetTests("html/semantics", GetSubdirectories("html/semantics"));

    public static TheoryData<string> HtmlTimersTests => GetTests("html/webappapis/timers");

    public static TheoryData<string> HtmlSyntaxTests => GetTests("html/syntax");

    public static TheoryData<string> EncodingTests => GetTests("encoding");

    public static TheoryData<string> EncodingDetectionTests => GetTests("encoding-detection");

    public static TheoryData<string> UrlTests => GetTests("url");

    public static TheoryData<string> UrlPatternTests => GetTests("urlpattern");

    public static TheoryData<string> HtmlDynamicMarkupInsertionTests => GetTests("html/webappapis/dynamic-markup-insertion");

    public static TheoryData<string> WebIdlTests => GetTests("webidl/ecmascript-binding");

    public static TheoryData<string> FileApiTests => GetTests("FileAPI");

    public static TheoryData<string> BroadcastChannelTests => GetTests("webmessaging/broadcastchannel");

    public static TheoryData<string> MessageChannelTests => GetTests("webmessaging/message-channels");

    // The window postMessage() and MessagePort tests of webmessaging (the subdirectories of BroadcastChannel and MessageChannel have their own group)
    public static TheoryData<string> WebMessagingTests => GetTests("webmessaging", WebMessagingExcludedDirectories);

    public static TheoryData<string> WorkersTests => GetTests("workers");

    public static TheoryData<string> SecureContextsTests => GetTests("secure-contexts");

    public static TheoryData<string> HtmlWebAppApisTests => GetTests("html/webappapis", "html/webappapis/timers", "html/webappapis/dynamic-markup-insertion");

    public static TheoryData<string> UiEventsTests => GetTests("uievents");

    public static TheoryData<string> PointerEventsTests => GetTests("pointerevents");

    public static TheoryData<string> TouchEventsTests => GetTests("touch-events");

    public static TheoryData<string> HtmlUserActivationTests => GetTests("html/user-activation");

    public static TheoryData<string> CloseWatcherTests => GetTests("close-watcher");

    public static TheoryData<string> ScrollToTextFragmentTests => GetTests("scroll-to-text-fragment");

    public static TheoryData<string> CorsTests => GetTests("cors");

    public static TheoryData<string> FullscreenTests => GetTests("fullscreen");

    public static TheoryData<string> PointerLockTests => GetTests("pointerlock");

    public static TheoryData<string> SchedulerTests => GetTests("scheduler");

    public static TheoryData<string> WebLocksTests => GetTests("web-locks");

    public static TheoryData<string> StorageTests => GetTests("storage");

    public static TheoryData<string> FileSystemTests => GetTests("fs");

    public static TheoryData<string> FocusTests => GetTests("focus");

    public static TheoryData<string> InertTests => GetTests("inert");

    public static TheoryData<string> InputEventsTests => GetTests("input-events");

    public static TheoryData<string> DensitySizeCorrectionTests => GetTests("density-size-correction");

    public static TheoryData<string> PageVisibilityTests => GetTests("page-visibility");

    public static TheoryData<string> HrTimeTests => GetTests("hr-time");

    public static TheoryData<string> HtmlHistoryTests => GetTests("html/browsers/history");

    public static TheoryData<string> HtmlWindowsTests => GetTests("html/browsers/windows");

    public static TheoryData<string> HtmlOriginTests => GetTests("html/browsers/origin");

    public static TheoryData<string> HtmlBrowsingTheWebTests => GetTests("html/browsers/browsing-the-web");

    public static TheoryData<string> HtmlTheWindowObjectTests => GetTests("html/browsers/the-window-object");

    public static TheoryData<string> HtmlTheWindowProxyExoticObjectTests => GetTests("html/browsers/the-windowproxy-exotic-object");

    public static TheoryData<string> HtmlSandboxingTests => GetTests("html/browsers/sandboxing");

    public static TheoryData<string> HtmlCrossOriginOpenerPolicyTests => GetTests("html/cross-origin-opener-policy");

    public static TheoryData<string> HtmlCrossOriginEmbedderPolicyTests => GetTests("html/cross-origin-embedder-policy");

    public static TheoryData<string> HtmlDocumentIsolationPolicyTests => GetTests("html/document-isolation-policy");

    public static TheoryData<string> HtmlAnonymousIframeTests => GetTests("html/anonymous-iframe");

    public static TheoryData<string> HtmlLinkTypesTests => GetTests("html/links");

    public static TheoryData<string> CssNestingTests => GetTests("css/css-nesting");

    public static TheoryData<string> CssConditionalTests => GetTests("css/css-conditional", "css/css-conditional/container-queries", "css/css-conditional/parsing");

    public static TheoryData<string> CssVariablesTests => GetTests("css/css-variables");

    public static TheoryData<string> CssPropertiesValuesApiTests => GetTests("css/css-properties-values-api");

    public static TheoryData<string> CssFontLoadingTests => GetTests("css/css-font-loading");

    public static TheoryData<string> HtmlInteractionTests => GetTests("html/interaction", "html/interaction/focus/focusgroup");

    public static TheoryData<string> StreamsTests => GetTests("streams");

    public static TheoryData<string> AccessibilityTests => GetTests("accessibility");

    public static TheoryData<string> AccNameTests => GetTests("accname");

    public static TheoryData<string> HtmlAamTests => GetTests("html-aam");

    public static TheoryData<string> SvgAamTests => GetTests("svg-aam");

    public static TheoryData<string> WaiAriaTests => GetTests("wai-aria");

    public static TheoryData<string> HtmlEditingTests => GetTests("html/editing");

    public static TheoryData<string> BeaconTests => GetTests("beacon");

    public static TheoryData<string> ClipboardApisTests => GetTests("clipboard-apis");

    public static TheoryData<string> ClientHintsTests => GetTests("client-hints");

    public static TheoryData<string> UAClientHintsTests => GetTests("ua-client-hints");

    public static TheoryData<string> CookiesTests => GetTests("cookies");

    public static TheoryData<string> CookieStoreTests => GetTests("cookiestore");

    public static TheoryData<string> WebSocketsTests => GetTests("websockets");

    public static TheoryData<string> EventSourceTests => GetTests("eventsource");

    public static TheoryData<string> PreloadTests => GetTests("preload");

    public static TheoryData<string> PermissionsPolicyTests => GetTests("permissions-policy");

    public static TheoryData<string> ResizeObserverTests => GetTests("resize-observer");

    public static TheoryData<string> IntersectionObserverTests => GetTests("intersection-observer");

    public static TheoryData<string> LoadingTests => GetTests("loading");

    public static TheoryData<string> LayoutInstabilityTests => GetTests("layout-instability");

    public static TheoryData<string> ApngTests => GetTests("apng");

    public static TheoryData<string> PngTests => GetTests("png");

    public static TheoryData<string> ImageBitmapRenderingContextTests => GetTests("imagebitmap-renderingcontext");

    public static TheoryData<string> HtmlCanvasTests => GetTests("html/canvas");

    public static TheoryData<string> PaintTimingTests => GetTests("paint-timing");

    public static TheoryData<string> LargestContentfulPaintTests => GetTests("largest-contentful-paint");

    public static TheoryData<string> ElementTimingTests => GetTests("element-timing");

    public static TheoryData<string> EventTimingTests => GetTests("event-timing");

    public static TheoryData<string> ForcedColorsModeTests => GetTests("forced-colors-mode");

    public static TheoryData<string> ResourceTimingTests => GetTests("resource-timing");

    public static TheoryData<string> PerformanceTimelineTests => GetTests("performance-timeline");

    public static TheoryData<string> UserTimingTests => GetTests("user-timing");

    public static TheoryData<string> NavigationTimingTests => GetTests("navigation-timing");

    public static TheoryData<string> ImportMapsTests => GetTests("import-maps");

    public static TheoryData<string> VisualViewportTests => GetTests("visual-viewport");

    public static TheoryData<string> ViewportTests => GetTests("viewport");

    public static TheoryData<string> ScrollAnimationsTests => GetTests("scroll-animations");

    public static TheoryData<string> WebStorageTests => GetTests("webstorage");
    public static TheoryData<string> IndexedDBTests => GetTests("IndexedDB");
    public static TheoryData<string> CacheStorageTests => GetTests("service-workers/cache-storage");
    public static TheoryData<string> ServiceWorkerTests => GetTests("service-workers", "service-workers/cache-storage");
    public static TheoryData<string> CompressionTests => GetTests("compression");

    public static TheoryData<string> WebCryptoApiTests => GetTests("WebCryptoAPI");

    public static TheoryData<string> WasmTests => GetTests("wasm");

    public static TheoryData<string> WebVttTests => GetTests("webvtt");

    public static TheoryData<string> MediaSourceTests => GetTests("media-source");

    public static TheoryData<string> MediaPlaybackQualityTests => GetTests("media-playback-quality");

    public static TheoryData<string> ConsoleTests => GetTests("console");

    public static TheoryData<string> CompatTests => GetTests("compat");

    public static TheoryData<string> QuirksTests => GetTests("quirks");

    public static TheoryData<string> HtmlXhtmlSyntaxTests => GetTests("html/the-xhtml-syntax");

    public static TheoryData<string> HtmlMetaTests => GetTests("html/meta");

    public static TheoryData<string> HtmlSelectTests => GetTests("html/select");

    public static TheoryData<string> HtmlObsoleteTests => GetTests("html/obsolete");

    public static TheoryData<string> HtmlScriptElementTests => GetTests("html/scripting");

    public static TheoryData<string> HtmlOfflineTests => GetTests("html/browsers/offline");

    public static TheoryData<string> HtmlRenderingTests => GetTests("html/rendering");

    public static TheoryData<string> CssCssomViewTests => GetTests("css/cssom-view", "css/cssom-view/parsing");

    public static TheoryData<string> XhrTests => GetTests("xhr");

    public static TheoryData<string> FetchApiTests => GetTests("fetch/api");

    public static TheoryData<string> FetchDataUrlsTests => GetTests("fetch/data-urls");

    public static TheoryData<string> FetchTests => GetTests("fetch", "fetch/api", "fetch/data-urls");

    public static TheoryData<string> NavigationApiTests => GetTests("navigation-api");

    public static TheoryData<string> ReferrerPolicyTests => GetTests("referrer-policy");

    public static TheoryData<string> MixedContentTests => GetTests("mixed-content");

    public static TheoryData<string> XFrameOptionsTests => GetTests("x-frame-options");

    public static TheoryData<string> ReportingTests => GetTests("reporting");

    public static TheoryData<string> DeprecationReportingTests => GetTests("deprecation-reporting");

    public static TheoryData<string> InterventionReportingTests => GetTests("intervention-reporting");

    public static TheoryData<string> ContentSecurityPolicyBaseUriTests => GetTests("content-security-policy/base-uri");

    public static TheoryData<string> ContentSecurityPolicyBlobTests => GetTests("content-security-policy/blob");

    public static TheoryData<string> ContentSecurityPolicyChildSrcTests => GetTests("content-security-policy/child-src");

    public static TheoryData<string> ContentSecurityPolicyConnectSrcTests => GetTests("content-security-policy/connect-src");

    public static TheoryData<string> ContentSecurityPolicyDefaultSrcTests => GetTests("content-security-policy/default-src");

    public static TheoryData<string> ContentSecurityPolicyEmbeddedEnforcementTests => GetTests("content-security-policy/embedded-enforcement");

    public static TheoryData<string> ContentSecurityPolicyFontSrcTests => GetTests("content-security-policy/font-src");

    public static TheoryData<string> ContentSecurityPolicyFormActionTests => GetTests("content-security-policy/form-action");

    public static TheoryData<string> ContentSecurityPolicyFrameAncestorsTests => GetTests("content-security-policy/frame-ancestors");

    public static TheoryData<string> ContentSecurityPolicyFrameSrcTests => GetTests("content-security-policy/frame-src");

    public static TheoryData<string> ContentSecurityPolicyGenTests => GetTests("content-security-policy/gen");

    public static TheoryData<string> ContentSecurityPolicyGenericTests => GetTests("content-security-policy/generic");

    public static TheoryData<string> ContentSecurityPolicyImgSrcTests => GetTests("content-security-policy/img-src");

    public static TheoryData<string> ContentSecurityPolicyInheritanceTests => GetTests("content-security-policy/inheritance");

    public static TheoryData<string> ContentSecurityPolicyInsideWorkerTests => GetTests("content-security-policy/inside-worker");

    public static TheoryData<string> ContentSecurityPolicyMediaSrcTests => GetTests("content-security-policy/media-src");

    public static TheoryData<string> ContentSecurityPolicyMetaTests => GetTests("content-security-policy/meta");

    public static TheoryData<string> ContentSecurityPolicyNavigationTests => GetTests("content-security-policy/navigation");

    public static TheoryData<string> ContentSecurityPolicyNonceHidingTests => GetTests("content-security-policy/nonce-hiding");

    public static TheoryData<string> ContentSecurityPolicyObjectSrcTests => GetTests("content-security-policy/object-src");

    public static TheoryData<string> ContentSecurityPolicyParsingTests => GetTests("content-security-policy/parsing");

    public static TheoryData<string> ContentSecurityPolicyPluginTypesTests => GetTests("content-security-policy/plugin-types");

    public static TheoryData<string> ContentSecurityPolicyReportHashTests => GetTests("content-security-policy/report-hash");

    public static TheoryData<string> ContentSecurityPolicyReportingTests => GetTests("content-security-policy/reporting");

    public static TheoryData<string> ContentSecurityPolicyReportingApiTests => GetTests("content-security-policy/reporting-api");

    public static TheoryData<string> ContentSecurityPolicyResourceHintsTests => GetTests("content-security-policy/resource-hints");

    public static TheoryData<string> ContentSecurityPolicySandboxTests => GetTests("content-security-policy/sandbox");

    public static TheoryData<string> ContentSecurityPolicyScriptSrcTests => GetTests("content-security-policy/script-src");

    public static TheoryData<string> ContentSecurityPolicyScriptSrcAttrElemTests => GetTests("content-security-policy/script-src-attr-elem");

    public static TheoryData<string> ContentSecurityPolicySecuritypolicyviolationTests => GetTests("content-security-policy/securitypolicyviolation");

    public static TheoryData<string> ContentSecurityPolicyStyleSrcTests => GetTests("content-security-policy/style-src");

    public static TheoryData<string> ContentSecurityPolicyStyleSrcAttrElemTests => GetTests("content-security-policy/style-src-attr-elem");

    public static TheoryData<string> ContentSecurityPolicySvgTests => GetTests("content-security-policy/svg");

    public static TheoryData<string> ContentSecurityPolicyUnsafeEvalTests => GetTests("content-security-policy/unsafe-eval");

    public static TheoryData<string> ContentSecurityPolicyUnsafeHashesTests => GetTests("content-security-policy/unsafe-hashes");

    public static TheoryData<string> ContentSecurityPolicyWasmUnsafeEvalTests => GetTests("content-security-policy/wasm-unsafe-eval");

    public static TheoryData<string> ContentSecurityPolicyWebrtcTests => GetTests("content-security-policy/webrtc");

    public static TheoryData<string> ContentSecurityPolicyWorkerSrcTests => GetTests("content-security-policy/worker-src");

    public static TheoryData<string> ContentSecurityPolicyXsltTests => GetTests("content-security-policy/xslt");

    public static TheoryData<string> UpgradeInsecureRequestsTests => GetTests("upgrade-insecure-requests");

    public static TheoryData<string> CssColorTests => GetTests("css/css-color", "css/css-color/parsing");

    public static TheoryData<string> CssHighlightApiTests => GetTests("css/css-highlight-api");

    public static TheoryData<string> CssPseudoHighlightCascadeTests => GetTests("css/css-pseudo/highlight-cascade");

    public static TheoryData<string> CssMixinsTests => GetTests("css/css-mixins");

    public static TheoryData<string> CssShadowTests => GetTests("css/css-shadow");

    public static TheoryData<string> CssMediaQueriesTests => GetTests("css/mediaqueries");

    public static TheoryData<string> CssEnvTests => GetTests("css/css-env");

    public static TheoryData<string> CssViewportTests => GetTests("css/css-viewport");

    public static TheoryData<string> CssTypedOmTests => GetTests("css/css-typed-om");

    public static TheoryData<string> CssParserApiTests => GetTests("css/css-parser-api");

    public static TheoryData<string> CssValuesTests => GetTests("css/css-values");

    public static TheoryData<string> CssEasingTests => GetTests("css/css-easing");

    // The tests of the CSS directories outside of their parsing directory (CssParsing)
    public static TheoryData<string> CssUiTests => GetTests("css/css-ui", "css/css-ui/parsing");

    public static TheoryData<string> CssPseudoTests => GetTests("css/css-pseudo", "css/css-pseudo/parsing", "css/css-pseudo/highlight-cascade");

    public static TheoryData<string> CssFontsTests => GetTests("css/css-fonts", "css/css-fonts/parsing");

    public static TheoryData<string> CssBackgroundsTests => GetTests("css/css-backgrounds", "css/css-backgrounds/parsing");

    public static TheoryData<string> CssLogicalTests => GetTests("css/css-logical", "css/css-logical/parsing");

    public static TheoryData<string> CssDisplayTests => GetTests("css/css-display", "css/css-display/parsing");

    public static TheoryData<string> CssListsTests => GetTests("css/css-lists", "css/css-lists/parsing");

    public static TheoryData<string> CssTextDecorTests => GetTests("css/css-text-decor", "css/css-text-decor/parsing");

    public static TheoryData<string> CssCounterStylesTests => GetTests("css/css-counter-styles");

    public static TheoryData<string> CssBordersTests => GetTests("css/css-borders");

    public static TheoryData<string> CssColorHdrTests => GetTests("css/css-color-hdr");

    public static TheoryData<string> CssScrollbarsTests => GetTests("css/css-scrollbars");

    public static TheoryData<string> CssScrollSnapTests => GetTests("css/css-scroll-snap", "css/css-scroll-snap/parsing");

    public static TheoryData<string> CssScrollAnchoringTests => GetTests("css/css-scroll-anchoring", "css/css-scroll-anchoring/parsing");

    public static TheoryData<string> CssAnchorPositionTests => GetTests("css/css-anchor-position", "css/css-anchor-position/parsing");

    public static TheoryData<string> CssWritingModesTests => GetTests("css/css-writing-modes", "css/css-writing-modes/parsing");

    public static TheoryData<string> CssTextTests => GetTests("css/css-text", "css/css-text/parsing");

    public static TheoryData<string> CssPageTests => GetTests("css/css-page", "css/css-page/parsing");

    public static TheoryData<string> CssBreakTests => GetTests("css/css-break", "css/css-break/parsing");

    public static TheoryData<string> CssRubyTests => GetTests("css/css-ruby", "css/css-ruby/parsing");

    public static TheoryData<string> CssCompositingTests => GetTests("css/compositing", "css/compositing/parsing");

    public static TheoryData<string> CssFillStrokeTests => GetTests("css/fill-stroke");

    public static TheoryData<string> CssFetchingTests => GetTests("css/fetching");

    // The tests of #56 (animations and transitions): the directories of the CSS animations and transitions, of Web Animations, and
    // the animation tests of the other directories (only these files are fetched, tests/Meziantou.Framework.WebPlatform.SpecData/sources.json)
    public static TheoryData<string> CssAnimationsTests => GetTests("css/css-animations", "css/css-animations/parsing");

    public static TheoryData<string> CssTransitionsTests => GetTests("css/css-transitions", "css/css-transitions/parsing");

    public static TheoryData<string> CssViewTransitionsTests => GetTests("css/css-view-transitions", "css/css-view-transitions/parsing");

    public static TheoryData<string> WebAnimationsTests => GetTests("web-animations");

    public static TheoryData<string> CssCss2Tests => GetTests("css/CSS2");

    public static TheoryData<string> CssAlignTests => GetTests("css/css-align", "css/css-align/parsing");

    public static TheoryData<string> CssBoxTests => GetTests("css/css-box", "css/css-box/parsing");

    public static TheoryData<string> CssColorAdjustTests => GetTests("css/css-color-adjust", "css/css-color-adjust/parsing");

    public static TheoryData<string> CssContainTests => GetTests("css/css-contain", "css/css-contain/parsing");

    public static TheoryData<string> CssContentTests => GetTests("css/css-content", "css/css-content/parsing");

    public static TheoryData<string> CssFlexboxTests => GetTests("css/css-flexbox", "css/css-flexbox/parsing");

    public static TheoryData<string> CssGapsTests => GetTests("css/css-gaps", "css/css-gaps/parsing");

    public static TheoryData<string> CssGridTests => GetTests("css/css-grid", "css/css-grid/parsing");

    public static TheoryData<string> CssPaintApiTests => GetTests("css/css-paint-api", "css/css-paint-api/parsing");

    public static TheoryData<string> WorkletsTests => GetTests("worklets");

    public static TheoryData<string> CssLayoutApiTests => GetTests("css/css-layout-api");

    public static TheoryData<string> AnimationWorkletTests => GetTests("animation-worklet");

    public static TheoryData<string> SpeculationRulesTests => GetTests("speculation-rules");

    public static TheoryData<string> CssImagesTests => GetTests("css/css-images", "css/css-images/parsing");

    public static TheoryData<string> CssInlineTests => GetTests("css/css-inline", "css/css-inline/parsing");

    public static TheoryData<string> CssLinkParamsTests => GetTests("css/css-link-params", "css/css-link-params/parsing");

    public static TheoryData<string> CssMaskingTests => GetTests("css/css-masking", "css/css-masking/parsing");

    public static TheoryData<string> CssMulticolTests => GetTests("css/css-multicol", "css/css-multicol/parsing");

    public static TheoryData<string> CssOverflowTests => GetTests("css/css-overflow", "css/css-overflow/parsing");

    public static TheoryData<string> CssPositionTests => GetTests("css/css-position", "css/css-position/parsing");

    public static TheoryData<string> CssShapesTests => GetTests("css/css-shapes", "css/css-shapes/parsing");

    public static TheoryData<string> CssSizeAdjustTests => GetTests("css/css-size-adjust", "css/css-size-adjust/parsing");

    public static TheoryData<string> CssSizingTests => GetTests("css/css-sizing", "css/css-sizing/parsing");

    public static TheoryData<string> CssTablesTests => GetTests("css/css-tables", "css/css-tables/parsing");

    public static TheoryData<string> CssTransformsTests => GetTests("css/css-transforms", "css/css-transforms/parsing");

    public static TheoryData<string> CssFilterEffectsTests => GetTests("css/filter-effects", "css/filter-effects/parsing");

    public static TheoryData<string> CssMotionTests => GetTests("css/motion", "css/motion/parsing");

    public static TheoryData<string> CssWillChangeTests => GetTests("css/css-will-change", "css/css-will-change/parsing");

    public static TheoryData<string> CssRhythmTests => GetTests("css/css-rhythm", "css/css-rhythm/parsing");

    public static TheoryData<string> CssFormsTests => GetTests("css/css-forms", "css/css-forms/parsing");

    public static TheoryData<string> CssOverscrollBehaviorTests => GetTests("css/css-overscroll-behavior", "css/css-overscroll-behavior/parsing");

    public static TheoryData<string> CssForcedColorAdjustTests => GetTests("css/css-forced-color-adjust", "css/css-forced-color-adjust/parsing");

    public static TheoryData<string> SvgGeometryTests => GetTests("svg/geometry");

    public static TheoryData<string> SvgPaintingTests => GetTests("svg/painting");

    /// <summary>The other tests of svg (text, shapes, coordinate systems, structure, interactivity...), except the tests of other groups.</summary>
    public static TheoryData<string> SvgTests => GetTests("svg", SvgExcludedDirectories);

    private static readonly string[] SvgExcludedDirectories = ["svg/types", "svg/path", "svg/geometry", "svg/painting"];


    /// <summary>The parsing and serialization of the values of the CSS properties (css/*/parsing), except the tests of other groups.</summary>
    public static TheoryData<string> CssParsingTests => GetTests(CssParsingDirectory, CssParsingExcludedDirectories);

    private const string CssParsingDirectory = "css/*/parsing";

    private static readonly string[] CssParsingExcludedDirectories = ["css/selectors/parsing"];

    private static readonly string[] WebMessagingExcludedDirectories = ["webmessaging/broadcastchannel", "webmessaging/message-channels"];

    /// <summary>Returns the subdirectories of a directory of the test suite, to run only the files at its root.</summary>
    private static string[] GetSubdirectories(string directory)
    {
        var path = Path.Combine(WptPaths.Wpt, directory);
        return Directory.Exists(path) ? [.. Directory.EnumerateDirectories(path).Select(d => directory + "/" + Path.GetFileName(d)).Order(StringComparer.Ordinal)] : [];
    }

    private static TheoryData<string> GetTests(string directory, params string[] excludedDirectories)
    {
        var data = new TheoryData<string>();
        // WPT_FILTER restricts the tests to the ones whose id contains the value, or one of the values separated by '|' (useful
        // while debugging, or to update the expectations of some tests)
        var filters = Environment.GetEnvironmentVariable("WPT_FILTER") is { Length: > 0 } filterValue ? filterValue.Split('|', StringSplitOptions.RemoveEmptyEntries) : null;
        foreach (var test in Discover(directory, excludedDirectories).Tests)
        {
            if (filters is not null && !Array.Exists(filters, filter => test.Id.Contains(filter, StringComparison.Ordinal)))
                continue;

            data.Add(test.Id);
        }

        return data;
    }

    // The tests of a directory are discovered once, when the test cases are enumerated: reading the files of a large directory
    // for each test is quadratic, and the test cases of all the groups are enumerated whichever tests run
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<DiscoveredDirectory>> DiscoveredTests = new(StringComparer.Ordinal);

    private sealed class DiscoveredDirectory(List<WptTest> tests)
    {
        public List<WptTest> Tests { get; } = tests;

        public Dictionary<string, WptTest> ById { get; } = tests.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }

    private static DiscoveredDirectory Discover(string directory, string[] excludedDirectories)
        => DiscoveredTests.GetOrAdd(directory + "|" + string.Join("|", excludedDirectories), _ => new(() => new DiscoveredDirectory([.. WptTestDiscovery.Discover(directory, excludedDirectories)]))).Value;

    private static void Run(string directory, string id, params string[] excludedDirectories)
    {
        var test = Discover(directory, excludedDirectories).ById[id];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = WptTestRunner.Run(test);

        // The expectations are the results of the default engine (Meziantou.Framework.JavaScriptEngine)
        if (WptEngine.IsDefault)
        {
            // A test whose result is not the expected one runs again once, as with the --retry-unexpected option of wptrunner:
            // the tests run in parallel, and on a loaded machine (e.g. the CI agents) a test can exceed its timeout or observe
            // another order of its asynchronous steps. A regression still fails.
            if (!WptExpectations.UpdateMode && WptExpectations.GetDifferences(test, result) is not null)
            {
                stopwatch.Restart();
                result = WptTestRunner.Run(test);
            }

            WptReport.Append(test, result, stopwatch.Elapsed);
            WptExpectations.Check(test, result);
        }
        else
        {
            WptReport.Append(test, result, stopwatch.Elapsed);
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DomTests))]
    public void Dom(string test) => Run("dom", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ShadowDomTests))]
    public void ShadowDom(string test) => Run("shadow-dom", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssSelectorsTests))]
    public void CssSelectors(string test) => Run("css/selectors", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DomParsingTests))]
    public void DomParsing(string test) => Run("domparsing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DomXPathTests))]
    public void DomXPath(string test) => Run("domxpath", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlDomTests))]
    public void HtmlDom(string test) => Run("html/dom", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgTypesTests))]
    public void SvgTypes(string test) => Run("svg/types", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgPathTests))]
    public void SvgPath(string test) => Run("svg/path", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlFormsTests))]
    public void HtmlForms(string test) => Run("html/semantics/forms", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssCssomTests))]
    public void CssCssom(string test) => Run("css/cssom", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssSyntaxTests))]
    public void CssSyntax(string test) => Run("css/css-syntax", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SanitizerApiTests))]
    public void SanitizerApi(string test) => Run("sanitizer-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(TrustedTypesTests))]
    public void TrustedTypes(string test) => Run("trusted-types", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssGeometryTests))]
    public void CssGeometry(string test) => Run("css/geometry", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssCascadeTests))]
    public void CssCascade(string test) => Run("css/css-cascade", test, "css/css-cascade/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssContainerQueriesTests))]
    public void CssContainerQueries(string test) => Run("css/css-conditional/container-queries", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlInfrastructureTests))]
    public void HtmlInfrastructure(string test) => Run("html/infrastructure", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlPopoversTests))]
    public void HtmlPopovers(string test) => Run("html/semantics/popovers", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlCommandTests))]
    public void HtmlCommands(string test) => Run("html/semantics/the-button-element/command-and-commandfor", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CustomElementsTests))]
    public void CustomElements(string test) => Run("custom-elements", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SelectionTests))]
    public void Selection(string test) => Run("selection", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(EditingTests))]
    public void Editing(string test) => Run("editing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlGroupingContentTests))]
    public void HtmlGroupingContent(string test) => Run("html/semantics/grouping-content", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlSemanticsSelectorsTests))]
    public void HtmlSemanticsSelectors(string test) => Run("html/semantics/selectors", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTextLevelSemanticsTests))]
    public void HtmlTextLevelSemantics(string test) => Run("html/semantics/text-level-semantics", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTabularDataTests))]
    public void HtmlTabularData(string test) => Run("html/semantics/tabular-data", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlEditsTests))]
    public void HtmlEdits(string test) => Run("html/semantics/edits", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlSectionsTests))]
    public void HtmlSections(string test) => Run("html/semantics/sections", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlInteractiveElementsTests))]
    public void HtmlInteractiveElements(string test) => Run("html/semantics/interactive-elements", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlMenuTests))]
    public void HtmlMenu(string test) => Run("html/semantics/menu", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlDisabledElementsTests))]
    public void HtmlDisabledElements(string test) => Run("html/semantics/disabled-elements", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlLinksTests))]
    public void HtmlLinks(string test) => Run("html/semantics/links", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlDocumentMetadataTests))]
    public void HtmlDocumentMetadata(string test) => Run("html/semantics/document-metadata", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlEmbeddedContentTests))]
    public void HtmlEmbeddedContent(string test) => Run("html/semantics/embedded-content", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(MathMLTests))]
    public void MathML(string test) => Run("mathml", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlInterestForTests))]
    public void HtmlInterestFor(string test) => Run("html/semantics/interestfor", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlScriptingTests))]
    public void HtmlScripting(string test) => Run("html/semantics/scripting-1", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlSemanticsTests))]
    public void HtmlSemantics(string test) => Run("html/semantics", test, GetSubdirectories("html/semantics"));

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTimersTests))]
    public void HtmlTimers(string test) => Run("html/webappapis/timers", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlDynamicMarkupInsertionTests))]
    public void HtmlDynamicMarkupInsertion(string test) => Run("html/webappapis/dynamic-markup-insertion", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlSyntaxTests))]
    public void HtmlSyntax(string test) => Run("html/syntax", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(EncodingTests))]
    public void Encoding(string test) => Run("encoding", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(EncodingDetectionTests))]
    public void EncodingDetection(string test) => Run("encoding-detection", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UrlTests))]
    public void Url(string test) => Run("url", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UrlPatternTests))]
    public void UrlPattern(string test) => Run("urlpattern", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebIdlTests))]
    public void WebIdl(string test) => Run("webidl/ecmascript-binding", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FileApiTests))]
    public void FileApi(string test) => Run("FileAPI", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BroadcastChannelTests))]
    public void BroadcastChannel(string test) => Run("webmessaging/broadcastchannel", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(MessageChannelTests))]
    public void MessageChannel(string test) => Run("webmessaging/message-channels", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebMessagingTests))]
    public void WebMessaging(string test) => Run("webmessaging", test, WebMessagingExcludedDirectories);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WorkersTests))]
    public void Workers(string test) => Run("workers", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SecureContextsTests))]
    public void SecureContexts(string test) => Run("secure-contexts", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlWebAppApisTests))]
    public void HtmlWebAppApis(string test) => Run("html/webappapis", test, "html/webappapis/timers", "html/webappapis/dynamic-markup-insertion");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UiEventsTests))]
    public void UiEvents(string test) => Run("uievents", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PointerEventsTests))]
    public void PointerEvents(string test) => Run("pointerevents", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(TouchEventsTests))]
    public void TouchEvents(string test) => Run("touch-events", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlUserActivationTests))]
    public void HtmlUserActivation(string test) => Run("html/user-activation", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CloseWatcherTests))]
    public void CloseWatcher(string test) => Run("close-watcher", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ScrollToTextFragmentTests))]
    public void ScrollToTextFragment(string test) => Run("scroll-to-text-fragment", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CorsTests))]
    public void Cors(string test) => Run("cors", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FullscreenTests))]
    public void Fullscreen(string test) => Run("fullscreen", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PointerLockTests))]
    public void PointerLock(string test) => Run("pointerlock", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SchedulerTests))]
    public void Scheduler(string test) => Run("scheduler", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebLocksTests))]
    public void WebLocks(string test) => Run("web-locks", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(StorageTests))]
    public void Storage(string test) => Run("storage", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FileSystemTests))]
    public void FileSystem(string test) => Run("fs", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FocusTests))]
    public void Focus(string test) => Run("focus", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(InertTests))]
    public void Inert(string test) => Run("inert", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(InputEventsTests))]
    public void InputEvents(string test) => Run("input-events", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DensitySizeCorrectionTests))]
    public void DensitySizeCorrection(string test) => Run("density-size-correction", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PageVisibilityTests))]
    public void PageVisibility(string test) => Run("page-visibility", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HrTimeTests))]
    public void HrTime(string test) => Run("hr-time", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlHistoryTests))]
    public void HtmlHistory(string test) => Run("html/browsers/history", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlWindowsTests))]
    public void HtmlWindows(string test) => Run("html/browsers/windows", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlOriginTests))]
    public void HtmlOrigin(string test) => Run("html/browsers/origin", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlBrowsingTheWebTests))]
    public void HtmlBrowsingTheWeb(string test) => Run("html/browsers/browsing-the-web", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTheWindowObjectTests))]
    public void HtmlTheWindowObject(string test) => Run("html/browsers/the-window-object", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTheWindowProxyExoticObjectTests))]
    public void HtmlTheWindowProxyExoticObject(string test) => Run("html/browsers/the-windowproxy-exotic-object", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlSandboxingTests))]
    public void HtmlSandboxing(string test) => Run("html/browsers/sandboxing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlCrossOriginOpenerPolicyTests))]
    public void HtmlCrossOriginOpenerPolicy(string test) => Run("html/cross-origin-opener-policy", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlCrossOriginEmbedderPolicyTests))]
    public void HtmlCrossOriginEmbedderPolicy(string test) => Run("html/cross-origin-embedder-policy", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlDocumentIsolationPolicyTests))]
    public void HtmlDocumentIsolationPolicy(string test) => Run("html/document-isolation-policy", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlAnonymousIframeTests))]
    public void HtmlAnonymousIframe(string test) => Run("html/anonymous-iframe", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlLinkTypesTests))]
    public void HtmlLinkTypes(string test) => Run("html/links", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssNestingTests))]
    public void CssNesting(string test) => Run("css/css-nesting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssConditionalTests))]
    public void CssConditional(string test) => Run("css/css-conditional", test, "css/css-conditional/container-queries", "css/css-conditional/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssVariablesTests))]
    public void CssVariables(string test) => Run("css/css-variables", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssPropertiesValuesApiTests))]
    public void CssPropertiesValuesApi(string test) => Run("css/css-properties-values-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFontLoadingTests))]
    public void CssFontLoading(string test) => Run("css/css-font-loading", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlInteractionTests))]
    public void HtmlInteraction(string test) => Run("html/interaction", test, "html/interaction/focus/focusgroup");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(StreamsTests))]
    public void Streams(string test) => Run("streams", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(AccessibilityTests))]
    public void Accessibility(string test) => Run("accessibility", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(AccNameTests))]
    public void AccName(string test) => Run("accname", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlAamTests))]
    public void HtmlAam(string test) => Run("html-aam", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgAamTests))]
    public void SvgAam(string test) => Run("svg-aam", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WaiAriaTests))]
    public void WaiAria(string test) => Run("wai-aria", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlEditingTests))]
    public void HtmlEditing(string test) => Run("html/editing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BeaconTests))]
    public void Beacon(string test) => Run("beacon", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ClipboardApisTests))]
    public void ClipboardApis(string test) => Run("clipboard-apis", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ClientHintsTests))]
    public void ClientHints(string test) => Run("client-hints", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UAClientHintsTests))]
    public void UAClientHints(string test) => Run("ua-client-hints", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CookiesTests))]
    public void Cookies(string test) => Run("cookies", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CookieStoreTests))]
    public void CookieStore(string test) => Run("cookiestore", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebSocketsTests))]
    public void WebSockets(string test) => Run("websockets", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(EventSourceTests))]
    public void EventSource(string test) => Run("eventsource", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PreloadTests))]
    public void Preload(string test) => Run("preload", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PermissionsPolicyTests))]
    public void PermissionsPolicy(string test) => Run("permissions-policy", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ResizeObserverTests))]
    public void ResizeObserver(string test) => Run("resize-observer", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(IntersectionObserverTests))]
    public void IntersectionObserver(string test) => Run("intersection-observer", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(LoadingTests))]
    public void Loading(string test) => Run("loading", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(LayoutInstabilityTests))]
    public void LayoutInstability(string test) => Run("layout-instability", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ApngTests))]
    public void Apng(string test) => Run("apng", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PngTests))]
    public void Png(string test) => Run("png", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ImageBitmapRenderingContextTests))]
    public void ImageBitmapRenderingContext(string test) => Run("imagebitmap-renderingcontext", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlCanvasTests))]
    public void HtmlCanvas(string test) => Run("html/canvas", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PaintTimingTests))]
    public void PaintTiming(string test) => Run("paint-timing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(LargestContentfulPaintTests))]
    public void LargestContentfulPaint(string test) => Run("largest-contentful-paint", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ElementTimingTests))]
    public void ElementTiming(string test) => Run("element-timing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(EventTimingTests))]
    public void EventTiming(string test) => Run("event-timing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ForcedColorsModeTests))]
    public void ForcedColorsMode(string test) => Run("forced-colors-mode", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ResourceTimingTests))]
    public void ResourceTiming(string test) => Run("resource-timing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PerformanceTimelineTests))]
    public void PerformanceTimeline(string test) => Run("performance-timeline", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UserTimingTests))]
    public void UserTiming(string test) => Run("user-timing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(NavigationTimingTests))]
    public void NavigationTiming(string test) => Run("navigation-timing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ImportMapsTests))]
    public void ImportMaps(string test) => Run("import-maps", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(VisualViewportTests))]
    public void VisualViewport(string test) => Run("visual-viewport", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ViewportTests))]
    public void Viewport(string test) => Run("viewport", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ScrollAnimationsTests))]
    public void ScrollAnimations(string test) => Run("scroll-animations", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebStorageTests))]
    public void WebStorage(string test) => Run("webstorage", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(IndexedDBTests))]
    public void IndexedDB(string test) => Run("IndexedDB", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CacheStorageTests))]
    public void CacheStorage(string test) => Run("service-workers/cache-storage", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ServiceWorkerTests))]
    public void ServiceWorkers(string test) => Run("service-workers", test, "service-workers/cache-storage");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CompressionTests))]
    public void Compression(string test) => Run("compression", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebCryptoApiTests))]
    public void WebCryptoApi(string test) => Run("WebCryptoAPI", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WasmTests))]
    public void Wasm(string test) => Run("wasm", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebVttTests))]
    public void WebVtt(string test) => Run("webvtt", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(MediaSourceTests))]
    public void MediaSource(string test) => Run("media-source", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(MediaPlaybackQualityTests))]
    public void MediaPlaybackQuality(string test) => Run("media-playback-quality", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ConsoleTests))]
    public void ConsoleApi(string test) => Run("console", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CompatTests))]
    public void Compat(string test) => Run("compat", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(QuirksTests))]
    public void Quirks(string test) => Run("quirks", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlXhtmlSyntaxTests))]
    public void HtmlXhtmlSyntax(string test) => Run("html/the-xhtml-syntax", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlMetaTests))]
    public void HtmlMeta(string test) => Run("html/meta", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlSelectTests))]
    public void HtmlSelect(string test) => Run("html/select", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlObsoleteTests))]
    public void HtmlObsolete(string test) => Run("html/obsolete", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlScriptElementTests))]
    public void HtmlScriptElement(string test) => Run("html/scripting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlOfflineTests))]
    public void HtmlOffline(string test) => Run("html/browsers/offline", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlRenderingTests))]
    public void HtmlRendering(string test) => Run("html/rendering", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssCssomViewTests))]
    public void CssCssomView(string test) => Run("css/cssom-view", test, "css/cssom-view/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(XhrTests))]
    public void Xhr(string test) => Run("xhr", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FetchApiTests))]
    public void FetchApi(string test) => Run("fetch/api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FetchDataUrlsTests))]
    public void FetchDataUrls(string test) => Run("fetch/data-urls", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(FetchTests))]
    public void Fetch(string test) => Run("fetch", test, "fetch/api", "fetch/data-urls");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(NavigationApiTests))]
    public void NavigationApi(string test) => Run("navigation-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ReferrerPolicyTests))]
    public void ReferrerPolicy(string test) => Run("referrer-policy", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(MixedContentTests))]
    public void MixedContent(string test) => Run("mixed-content", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(XFrameOptionsTests))]
    public void XFrameOptions(string test) => Run("x-frame-options", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ReportingTests))]
    public void Reporting(string test) => Run("reporting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DeprecationReportingTests))]
    public void DeprecationReporting(string test) => Run("deprecation-reporting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(InterventionReportingTests))]
    public void InterventionReporting(string test) => Run("intervention-reporting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyBaseUriTests))]
    public void ContentSecurityPolicyBaseUri(string test) => Run("content-security-policy/base-uri", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyBlobTests))]
    public void ContentSecurityPolicyBlob(string test) => Run("content-security-policy/blob", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyChildSrcTests))]
    public void ContentSecurityPolicyChildSrc(string test) => Run("content-security-policy/child-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyConnectSrcTests))]
    public void ContentSecurityPolicyConnectSrc(string test) => Run("content-security-policy/connect-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyDefaultSrcTests))]
    public void ContentSecurityPolicyDefaultSrc(string test) => Run("content-security-policy/default-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyEmbeddedEnforcementTests))]
    public void ContentSecurityPolicyEmbeddedEnforcement(string test) => Run("content-security-policy/embedded-enforcement", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyFontSrcTests))]
    public void ContentSecurityPolicyFontSrc(string test) => Run("content-security-policy/font-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyFormActionTests))]
    public void ContentSecurityPolicyFormAction(string test) => Run("content-security-policy/form-action", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyFrameAncestorsTests))]
    public void ContentSecurityPolicyFrameAncestors(string test) => Run("content-security-policy/frame-ancestors", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyFrameSrcTests))]
    public void ContentSecurityPolicyFrameSrc(string test) => Run("content-security-policy/frame-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyGenTests))]
    public void ContentSecurityPolicyGen(string test) => Run("content-security-policy/gen", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyGenericTests))]
    public void ContentSecurityPolicyGeneric(string test) => Run("content-security-policy/generic", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyImgSrcTests))]
    public void ContentSecurityPolicyImgSrc(string test) => Run("content-security-policy/img-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyInheritanceTests))]
    public void ContentSecurityPolicyInheritance(string test) => Run("content-security-policy/inheritance", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyInsideWorkerTests))]
    public void ContentSecurityPolicyInsideWorker(string test) => Run("content-security-policy/inside-worker", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyMediaSrcTests))]
    public void ContentSecurityPolicyMediaSrc(string test) => Run("content-security-policy/media-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyMetaTests))]
    public void ContentSecurityPolicyMeta(string test) => Run("content-security-policy/meta", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyNavigationTests))]
    public void ContentSecurityPolicyNavigation(string test) => Run("content-security-policy/navigation", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyNonceHidingTests))]
    public void ContentSecurityPolicyNonceHiding(string test) => Run("content-security-policy/nonce-hiding", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyObjectSrcTests))]
    public void ContentSecurityPolicyObjectSrc(string test) => Run("content-security-policy/object-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyParsingTests))]
    public void ContentSecurityPolicyParsing(string test) => Run("content-security-policy/parsing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyPluginTypesTests))]
    public void ContentSecurityPolicyPluginTypes(string test) => Run("content-security-policy/plugin-types", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyReportHashTests))]
    public void ContentSecurityPolicyReportHash(string test) => Run("content-security-policy/report-hash", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyReportingTests))]
    public void ContentSecurityPolicyReporting(string test) => Run("content-security-policy/reporting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyReportingApiTests))]
    public void ContentSecurityPolicyReportingApi(string test) => Run("content-security-policy/reporting-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyResourceHintsTests))]
    public void ContentSecurityPolicyResourceHints(string test) => Run("content-security-policy/resource-hints", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicySandboxTests))]
    public void ContentSecurityPolicySandbox(string test) => Run("content-security-policy/sandbox", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyScriptSrcTests))]
    public void ContentSecurityPolicyScriptSrc(string test) => Run("content-security-policy/script-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyScriptSrcAttrElemTests))]
    public void ContentSecurityPolicyScriptSrcAttrElem(string test) => Run("content-security-policy/script-src-attr-elem", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicySecuritypolicyviolationTests))]
    public void ContentSecurityPolicySecuritypolicyviolation(string test) => Run("content-security-policy/securitypolicyviolation", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyStyleSrcTests))]
    public void ContentSecurityPolicyStyleSrc(string test) => Run("content-security-policy/style-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyStyleSrcAttrElemTests))]
    public void ContentSecurityPolicyStyleSrcAttrElem(string test) => Run("content-security-policy/style-src-attr-elem", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicySvgTests))]
    public void ContentSecurityPolicySvg(string test) => Run("content-security-policy/svg", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyUnsafeEvalTests))]
    public void ContentSecurityPolicyUnsafeEval(string test) => Run("content-security-policy/unsafe-eval", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyUnsafeHashesTests))]
    public void ContentSecurityPolicyUnsafeHashes(string test) => Run("content-security-policy/unsafe-hashes", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyWasmUnsafeEvalTests))]
    public void ContentSecurityPolicyWasmUnsafeEval(string test) => Run("content-security-policy/wasm-unsafe-eval", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyWebrtcTests))]
    public void ContentSecurityPolicyWebrtc(string test) => Run("content-security-policy/webrtc", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyWorkerSrcTests))]
    public void ContentSecurityPolicyWorkerSrc(string test) => Run("content-security-policy/worker-src", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContentSecurityPolicyXsltTests))]
    public void ContentSecurityPolicyXslt(string test) => Run("content-security-policy/xslt", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(UpgradeInsecureRequestsTests))]
    public void UpgradeInsecureRequests(string test) => Run("upgrade-insecure-requests", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssColorTests))]
    public void CssColor(string test) => Run("css/css-color", test, "css/css-color/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssHighlightApiTests))]
    public void CssHighlightApi(string test) => Run("css/css-highlight-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssPseudoHighlightCascadeTests))]
    public void CssPseudoHighlightCascade(string test) => Run("css/css-pseudo/highlight-cascade", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssMixinsTests))]
    public void CssMixins(string test) => Run("css/css-mixins", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssShadowTests))]
    public void CssShadow(string test) => Run("css/css-shadow", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssMediaQueriesTests))]
    public void CssMediaQueries(string test) => Run("css/mediaqueries", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssEnvTests))]
    public void CssEnv(string test) => Run("css/css-env", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssViewportTests))]
    public void CssViewport(string test) => Run("css/css-viewport", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTypedOmTests))]
    public void CssTypedOm(string test) => Run("css/css-typed-om", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssParserApiTests))]
    public void CssParserApi(string test) => Run("css/css-parser-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssValuesTests))]
    public void CssValues(string test) => Run("css/css-values", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssEasingTests))]
    public void CssEasing(string test) => Run("css/css-easing", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssUiTests))]
    public void CssUi(string test) => Run("css/css-ui", test, "css/css-ui/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssPseudoTests))]
    public void CssPseudo(string test) => Run("css/css-pseudo", test, "css/css-pseudo/parsing", "css/css-pseudo/highlight-cascade");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFontsTests))]
    public void CssFonts(string test) => Run("css/css-fonts", test, "css/css-fonts/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssBackgroundsTests))]
    public void CssBackgrounds(string test) => Run("css/css-backgrounds", test, "css/css-backgrounds/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssLogicalTests))]
    public void CssLogical(string test) => Run("css/css-logical", test, "css/css-logical/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssDisplayTests))]
    public void CssDisplay(string test) => Run("css/css-display", test, "css/css-display/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssListsTests))]
    public void CssLists(string test) => Run("css/css-lists", test, "css/css-lists/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTextDecorTests))]
    public void CssTextDecor(string test) => Run("css/css-text-decor", test, "css/css-text-decor/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssCounterStylesTests))]
    public void CssCounterStyles(string test) => Run("css/css-counter-styles", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssBordersTests))]
    public void CssBorders(string test) => Run("css/css-borders", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssColorHdrTests))]
    public void CssColorHdr(string test) => Run("css/css-color-hdr", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssScrollbarsTests))]
    public void CssScrollbars(string test) => Run("css/css-scrollbars", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssScrollSnapTests))]
    public void CssScrollSnap(string test) => Run("css/css-scroll-snap", test, "css/css-scroll-snap/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssScrollAnchoringTests))]
    public void CssScrollAnchoring(string test) => Run("css/css-scroll-anchoring", test, "css/css-scroll-anchoring/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssAnchorPositionTests))]
    public void CssAnchorPosition(string test) => Run("css/css-anchor-position", test, "css/css-anchor-position/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssWritingModesTests))]
    public void CssWritingModes(string test) => Run("css/css-writing-modes", test, "css/css-writing-modes/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTextTests))]
    public void CssText(string test) => Run("css/css-text", test, "css/css-text/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssPageTests))]
    public void CssPage(string test) => Run("css/css-page", test, "css/css-page/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssBreakTests))]
    public void CssBreak(string test) => Run("css/css-break", test, "css/css-break/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssRubyTests))]
    public void CssRuby(string test) => Run("css/css-ruby", test, "css/css-ruby/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssCompositingTests))]
    public void CssCompositing(string test) => Run("css/compositing", test, "css/compositing/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFillStrokeTests))]
    public void CssFillStroke(string test) => Run("css/fill-stroke", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFetchingTests))]
    public void CssFetching(string test) => Run("css/fetching", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssAnimationsTests))]
    public void CssAnimations(string test) => Run("css/css-animations", test, "css/css-animations/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTransitionsTests))]
    public void CssTransitions(string test) => Run("css/css-transitions", test, "css/css-transitions/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssViewTransitionsTests))]
    public void CssViewTransitions(string test) => Run("css/css-view-transitions", test, "css/css-view-transitions/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WebAnimationsTests))]
    public void WebAnimations(string test) => Run("web-animations", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssCss2Tests))]
    public void CssCss2(string test) => Run("css/CSS2", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssAlignTests))]
    public void CssAlign(string test) => Run("css/css-align", test, "css/css-align/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssBoxTests))]
    public void CssBox(string test) => Run("css/css-box", test, "css/css-box/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssColorAdjustTests))]
    public void CssColorAdjust(string test) => Run("css/css-color-adjust", test, "css/css-color-adjust/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssContainTests))]
    public void CssContain(string test) => Run("css/css-contain", test, "css/css-contain/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssContentTests))]
    public void CssContent(string test) => Run("css/css-content", test, "css/css-content/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFlexboxTests))]
    public void CssFlexbox(string test) => Run("css/css-flexbox", test, "css/css-flexbox/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssGapsTests))]
    public void CssGaps(string test) => Run("css/css-gaps", test, "css/css-gaps/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssGridTests))]
    public void CssGrid(string test) => Run("css/css-grid", test, "css/css-grid/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssPaintApiTests))]
    public void CssPaintApi(string test) => Run("css/css-paint-api", test, "css/css-paint-api/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(WorkletsTests))]
    public void Worklets(string test) => Run("worklets", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssLayoutApiTests))]
    public void CssLayoutApi(string test) => Run("css/css-layout-api", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(AnimationWorkletTests))]
    public void AnimationWorklet(string test) => Run("animation-worklet", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SpeculationRulesTests))]
    public void SpeculationRules(string test) => Run("speculation-rules", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssImagesTests))]
    public void CssImages(string test) => Run("css/css-images", test, "css/css-images/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssInlineTests))]
    public void CssInline(string test) => Run("css/css-inline", test, "css/css-inline/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssLinkParamsTests))]
    public void CssLinkParams(string test) => Run("css/css-link-params", test, "css/css-link-params/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssMaskingTests))]
    public void CssMasking(string test) => Run("css/css-masking", test, "css/css-masking/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssMulticolTests))]
    public void CssMulticol(string test) => Run("css/css-multicol", test, "css/css-multicol/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssOverflowTests))]
    public void CssOverflow(string test) => Run("css/css-overflow", test, "css/css-overflow/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssPositionTests))]
    public void CssPosition(string test) => Run("css/css-position", test, "css/css-position/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssShapesTests))]
    public void CssShapes(string test) => Run("css/css-shapes", test, "css/css-shapes/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssSizeAdjustTests))]
    public void CssSizeAdjust(string test) => Run("css/css-size-adjust", test, "css/css-size-adjust/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssSizingTests))]
    public void CssSizing(string test) => Run("css/css-sizing", test, "css/css-sizing/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTablesTests))]
    public void CssTables(string test) => Run("css/css-tables", test, "css/css-tables/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTransformsTests))]
    public void CssTransforms(string test) => Run("css/css-transforms", test, "css/css-transforms/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFilterEffectsTests))]
    public void CssFilterEffects(string test) => Run("css/filter-effects", test, "css/filter-effects/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssMotionTests))]
    public void CssMotion(string test) => Run("css/motion", test, "css/motion/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssWillChangeTests))]
    public void CssWillChange(string test) => Run("css/css-will-change", test, "css/css-will-change/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssRhythmTests))]
    public void CssRhythm(string test) => Run("css/css-rhythm", test, "css/css-rhythm/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFormsTests))]
    public void CssForms(string test) => Run("css/css-forms", test, "css/css-forms/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssOverscrollBehaviorTests))]
    public void CssOverscrollBehavior(string test) => Run("css/css-overscroll-behavior", test, "css/css-overscroll-behavior/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssForcedColorAdjustTests))]
    public void CssForcedColorAdjust(string test) => Run("css/css-forced-color-adjust", test, "css/css-forced-color-adjust/parsing");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgGeometryTests))]
    public void SvgGeometry(string test) => Run("svg/geometry", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgPaintingTests))]
    public void SvgPainting(string test) => Run("svg/painting", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgTests))]
    public void Svg(string test) => Run("svg", test, SvgExcludedDirectories);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssParsingTests))]
    public void CssParsing(string test) => Run(CssParsingDirectory, test, CssParsingExcludedDirectories);
}
