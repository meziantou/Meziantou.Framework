---
title: Limitations
description: What the engine does not do, and where it differs from a browser.
---

The library implements most of the web platform, but it is not a complete browser. The main differences are listed
below; the [WPT coverage](10-WptCoverage/01-Overview.md) gives the reason why each web-platform-test that does not pass
fails.

## Display and input

- **No window.** The pages are rendered into images: your application takes the screenshots and plays the user with
  [BrowsingContext.Input](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.Input) (see [User input](05-LoadingPages/06-UserInput.md)). There is no pinch zoom, and the text cannot be
  selected with the pointer.
- **Layout.** The layout engine is internal: it runs when a web API reads the geometry, and its results are read through
  the web APIs only (CSSOM View, the computed styles, the observers; see [Layout and geometry](05-LoadingPages/05-LayoutAndGeometry.md)).
  It does not implement:
  - the layout of the pages whose inline size differs from the first page;
  - the repetition of a hyphen at the start of the next line;
  - the spelling changes at a hyphenation break, except in Dutch, Hungarian and Pinyin;
  - the hyphenation dictionaries under copyleft licenses (cs, hu, hy, lv);
  - the scrolling of a `::scroll-marker-group` that is a scroll container;
  - in the shaping of the complex scripts, the Myanmar Zawgyi encoding and the Arabic fallback shaping of the
    Windows-1256 fonts.
- **Painting.** The core library paints the documents into a display list; a [rendering backend](07-Rendering/01-Overview.md)
  produces the pixels. Without a backend, nothing is painted, the images are not decoded and `canvas.getContext("2d")`
  returns `null`. The Imaging backend does not paint:
  - the color glyphs other than the layers of `COLR` version 0 (the paint graphs of `COLR` version 1, the bitmap and SVG
    glyphs);
  - the wrapping of the SVG texts in the shapes of `shape-inside`;
  - the canvas layers (`beginLayer()`).

  Float16 canvas storage and transfers are supported, but gradients and intermediate effects use normalized 16-bit colors
  (see [Canvas](07-Rendering/05-Canvas.md)). As in Blink, the painted boxes and the baselines are snapped to the device
  pixels and the font metrics are rounded to whole pixels; the layout keeps its fractional geometry.

## Images and media

- **Images are decoded by the rendering backend.** Without a backend, only the natural dimensions of the images are read
  from their header: `decode()` resolves once the image is available, and `createImageBitmap()` rejects the images and the
  blobs. The Imaging backend decodes PNG (including animated PNG), JPEG (including CMYK), GIF, WebP, AVIF, JPEG XL, BMP and
  ICO images, with their EXIF orientation and resolution and their color profile.
- **Audio and video are not decoded.** The media elements run on a media backend that your application provides
  ([BrowsingContextOptions.MediaBackend](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaBackend)); without it, they have no media. The Media Source Extensions parse the segments
  with the backend but do not decode the frames. The media controls and Encrypted Media are not implemented.

## Network and security

- **Nothing is fetched** without a [document loader](05-LoadingPages/02-NetworkRequests.md), and no script runs without a
  [script engine](06-Scripting/01-Overview.md): the scripts are not fetched and the event handler attributes have no
  effect. A document parsed with [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser) has no browsing context: no window, no computed style and no
  event loop, so the mutation observer callbacks are not invoked.
- **No downloads**, and the third-party cookies are not blocked.
- **Reporting API.** Content Security Policy reports its violations, but the Reporting API generates only the CSP, COEP
  and test reports: there are no deprecation, intervention, COOP or Document Policy reports.

## Scripting

- The streams cannot be transferred between workers or windows.
- WebAssembly threads are not supported.
- The scripts run in your process: read [Running untrusted pages](06-Scripting/04-RunningUntrustedPages.md) before you
  load pages you do not control.

## Experimental APIs

The members that implement proposals that no specification defines yet (such as [Element.StreamAppendHtml()](cref:Meziantou.Framework.WebPlatform.Dom.Element.StreamAppendHtml)) have the
`[Experimental]` attribute: using them reports an error with a diagnostic per feature (`WEBPLATEXP0002`...), which you
suppress to use them (`<NoWarn>$(NoWarn);WEBPLATEXP0002</NoWarn>`). They may change or be removed when the proposal
changes.
