---
title: Limitations
description: What the engine does not do, and where it differs from a browser.
---

The library implements most of the web platform, but it is not a complete browser. The main differences are listed
below; the [WPT coverage](10-WptCoverage/01-Overview.md) gives the reason why each web-platform-test that does not pass
fails.

## Display and input

- **No window.** The pages are rendered into images: your application takes the screenshots and plays the user with
  [BrowsingContext.Input](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.Input) (see [User input](05-LoadingPages/06-UserInput.md)). There is no pinch zoom.
- **Layout.** The layout engine is internal: its results are read through the web APIs (CSSOM View, the computed styles,
  the observers). The features that are not implemented are listed in the
  [readme of the package](https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.Framework.WebPlatform/readme.md#limitations).
- **Painting.** The canvas layers (`beginLayer()`) and the `float16` canvases are not implemented. Without a
  [rendering backend](07-Rendering/01-Overview.md), nothing is painted and the images are not decoded.

## Media and network

- **Audio and video are not decoded.** The media elements run on a media backend that your application provides
  ([BrowsingContextOptions.MediaBackend](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaBackend)); without it, they have no media. The media controls and Encrypted Media are not
  implemented.
- **No downloads**, and the third-party cookies are not blocked.
- **Nothing is fetched** without a [document loader](05-LoadingPages/02-NetworkRequests.md), and no script runs without a
  [script engine](06-Scripting/01-Overview.md).

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
