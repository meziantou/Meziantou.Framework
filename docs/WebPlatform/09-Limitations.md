---
title: Limitations
description: What the engine does not do, and where it differs from a browser.
---

The library implements most of the web platform, but it is not a complete browser. The main differences are listed
below; the [WPT coverage](10-WptCoverage/01-Overview.md) gives the reason why each web-platform-test that does not pass
fails.

## Display and input

- **No window.** The pages are rendered into images: your application takes the screenshots and plays the user with
  [BrowsingContext.Input](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.Input) (see [User input](05-LoadingPages/06-UserInput.md)). The pinch zoom cannot zoom out below the initial
  scale of the page, and the scroll methods of the window (`scrollTo()`...) do not scroll the zoomed visual viewport. The
  delegated ink trails (`navigator.ink`) of the documents of the `iframe` elements are not drawn in the screenshots of the
  top-level document, and the scroll performance timing (`PerformanceScrollTiming`) does not report the scrolls of the
  scripts nor the scrolls of the zoomed visual viewport.
- **CSS values.** A value with more than 1,000 repetitions of a component of its grammar (e.g. more than 1,000 tracks
  listed in `grid-template-columns`, or 1,000 families in `font-family`) is invalid: the parser backtracks through the
  repetitions, and a fixed limit keeps the time it takes small and the same on every platform.
- **Layout.** The layout engine is internal: it runs when a web API reads the geometry, and its results are read through
  the web APIs only (CSSOM View, the computed styles, the observers; see [Layout and geometry](05-LoadingPages/05-LayoutAndGeometry.md)).
  It does not implement:
  - the layout of the content at the top of a page whose inline size differs from the previous page when it cannot be laid
    out again in that inline size (a line box or a monolithic box sliced by the break that is not laid out the same way, a
    multi-column container with anchored absolutely positioned boxes, with an absolutely positioned box at its static
    position broken by the end of the previous page, or with a multi-column container broken in its columns, a grid whose
    number of columns depends on its width and whose items are not placed in their order, a table row, a flex line or a
    grid row whose items cannot continue side by side): the rest of it keeps the layout of the previous page, and the
    content after it is laid out in the new inline size (the whole page keeps the inline size of the previous page when
    that content does not end on the page, or when other content continues beside its end);
  - the spelling changes at a hyphenation break that need a dictionary (the triple consonants of the Swedish and Norwegian
    compound words);
  - the hyphenation dictionaries under copyleft licenses (cs, hu, hy, lv);
  - the scrolling of a `::scroll-marker-group` that is a scroll container;
  - the values of `shape-inside` of CSS Shapes 2 (`display`, `outside-shape`, an image, a shape box): only the shapes of
    SVG 2 (basic shapes and references to SVG shape elements) wrap the SVG texts;
  - in the shaping of the complex scripts, the Myanmar Zawgyi encoding and the Arabic fallback shaping of the
    Windows-1256 fonts.
- **SVG `use` elements.** The `use` elements have no shadow tree of element instances: the referenced content is rendered
  and hit tested in place, and the events of its instances are dispatched to the `use` element. A click of the user on
  the instance of a hyperlink (an `a` element of the referenced content) follows the referenced hyperlink, whose source
  element is the `use` element; the `ShadowAnimation` interface and the `:hover` of the instances are not supported.
- **Painting.** The core library paints the documents into a display list; a [rendering backend](07-Rendering/01-Overview.md)
  produces the pixels. Without a backend, nothing is painted, the images are not decoded and `canvas.getContext("2d")`
  returns `null`. The Imaging backend draws the color glyphs (`COLR` versions 0 and 1, `SVG `, `sbix` and `CBDT`) with
  their colors, the canvas layers (`beginLayer()`) and the `CanvasFilter` objects.

  Float16 canvas storage and transfers are supported, but gradients and intermediate effects use normalized 16-bit colors
  (see [Canvas](07-Rendering/05-Canvas.md)). As in Blink, the painted boxes and the baselines are snapped to the device
  pixels and the font metrics are rounded to whole pixels; the layout keeps its fractional geometry.

## Images and media

- **Images are decoded by the rendering backend.** Without a backend, only the natural dimensions of the images are read
  from their header: `decode()` resolves once the image is available, and `createImageBitmap()` rejects the images and the
  blobs. The Imaging backend decodes PNG (including animated PNG), JPEG (including CMYK), GIF, WebP, AVIF, JPEG XL, BMP and
  ICO images, with their EXIF orientation and resolution and their color profile. The pages are sRGB: the colors of an
  image outside of the gamut of sRGB are clipped, and only the Display P3 and half-float canvases keep them (the images
  whose color profile has lookup tables and the `cICP` descriptions other than Display P3 are clipped to sRGB there too).
- **Audio and video are not decoded.** The media elements run on a media backend that your application provides
  ([BrowsingContextOptions.MediaBackend](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaBackend)); without it, they have no media. The Media Source Extensions parse the segments
  with the backend but do not decode the frames. The media controls are not implemented, and Encrypted Media
  Extensions only implement the Clear Key key system, for the media data of the Media Source Extensions.
- **There is no audio or video codec.** WebCodecs (`AudioDecoder`, `VideoDecoder`, `AudioEncoder`, `VideoEncoder`) and
  `MediaRecorder` use the codecs of a backend that your application provides
  ([BrowsingContextOptions.MediaCodecs](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaCodecs), see
  [Audio and video codecs](05-LoadingPages/10-AudioAndVideoCodecs.md)); without it, `configure()` fails with a
  `NotSupportedError`. `MediaRecorder` records WebM streams of VP8 and Opus only.

## Network and security

- **Nothing is fetched** without a [document loader](05-LoadingPages/02-NetworkRequests.md), and no script runs without a
  [script engine](06-Scripting/01-Overview.md): the scripts are not fetched and the event handler attributes have no
  effect. A document parsed with [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser) has no browsing context: no window, no computed style and no
  event loop, so the mutation observer callbacks are not invoked.
- **Storage Access API.** The third-party cookies are not blocked unless you
  [block them](05-LoadingPages/03-CookiesAndStorage.md#third-party-cookies-and-storage-access), and the Storage Access
  API only gives access to the cookies: `requestStorageAccess(types)` and its `StorageAccessHandle` (the unpartitioned
  local storage, IndexedDB, caches... of a third-party frame) are not supported.
- **Reporting API.** The `deprecation` and `intervention` reports are only generated for the features listed in
  [Network requests](05-LoadingPages/02-NetworkRequests.md#responses) (the synchronous `XMLHttpRequest` requests of a
  window, `preventDefault()` in the listeners that are passive by default), and there is no Crash Reporting API.
- **Signature-based Subresource Integrity** does not support the `signature` attribute of the inline scripts of
  earlier drafts, nor the `'ed25519-…'` sources of Content Security Policy.
- **Compression dictionaries**: the `dcb` streams whose dictionary and data exceed the window of the stream are not
  decoded, and the requests of `<link rel="compression-dictionary">` have the empty destination (not
  `compression-dictionary`).

## Scripting

- The bytes of a shared `WebAssembly.Memory` are allocated for its maximum size, up to 2 GiB minus one page: a shared
  memory cannot grow beyond. A `memory.atomic.wait32` instruction is not interrupted by the limits of the script engine.
- JavaScript Promise Integration (`WebAssembly.Suspending`, `WebAssembly.promising()`) is not supported.
- The time spent on the styles and the layout is not measured: the long animation frames report a `styleDuration`, a
  `layoutDuration` and forced style and layout durations of 0, and no `presentationTime`.
- The scripts run in your process: read [Running untrusted pages](06-Scripting/04-RunningUntrustedPages.md) before you
  load pages you do not control.

## Experimental APIs

The members that implement proposals that no specification defines yet (such as [ContainerQueryList.OnChange](cref:Meziantou.Framework.WebPlatform.Css.Om.ContainerQueryList.OnChange)) have the
`[Experimental]` attribute: using them reports an error with a diagnostic per feature (`WEBPLATEXP0003`...), which you
suppress to use them (`<NoWarn>$(NoWarn);WEBPLATEXP0005</NoWarn>`). They may change or be removed when the proposal
changes.
