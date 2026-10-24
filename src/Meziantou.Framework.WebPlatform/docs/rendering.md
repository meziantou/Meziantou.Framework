# Rendering

The library computes the layout of the documents (see [layout.md](layout.md)) and paints them, but it never draws pixels
by itself: the core library has no graphics dependency (it stays trimming- and AOT-compatible). The painting produces a
backend-agnostic **display list**, and a **rendering backend**, plugged into the browsing contexts like a
[script engine](scripting.md), rasterizes it, decodes the images and draws the canvases.

```csharp
using Meziantou.Framework.Imaging;             // Image<Rgba32>
using Meziantou.Framework.WebPlatform.Imaging; // the Meziantou.Framework.WebPlatform.Imaging package

using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 1280, Height = 720, DevicePixelRatio = 2 },
    DocumentLoader = DocumentLoader.Create(), // the style sheets, the fonts and the images of the page
    RenderingBackend = new ImagingRenderingBackend(),
});

context.Navigate("https://example.com/");
await context.EventLoop.RunUntilIdleAsync();

// The viewport (2560x1440 pixels), as PNG bytes or as an image of Meziantou.Framework.Imaging
await File.WriteAllBytesAsync("page.png", context.TakeScreenshotAsPng());
using Image<Rgba32> image = context.TakeScreenshot();

// The border box of an element (the rectangle of getBoundingClientRect()), at another scale
using var element = context.Document.QuerySelector("h1")!.TakeScreenshot(new ScreenshotOptions { DevicePixelRatio = 1 });
```

Without a backend (the default, `BrowsingContextOptions.RenderingBackend` is null):

- nothing is rasterized: the layout and the geometry APIs (CSSOM View, the hit testing, the observers) work as usual;
- the APIs that need pixels behave as in a user agent that does not support them: `canvas.getContext('2d')` and
  `offscreenCanvas.getContext('2d')` return null (`Path2D`, `toDataURL()` and `toBlob()`, which serialize the bitmap as
  PNG, still work);
- the images are not decoded (only the natural size is read from their header), and their encoded bytes are not kept (except
  the ones of the SVG images, whose documents are painted by the library): `img.decode()` resolves once the image is
  available, `createImageBitmap()` rejects the images and the blobs with an `InvalidStateError` (the bitmaps of the
  `ImageData`, `ImageBitmap` and `canvas` sources are created as usual);
- the display list is still painted for the [paint timing](#paint-timing);
- the snapshots of the [view transitions](view-transitions.md) are display lists, drawn in the display list of the document.

## Screenshots

The `Meziantou.Framework.WebPlatform.Imaging` package adds extension methods (`ImagingScreenshotExtensions`):

| Method | Result |
| --- | --- |
| `BrowsingContext.TakeScreenshot(options)` | The viewport of the active document of the browsing context, as an `Image<Rgba32>` of [Meziantou.Framework.Imaging](https://www.nuget.org/packages/Meziantou.Framework.Imaging) (owned by the caller) |
| `Document.TakeScreenshot(options)` | The viewport of a document (the area of its `iframe` for a nested document: transparent where the document paints nothing) |
| `Element.TakeScreenshot(options)` | The border box of an element in its viewport (`getBoundingClientRect()`, transforms included), rounded outwards to device pixels, with what is painted over it; the parts outside of the viewport are painted too |
| `TakeScreenshotAsPng(options)` | The same screenshots, encoded as PNG |
| `TakeScreenshotAsJpeg(options)` | The same screenshots, encoded as JPEG (composited over white, quality `ScreenshotOptions.Quality`, 80 by default as in Chromium, chroma subsampled 4:2:0) |

The `ScreenshotOptions` are the ones of the screenshots of Playwright:

| Option | Playwright | Effect |
| --- | --- | --- |
| `DevicePixelRatio` | `scale: 'css'` (1) | The number of pixels per CSS px (see below) |
| `OmitBackground` | `omitBackground` | The canvas of a top-level document is transparent instead of its canvas color (white, or the dark color of a dark color scheme): the backgrounds of the document (the root element, the body propagated to the canvas) are still painted, the rest is transparent (e.g. a transparent SVG document). The printed pages have no white paper. Ignored by the JPEG screenshots |
| `FullPage` | `fullPage` | The whole scrolling area of the viewport from the origin of the document (`scrollWidth` x `scrollHeight`, at least the viewport), without the scrollbars of the viewport. The layout does not change (`vh` and the media queries still see the viewport); a fixed element is painted once, where it is at the current scroll position |
| `Clip` | `clip` | An area in CSS px, relative to the viewport (to the document with `FullPage`), clipped to it; an area empty once clipped throws an `ArgumentException` |
| `Quality` | `quality` | The quality of the JPEG screenshots (0 to 100) |

The screenshots are compared with the ones of Playwright by `ChromiumDiff --render` (see
[fuzzing.md](fuzzing.md#comparing-the-exports-with-playwright)).

- The size of a screenshot is the size of the area in CSS px multiplied by the device pixel ratio:
  `ViewportOptions.DevicePixelRatio` by default, or `ScreenshotOptions.DevicePixelRatio` (which scales the painting
  without changing the layout: the media queries still see the ratio of the viewport). The screenshot of a viewport
  includes its [scrollbars](#scrollbars) (or their empty gutters). The pixels are sRGB, 8 bits per component, not
  premultiplied (`Rgba32`); the canvas of a top-level document is drawn over its canvas color (white, unless
  `OmitBackground`).
- A screenshot paints the current state of the document: the layout is computed when it is out of date, as by the geometry
  APIs (call it from the thread of the event loop, after the resources are loaded, e.g. after `RunUntilIdleAsync()`). The
  display list is cached by the document: two screenshots without change in between paint it once.
- The screenshots use the `ImagingRenderingBackend` of the browsing context. Without one, a temporary backend with the
  default options rasterizes the screenshot, but the images are not drawn (a document keeps the encoded bytes of its images
  only when its browsing context has a backend).
- A document that is not the active document of a browsing context (e.g. parsed by `HtmlDocumentParser`) and an element
  without box (or with an empty box) throw an `InvalidOperationException`.

`samples/Meziantou.Framework.WebPlatform.ScreenshotSample` takes a screenshot of a URL
(`dotnet run --project samples/Meziantou.Framework.WebPlatform.ScreenshotSample -- https://example.com/ page.png`).

The screenshots are tested by the [reftests of the web-platform-tests](web-platform-tests.md#reftests): most of the
~28,000 reftests of the fetched directories pass (see [wpt-coverage.md](wpt-coverage.md#reftests)); the coverage pages give the reason of each other one (the
layout and painting features that are not implemented, and the bugs).

## Printing

`ImagingPrintExtensions` prints a document into pages ([CSS Paged Media](https://drafts.csswg.org/css-page-3/), see
[Paged media](layout.md#paged-media-csslayoutpaged-layoutenginepagedcs) for the pagination):

| Method | Result |
| --- | --- |
| `BrowsingContext.RenderPages(printOptions, options)`, `Document.RenderPages(printOptions, options)` | One `Image<Rgba32>` per page (owned by the caller), of the size of its page box (transposed when the page is rotated by `page-orientation`) |
| `RenderPagesAsPng(printOptions, options)` | The same pages, encoded as PNG |

- `PrintOptions` (core library, `Dom` namespace) gives the size of the pages of `size: auto` (`PageWidth`, `PageHeight`, in
  CSS px; US Letter by default) and their margins when no `@page` rule sets them (`MarginTop`... 0.5in by default). The
  `size` of the `@page` rules (lengths, page size keywords, orientation) gives the size of each page, and the `width` and
  `height` of the page context the size of its page box (see [Paged media](layout.md#paged-media-csslayoutpaged-layoutenginepagedcs)):
  the pages of a document may have different sizes. `page-orientation: rotate-left | rotate-right` rotates the printed page a
  quarter turn (counter-clockwise or clockwise) after it is laid out. `ScreenshotOptions.DevicePixelRatio` scales the images (the device pixel ratio of the
  viewport by default).
- Each page is painted on white paper, as in the browsers: the background of the page box (the `@page` rules) over the
  whole page (its margins included, whatever `background-clip`; its images are positioned in the page box, by
  `background-origin`), its borders and outline, the page-margin boxes whose
  `z-index` is negative, then the document in the page area (clipped to the padding box of the page box; it is painted at
  the position of the page area, which may be fractional, and snapped to the device pixels of the page, so a box at the
  same position in the page covers the same pixels whatever the page margins): its canvas background, positioned in the root element stitched across the pages
  (`box-decoration-break: slice`), covers the padding box of the page box, as in Blink; then the other page-margin boxes.
  Each page-margin box is painted atomically (as a stacking context), in the order of its `z-index`, then from the top-left
  corner clockwise.
- The printing does not change the screen: the document is styled and laid out by other engines while it is printed
  (`Document.BeginPrint`, an internal `PrintSession`): the screen is not laid out again afterwards, no transition starts and
  no event is fired (`beforeprint`, `afterprint` and `window.print()` are not implemented: `print()` does nothing). The
  images (also the ones of the page contexts and of the page-margin boxes) and the font faces used only by the print styles
  are fetched by the printing but are not drawn when they were not loaded before the pages are painted
  (print again once they are). The nested documents (`iframe`) are painted as on the screen.
- `ImagingPrintExtensions` renders the pages as images; `PdfPrintExtensions` (`Meziantou.Framework.WebPlatform.PdfRendering`)
  writes them into a PDF document (see [PDF output](#pdf-output)).
- `ScreenshotOptions.OmitBackground` paints the pages over a transparent sheet instead of white paper.

The other `PrintOptions` are the ones of the PDF of Playwright (`page.pdf()`):

| Option | Playwright | Effect |
| --- | --- | --- |
| `PageWidth`, `PageHeight` | `format`, `width`, `height` | The size of the pages of `size: auto` (US Letter by default, as in Playwright) |
| `MarginTop`... | `margin` | The margins of the pages when no `@page` rule sets them (0.5in by default; **0 by default in Playwright**) |
| `Landscape` | `landscape` | The width and the height of the pages of `size: auto` are swapped |
| `Scale` | `scale` | From 0.1 to 2: the content is laid out in the page area divided by the scale and painted scaled; the page margins and the page-margin boxes are not scaled (as in Chromium) |
| `PrintBackground` | `printBackground` | **True by default (false in Playwright).** When false, as in the browsers, the backgrounds of the boxes whose `print-color-adjust` is `economy` (the initial value) are painted white, without their images, and their nearly white text is darkened; `print-color-adjust: exact` keeps the backgrounds |
| `PreferCssPageSize` | `preferCSSPageSize` | **True by default (false in Playwright).** When false, the pages are printed on the paper of `PageWidth` x `PageHeight` (in the orientation of the page box): a page box of another size (the `size` of `@page`) is centered on it, scaled down when it does not fit, as in Chromium |
| `PageRanges` | `pageRanges` | The pages to print (`1-5, 8, -3, 11-`): once each, in order, with their numbers; the numbers beyond the last page are ignored, an invalid syntax throws an `ArgumentException` and ranges that select no page an `InvalidOperationException`. The links of a PDF to the pages that are not printed are removed |
| `DisplayHeaderFooter`, `HeaderTemplate`, `FooterTemplate` | `displayHeaderFooter`, `headerTemplate`, `footerTemplate` | The HTML templates printed in the top and bottom margins of each page, as in Chromium: each template is a document of its own (no script; only its `data:` resources), as wide as the page, 20px from its edge, whose root font size is 0 (set the size of the text), whose backgrounds are not printed (unless `print-color-adjust: exact`); the content of the elements of class `date`, `title`, `url`, `pageNumber` and `totalPages` is the date (`M/d/yyyy`), the title and the URL of the document, the number of the page and the number of pages |

The defaults that differ keep the previous behavior of the library; set `MarginTop`, `MarginRight`, `MarginBottom` and
`MarginLeft` to 0, `PrintBackground` and `PreferCssPageSize` to false to print as the defaults of `page.pdf()`. The PDF are
compared with the ones of Playwright by `ChromiumDiff --render` (see [fuzzing.md](fuzzing.md#comparing-the-exports-with-playwright)).

## Device emulation

The options of the devices of Playwright (`playwright.devices`, the options of `browser.newContext()`) are the ones of the
viewport and of the browsing context:

| Option | Playwright | Effect |
| --- | --- | --- |
| `ViewportOptions.Width`, `Height` | `viewport` | The size of the viewport in CSS px |
| `ViewportOptions.DevicePixelRatio` | `deviceScaleFactor` | The device pixel ratio (the `resolution` media feature; the size of the screenshots) |
| `ViewportOptions.ScreenWidth`, `ScreenHeight` | `screen` | The size of the screen (`device-width`, `device-height`, `screen.width`); the viewport by default |
| `ViewportOptions.HasTouch` | `hasTouch` | `pointer`/`any-pointer: coarse`, `hover`/`any-hover: none`, `navigator.maxTouchPoints` 1 |
| `ViewportOptions.IsMobile` | `isMobile` | The `viewport` meta element sets the layout viewport and the scale of the top-level document (see below), overlay scrollbars, mobile client hints |
| `BrowsingContextOptions.UserAgent` | `userAgent` | `navigator.userAgent`, the user agent of the reports, the `User-Agent` header of the requests |
| `ViewportOptions.PreferredColorScheme` | `colorScheme` | `prefers-color-scheme` |
| `ViewportOptions.MediaType` | `page.emulateMedia({ media })` | `screen` or `print` (the `resolution` of the print media is 300dpi, as in Chromium) |

`DeviceDescriptor` describes a device with the names of Playwright and creates its `ViewportOptions` (`CreateViewport`); the
library has no list of devices: copy the values of a descriptor of Playwright.

On a mobile device (`IsMobile`), as in the mobile emulation of Chromium, the last `<meta name="viewport">` element of the
top-level document ([CSS Viewport](https://drafts.csswg.org/css-viewport/)) sets its layout viewport: the initial containing
block, the viewport units, the width media queries and `innerWidth` follow it, while the screen (`device-width`) stays the
viewport of the options. Its width is the `width` of the meta element (`device-width` or a length; at least the width of
the device divided by `initial-scale`), the width of the device divided by `initial-scale`, or 980px without both; its height
keeps the aspect ratio of the device unless `height` is set. The page is scaled by `initial-scale`, or to fit the width of its
contents in the width of the device, within `minimum-scale` and `maximum-scale` (5 by default): `visualViewport.scale` is
the scale and the screenshots show the visual viewport (the viewport of the device) at the device pixel ratio multiplied by
the scale. The text autosizing of Chromium (which enlarges the text of the pages that are scaled down) is not implemented,
and there is no pinch zoom: the visual viewport is at the origin of the layout viewport.

## PDF output

`PdfPrintExtensions.PrintToPdf` (package `Meziantou.Framework.WebPlatform.PdfRendering`) prints a document (a
`PrintSession`, as `ImagingPrintExtensions`) and writes each printed page into a page of a `Meziantou.Framework.Pdf`
document. The package is not a `RenderingBackend`: it reads the display lists of the pages, and uses the backend of the
browsing context for the images (their bytes are only kept when the browsing context has a backend) and the surfaces of
that backend (`RenderingBackend.CreateSurface`, an `ImagingRenderingBackend` when the browsing context has none) for what
PDF cannot draw. It uses the internals of the core library, and depends on its exact version; it uses the public API of
the Imaging backend only.

- The page size is the size of the printed page (CSS px, 0.75 point each). The first operator of each page maps the CSS px
  of the display list (y down) to the default user space of the page (points, y up), so the items are written with their
  own coordinates.
- The state items map to the graphics state: a save is `q`, its restore `Q`, a transform `cm`, a clip a path and `W n`. A
  layer is a transparency group (an isolated form XObject drawn with the opacity and the blend mode of the layer in its
  `ExtGState`) when it has an opacity or a blend mode, or when its items blend; it is a `q`/`Q` pair otherwise.
- The colors are DeviceRGB, their alphas `ca`/`CA`, the blend modes of `mix-blend-mode` the 15 blend modes of PDF. The
  linear and radial gradients are shading patterns whose function interpolates their stops; the colors of their transparent
  stops are the colors next to them (PDF interpolates the colors and the alphas separately, CSS premultiplied), the stops of
  a repeating linear gradient are repeated over the shape, and the alphas of stops that differ are a luminosity soft mask.
  The image patterns, the background tiles and the SVG patterns and images (`DisplayListShader`) are tiling patterns whose
  tile is written with the same writer (vector graphics).
- A glyph run is shown with `PdfContentBuilder.ShowGlyphs`: the glyph ids of the font file of the layout at the positions
  the layout computed (the text is not shaped again), with the text of the run (its `ToUnicode` CMap entries, or a
  replacement text for the ligatures and the right-to-left runs). The font is embedded as a subset (`PdfFont.FromFace` of
  the face of the layout). The synthesized bold is a fill and stroke, the synthesized oblique a shear of the text matrix.
  The glyphs of the instances of variable fonts and of the fonts PDF cannot embed are filled as paths, with the text as
  their replacement text.
- An image is embedded once per URL: a JPEG image without EXIF orientation as it is (`DCTDecode`), the other images
  decoded by the backend of the browsing context (their alpha a soft mask).
- What PDF cannot draw is rasterized by a surface of the backend, at `PdfPrintOptions.RasterizationScale` pixels per
  CSS px, in the bounds of the items (or of the clip), and drawn as an image at their place: the blurred paints
  (`BlurSigma`, the shadows), the layers with filters or backdrop filters, composited with a Porter-Duff operator or containing such items
  (the masks), the rest of the scope of a 3D transform, the conic gradients, the filtered shaders, the cropped, padded or
  mirrored image patterns, the repeating radial gradients, and the runs of color glyphs. A backdrop filter sees a
  transparent backdrop.
- The pages are measured before they are written: each item gives the area of its node on its page. The structure tree
  (when `PdfPrintOptions.Tagged`) is built from the elements in the order of the document: the headings, paragraphs,
  lists (`LI` with an `LBody`), tables (the scope and the spans of the cells), figures (`img`, `svg`, `canvas`, with their
  alternate text), `math` (`Formula`), quotations, code, links and sections are structure elements; the other elements
  belong to their parent. The glyphs and images are the content of the structure element of their node, and every drawing
  of a figure; the backgrounds, borders and decorations are artifacts, as the drawings without node (the paper, the
  page-margin boxes), the decorative images (`alt=""`) and the content of `aria-hidden` elements. A transparency group is
  marked as the structure element of its first text or image.
- The links are one link annotation per line of each `a` element with an `href` (a URI, or a destination in the PDF
  document for a fragment of the document), in the `Link` structure element; the headings are the outline, nested by
  level, whose destinations are where they are first painted.
- The nested documents (`iframe`) are painted as on the screen, and their content is an artifact.

## Rendering backends

`Rendering.RenderingBackend` is a public abstract class whose services are internal: the backends are the packages of this
repository, which use `InternalsVisibleTo` (as the script engine packages use the internal hosting primitives) and depend on
the exact version of the core library. The type cannot be derived from outside of them: the display list changes with the
painting features. A backend can be shared by several browsing contexts and must be thread-safe (the workers use it on
their own threads).

| Package | Backend |
| --- | --- |
| `Meziantou.Framework.WebPlatform.Imaging` | `ImagingRenderingBackend`: the drawing API, the fonts and the codecs of [Meziantou.Framework.Imaging](https://www.nuget.org/packages/Meziantou.Framework.Imaging) |

The services of a backend (`Rendering/RenderingBackend.cs`):

- `CreateSurface(width, height, options)` → `RenderingSurface`: a bitmap in device pixels (with or without alpha, sRGB or
  Display P3) that rasterizes display lists (`Draw`), is cleared (`Clear`), reads and writes its pixels in the format of
  `ImageData` (RGBA, not premultiplied: `ReadPixels`/`WritePixels`) and makes immutable snapshots (`CreateSnapshot`). It is
  used for the bitmaps of the 2D canvases (see [Canvas](#canvas)) and for the snapshots of the old state of the
  [view transitions](view-transitions.md#names-and-captures) (the display list of the captured element, rasterized once).
- `CreateImageDecoder(bytes, mimeType)` → `RenderingImageDecoder`: opens a raster image (the format is sniffed; null when
  the backend cannot read it): its size, its frames (`FrameCount`, `GetFrameDuration`, `RepetitionCount`) and
  `DecodeFrame(index, orientation, convertToSrgb)` → `RenderingImage`, the whole canvas of the image at a frame of its
  animation with the EXIF orientation given by the library (`ImageMetadata.GetExifOrientation`) and its colors converted
  to sRGB (or the encoded values for `colorSpaceConversion: "none"`). `DecodeImage(bytes, mimeType, applyOrientation)` is
  its first frame. The SVG images are not decoded by the backend: they are painted by their document (see
  [SVG images](#svg-images)).
- `CreateImage(width, height, rgba)` → `RenderingImage`: an image of pixels in the format of `ImageData` (the bitmaps of
  `ImageBitmap`).
- `EncodeImage(width, height, rgba, mimeType, quality)` → the bytes of a JPEG or WebP file (`toDataURL()`, `toBlob()`,
  `convertToBlob()`); the library encodes the PNG files itself (`Rendering/PngEncoder.cs`), so a type the backend does not
  encode is serialized as PNG. The Imaging backend composites the JPEG images over opaque black and encodes a WebP image
  of quality 1 losslessly, as Chrome.

### Imaging backend

`Meziantou.Framework.WebPlatform.Imaging` (`src/Meziantou.Framework.WebPlatform.Imaging`) draws the display lists with the
drawing API of [Meziantou.Framework.Imaging](https://www.nuget.org/packages/Meziantou.Framework.Imaging) (`DrawingCanvas`),
which also draws the glyphs and decodes and encodes the images: it is its only dependency (the core package does not
reference it). There is no native library: the package is trimming- and AOT-compatible (`IsAotCompatible`; the screenshot
sample is published with Native AOT on CI), and the same document gives the same pixels on every platform.

- **Pixels**: the screenshots and the surfaces that are not canvases (the snapshots of the view transitions, the SVG images
  drawn as images) are images of 16 bits per component (`Rgba64`, straight alpha), and so are the layers they draw: the
  opacity groups, the blend modes and the filters are not rounded to 8 bits between the operations. The pixels are rounded
  to 8 bits once, when they are read (`ReadPixels`, the screenshots: `PixelConversions.ToByte`), to the nearest level with
  a bias of 1/256 of a level, so that a coverage of a half rounded on a layer then on the canvas gives the same level as on
  the canvas directly. The bitmap of a canvas (`RenderingSurfaceOptions.IsCanvasBitmap`) has 8 bits per component, as in
  the browsers, and so has the drawing of a paint worklet (`DisplayListShader.IsCanvasRecording`: a pattern whose tile has
  8-bit samples, `PatternTilePrecision.EightBit`). The colors of the display lists are converted to the color space of the surface (sRGB or
  Display P3) and clipped to its gamut, and a color whose alpha rounds to 0 in 8 bits is transparent.
- **Display lists** (`DisplayListRasterizer`): the state operations of a display list are a stack (save, restore), the ones of
  a `DrawingCanvas` are callbacks: a transform (2D, or 3D with `Transform3D`, combined in three dimensions), a clip (a
  rectangle or a path, antialiased or not) or a layer starts a scope of the canvas whose callback draws the rest of the
  items, up to the restore that ends it (a callback that the drawing API runs twice, the window of a far shadow, draws the
  same items again). The coordinates are brought into the range of the drawing API (16,777,216 pixels; the drawing API
  cuts what a transform sends beyond it), and the curves are flattened into chords within 1/64 device pixel
  (`DrawingCanvas.Flattening`): the shapes that a test and its reference draw as different curves differ less on their
  antialiased edges than with the default of 1/16 pixel. Each group (`SaveLayerItem`) is a layer, composited with its opacity, its operator and
  its blend mode; the opacity applies to the layer before it is composited (`LayerOpacity.Group`: with an operator that is
  not source-over, `plus-lighter` or a mask, it is not a coverage). A group without filter is a layer of
  its bounds when they are known.
- **Filters**: the CSS filter functions and the SVG filters ([Filter Effects](https://drafts.fxtf.org/filter-effects/)) are
  filters of the drawing API (`BlurFilter`, `DropShadowFilter`, `ColorMatrixFilter`, `FilterChain`, and `FilterGraph` with
  its primitives, their subregions, `color-interpolation-filters` and the filter region); a luminance mask is a color matrix
  and a composite `in`. The drawing API filters what is drawn beyond the edges of the canvas within the reach of the filter
  (a blur reads it, a shadow moves it onto the canvas; a shadow cast from farther is computed on a window of its own). The
  backdrop of a backdrop
  filter is the pixels under the border box (`BackdropLayer`), clipped to the rounded border box; the filters of the
  element apply to it after the backdrop filters.
- **Paints**: the colors, the gradients (linear, radial between two circles, conic: the brushes of the drawing API; the
  gradients interpolated without premultiplied alpha, those of the canvases and of SVG, are sampled by more stops), the
  images (`ImageBrush`, with the spread of each axis and the source rectangle of a border image slice) and the patterns
  (`PatternBrush`: the tiles of the backgrounds, the display lists of the SVG images and patterns, the `filter()` images; a
  tile is drawn where the canvas shows it). The operators and the blend modes are those of
  [Compositing and Blending](https://drafts.fxtf.org/compositing-1/) (`lighter` and `plus-lighter` are `CompositeMode.Plus`).
  The blur of a paint (the shadows of the boxes) is a blur filter of its shape. The radii of a rounded rectangle that do not
  fit in it are reduced as the ones of `border-radius`.
- **Images**: a pixel of a scaled down image is the average of the pixels of the image it covers (`ImageSampling.Box`, as
  the box-filtered mipmaps of the browsers), and an enlarged image is interpolated bilinearly; `image-rendering: pixelated`
  takes the nearest pixel, and the high quality of the canvases is bicubic when
  the image is enlarged. The part of the source rectangle of `drawImage()` outside of the image is not drawn (the
  destination is reduced in proportion), and the samples stay in the source rectangle.
- **Strokes**: the pens of the drawing API (joins, caps, miter limit, dashes); a line drawn with a paint that is not a stroke
  is a hairline. A rectangle stroked without width or without height is the stroke of a line with its joins, and nothing is
  drawn for one that has neither (`strokeRect()`).
- **Text**: the backend does no layout and no shaping. A glyph run is drawn by id at the positions of the layout
  (`FillGlyphs`, `StrokeGlyphs`), with a font of Meziantou.Framework.Imaging made from the face the layout measured the text
  with (the two libraries share the font reader of Meziantou.Framework.Fonts, so a font file is read once), per face and
  instance of the variation axes (`FontCache`), and the coverages of the glyphs are cached per backend (`GlyphCache`). On
  the pixel grid, the origin of a glyph is rounded to a quarter of a device pixel along the line and to a whole pixel across
  it, as the glyph caches of the browsers do; the positions of the glyphs of the CSS text are first rounded to 1/4096 px
  (`Painter.QuantizeGlyphOrigin`), so that the same position computed through a computed value (whose lengths have 6
  decimals, e.g. a relative offset set by a script) is rounded to the same quarter. The synthetic bold thickens the
  outline (1/24 of the font size up to 9px, 1/32 from 36px), the synthetic oblique is a shear of the glyphs (the painting
  draws the upright glyphs of vertical text with a vertical skew transform around the center of their column instead, `DisplayFont.SyntheticObliqueAngle` 0), and a
  combined upright text is compressed with the horizontal scale of the run. The glyphs of `COLR` version 0 reach the backend
  as the glyph runs of their layers (see [Color glyphs](#color-glyphs)); the other color glyphs (`COLR` version 1, `sbix`,
  `CBDT`) are drawn by Meziantou.Framework.Imaging with their colors (the first palette of the font), faded by the alpha of
  the color of the text. The glyphs of the Ahem font (`DisplayFont.IsAliased`) are not antialiased and are drawn at
  whole device pixels: see [Pixel snapping](#pixel-snapping).
- **Image decoding** (`ImagingImageDecoder`): the format is sniffed; PNG (including the animated PNG), JPEG (including the
  CMYK images, converted to sRGB with their CMYK profile), GIF, WebP (including the animated WebP), AVIF (including the
  image sequences), BMP (including the run-length encoded ones), ICO and CUR are decoded (the other formats of
  Meziantou.Framework.Imaging, e.g. JPEG XL or HEIC, are rejected: the browsers do not all decode them). Only the header
  and the structure of the frames are read when an image is opened (the frames of an AVIF sequence are read once for
  their durations); the frames are decoded on demand by a forward-only reader (an earlier frame restarts it), composited
  by the library. The colors are converted to sRGB with the embedded ICC profile (the samples of 16 bits before they are
  reduced; as in the browsers, the gamma and the chromaticities of a PNG image without ICC profile are ignored), the EXIF
  orientation given by the core library is applied, and the loop count is read by the core library
  (`ImageMetadata.GetAnimationRepetitionCount`; the number of plays of an AVIF sequence). An invalid animated PNG is
  displayed as its default image, and the chunks of a PNG image whose type is not made of letters or whose checksum is
  wrong are ignored. An incomplete PNG, baseline JPEG or BMP image is decoded up to its last decoded row (the other rows
  are transparent); the incomplete images of the other formats are not decoded (a limitation of
  Meziantou.Framework.Imaging).
- **Image encoding** (`ImagingImageEncoder`): JPEG (quality 0.92 by default, 4:2:0, over opaque black) and WebP (quality
  0.8 by default, lossless at 1); the PNG screenshots are encoded by the core library.
- **Options** (`ImagingRenderingBackendOptions`): `AntiAlias` (true). It does not apply to the glyphs of Ahem.
- **Tests**: `tests/Meziantou.Framework.WebPlatform.Imaging.Tests` tests the surfaces, the drawing of the display list items
  and the codecs on small bitmaps, and compares screenshots of simple documents (Ahem text, colored boxes, borders,
  transforms, images) with the expected PNG files of its `Screenshots` directory, with a tolerance per channel and a number
  of differing pixels. A missing expected file is created; `SCREENSHOT_UPDATE_EXPECTATIONS=1` replaces them, and the
  screenshots that differ are saved in the `screenshots-actual` directory of the test output.

## Images

The images (`img`, the CSS images, the poster frames) are fetched and their header is read by the library (the natural
size, the EXIF orientation of the JPEG images and of the `eXIf` chunk or the EXIF raw profile of the PNG images before
their image data, and their EXIF resolution: the natural size of an image whose density is corrected, see
[Images](browsing-context.md#images), is not the size of its decoded pixels, so the rasterizer scales the source
rectangles and the image shaders, which are in natural pixels, to the decoded pixels). With a backend:

- the decoder of the backend opens a raster image when it is loaded: an image it cannot read (an unsupported format such as
  AVIF, a corrupted header) is broken (`error` event, `complete`, `naturalWidth` 0), as in a user agent that does not
  support it; an image whose data is corrupted after a valid header is available, but its pixels cannot be decoded;
- the pixels are decoded when they are painted, by `img.decode()` (which rejects with an `EncodingError` when they cannot
  be decoded) and by `createImageBitmap()`. `decoding="sync|async"` has no effect (the decoding is synchronous);
- the animated images (GIF, APNG, WebP) are played from the time they became available, on the clock of the event loop
  (the virtual clock of a `FakeTimeProvider` in the tests): the display list shows the frame of the time it is painted and is
  painted again after the next frame (a screenshot shows the current frame). The images of the same URL in the list of
  available images share their animation, as in the browsers. `image-animation` is not implemented.

The bitmaps (`Html/Canvas/`) are pixels in the format of `ImageData` (RGBA bytes, not premultiplied, sRGB), processed by the
library:

- `ImageData` (both constructors, the `rgba-unorm8` and `rgba-float16` formats, the color spaces, serializable); its `data`
  is the array given to the constructor, or an array created once and shared with the .NET code (`ImageData.Data`).
- `createImageBitmap()` (`GlobalScope.CreateImageBitmap`, also in the workers): from an `img` (its default image, with its
  EXIF orientation; an SVG image is rendered at the size of the bitmap: see below), an SVG `image` element (as an `img`),
  a `canvas` (its bitmap), a `Blob`
  (sniffed and decoded by the backend), an `ImageData` or an `ImageBitmap`; the source rectangle (the parts outside of the
  source are transparent black), `resizeWidth`/`resizeHeight` with `resizeQuality` (the nearest pixel for `pixelated`, a
  bilinear interpolation of the premultiplied colors otherwise, the average of the covered pixels when reducing with
  `medium` and `high`), `imageOrientation: "flipY"`, `premultiplyAlpha: "premultiply"` (the precision of the premultiplied
  colors) and `colorSpaceConversion: "none"`. The promise is resolved in a task. An SVG image is rendered by its document
  (`SvgImage.Rasterize`) at the output size (its natural size scaled by the resize options and the source rectangle; an
  image without natural dimensions needs `resizeWidth` and `resizeHeight`), so the resize options scale the vector image
  and not its pixels. The bitmap of a `video` is the frame its media player gives (`MediaPlayer.GetVideoFrame()`, see
  [Media elements](browsing-context.md#media-elements)); a video without frame is an `InvalidStateError`. `VideoFrame` sources are not supported. The pixels of an `ImageData` or of a canvas in another
  color space are converted to sRGB.
- `ImageBitmap` (`close()`, serializable and transferable when it is origin-clean) and `ImageBitmapRenderingContext`
  (`canvas.getContext("bitmaprenderer")`, `transferFromImageBitmap()`): the canvas paints its output bitmap (over black
  with `alpha: false`). The 2D contexts are described in [Canvas](#canvas).

## Canvas

The 2D contexts of the canvases (`Html/Canvas/`): `CanvasRenderingContext2D` (`canvas.getContext("2d")`) and
`OffscreenCanvasRenderingContext2D` (`offscreenCanvas.getContext("2d")`, also in the workers) share
`CanvasRenderingContext2DBase`, which implements all the members of the `CanvasRenderingContext2D` interface of the HTML
Standard. The library implements the drawing state, the paths, the styles, the text layout and the drawing model; the
backend only rasterizes. Without a backend (or with `WebPlatformFeatures.CanvasEnabled` false), there is no 2D context.
The `PaintRenderingContext2D` of the paint worklets is a subset of the same class whose bitmap only records its drawing
operations (see [Paint worklet images](#paint-worklet-images)).

- **Bitmap** (`CanvasBitmap`): a `RenderingSurface` of the size of the canvas (with or without alpha, sRGB or Display P3,
  `willReadFrequently`) and its origin-clean flag. The drawing operations are recorded in a display list, which is
  rasterized on the surface when the pixels are needed (`getImageData()`, drawn into another canvas or into the page,
  serialized), so a sequence of operations is rasterized at once. Setting the `width` or the `height` of the canvas (even to
  its value) and `reset()` replace the bitmap and reset the drawing state and the origin-clean flag.
- **Drawing model** (https://html.spec.whatwg.org/multipage/canvas.html#drawing-model): an operation is a group of the display
  list: the clip region (the paths of `clip()`, in the coordinates of the canvas), the shadow (a layer filtered by a
  shadow-only drop shadow, in the coordinates of the canvas: its offset and its blur are not transformed), the shape in a
  layer composited with `globalCompositeOperation` (the operators that are not `source-over` composite the whole clip
  region) and filtered by `filter` (the CSS filter functions; `url()` references are not supported), and the shape itself
  in the coordinates of the current transformation matrix. `globalAlpha` multiplies the alpha of the paint. As in the
  browsers, nothing is drawn with a non-invertible transformation matrix.
- **Paths** (`CanvasPath`): the current default path is stored in the coordinates of the canvas (its points are transformed
  when they are added), a `Path2D` in the coordinates where it is drawn. The arcs, the ellipses and the corners of
  `roundRect()` are cubic Bézier curves; the angles of `arc()` and `ellipse()` are normalized as Blink does (an end angle 2π
  after the start angle is a full ellipse); `arcTo()`, `rect()` and `roundRect()` follow the steps of the specification; the
  zero-length segments are pruned before a stroke. A `Path2D` is also created from SVG path data. `isPointInPath()` and
  `isPointInStroke()` use the SVG geometry (`SvgShapeGeometry`: the fill rules, the line widths, caps, joins and dashes).
- **Styles**: the colors are CSS colors (`currentcolor` is the color of the connected canvas element, else black),
  serialized as `#rrggbb` or `rgba()` for the legacy sRGB colors and as their CSS serialization otherwise; the gradients
  interpolate unpremultiplied (degenerate gradients paint nothing); the patterns are a snapshot of their image, with their
  repetition and `setTransform()`; `imageSmoothingEnabled` and `imageSmoothingQuality` select the sampling of the images.
- **Text**: `font` is parsed as the CSS `font` shorthand and computed by the style engine (a style of the canvas element
  inheriting from its computed font when it is connected, else from `10px sans-serif`; the variables and the
  element-dependent functions are invalid), and serialized with its size in px and without line height. The text is shaped
  by the managed text shaping with the fonts of the document (its web fonts are loaded when they are needed; a worker has
  the font faces of its font source, `self.fonts`, then the bundled fonts), in the visual order of the bidi algorithm (`direction`, or the direction of the canvas element),
  with `letterSpacing`, `wordSpacing` (resolved against the font), `fontKerning`, `fontStretch`, `textRendering` (the
  ligatures of `optimizeSpeed`), `fontVariantCaps` (the small-capital features of the font, or the synthesized small capitals) and
  `lang` (the language of the OpenType shaping; `inherit` is the language of the canvas element, for an `OffscreenCanvas`
  the language of its placeholder canvas element when its control was transferred, else of the document element of its window). The alignment, the baselines (the `BASE` table of the font, else 80% of the ascent
  for the hanging baseline and the descent for the ideographic one; the em square is the ascent and the descent scaled to the font size), `maxWidth` (a
  horizontal compression of the glyphs) and the metrics of `measureText()` (the advance width, the bounds of the glyph
  outlines, the font and em boxes, the baselines) follow the specification.
- **Images** (`drawImage()`, `createPattern()`): the `img` elements (their default image, with their EXIF orientation unless
  `image-orientation: none`; an image whose source changed is not fully decodable until it is loaded), the canvases and
  the `OffscreenCanvas`es (a snapshot of their bitmap; a canvas without context is transparent black), the placeholder
  canvases (the last frame of their `OffscreenCanvas`), the `ImageBitmap`s and the SVG `image` elements (as the `img`
  elements; a broken image throws). An SVG image is drawn as vectors: the display list of its document laid out at its
  concrete object size (its natural size, or the size of the canvas as the default object size) mapped from the source
  rectangle to the destination rectangle; a pattern rasterizes it at that size. A `video` draws the frame of its media player
  (`MediaPlayer.GetVideoFrame()`) at the natural size of the video, and nothing while its ready state is `HAVE_NOTHING` or
  `HAVE_METADATA` or when the player gives no frame. An image of another origin (also through a redirect) taints the canvas:
  `getImageData()`, `toDataURL()`, `toBlob()` and `convertToBlob()` then throw a `SecurityError`.
- **Pixels**: `getImageData()`, `putImageData()` (not composited, not clipped, not transformed) and `createImageData()`,
  with the conversion between the color space of the canvas and the one of the image data (`srgb`, `display-p3` and their
  linear variants) and the `rgba-float16` format. The bitmaps have 8-bit components, premultiplied by the backend (a
  `float16` canvas stores 8-bit components).
- **Serialization**: `toDataURL()`, `toBlob()` and `OffscreenCanvas.convertToBlob()` serialize the bitmap (converted to sRGB)
  as PNG (encoded by the library), JPEG or WebP (encoded by the backend, with their quality); another type is PNG.
- **OffscreenCanvas**: its 2D or `bitmaprenderer` context, `transferToImageBitmap()`, `convertToBlob()`, its transfer to a
  worker (a canvas with a context cannot be transferred), and `canvas.transferControlToOffscreen()`: the canvas element
  becomes its placeholder (its `getContext()` and the setters of its `width` and `height` throw an `InvalidStateError`). The
  `OffscreenCanvas` commits its frame at the end of the task that drew it: the frame is given to the placeholder in a task
  of the event loop of the placeholder (its `width` and `height` attributes take the size of the frame), which paints it and
  draws it into the other canvases. The direction of the texts of a transferred `OffscreenCanvas` is the direction of its
  placeholder element when its control was transferred.
- **Painting**: `Painter.PaintCanvas` draws the snapshot of the bitmap (`CanvasBitmap.GetSnapshot()`, cached until the
  bitmap changes) in the box of the canvas with `object-fit` and `image-rendering`; each drawing operation repaints the
  document.
- `drawFocusIfNeeded()` strokes the path with a 2px ring when the element is focused and is a descendant of the canvas.
- Not implemented: the canvas layers (`beginLayer()`, `endLayer()`, a proposal that the HTML Standard does not define yet),
  `CanvasFilter` objects, the HDR settings (`toneMapping`), the 16-bit
  storage of the `float16` canvases, the wide-gamut colors of the images (decoded to sRGB),
  `captureStream()`, the text clusters of `TextMetrics`, HTML-in-canvas (`drawElementImage()`), WebGL and WebGPU.

## Display list

A `DisplayList` (`Rendering/DisplayList.cs`) is an immutable sequence of operations in CSS px:

- state: `save`, `save-layer` (an offscreen group composited with an opacity, a blend mode and filters: blur, drop shadow,
  color matrix, luminance to alpha, the graph of the primitives of an SVG filter (`Rendering/FilterPrimitives.cs`); a layer can
  start with its backdrop filtered and clipped to a rounded rectangle: `backdrop-filter`), `restore`, `transform` (2D affine
  or 3D 4x4), `clip-rect`, `clip-rounded-rect`, `clip-path` (antialiased, or `aliased`: the pixels whose center is in the
  path);
- drawing: `draw-paint` (fills the clip), `draw-rect`, `draw-rounded-rect`, `draw-ring` (the area between two rounded
  rectangles: a border), `draw-path` (subpaths of lines and Bézier curves with a fill rule), `draw-line`, `draw-glyphs`,
  `draw-image`.

The rounded rectangles of the display list have elliptical corners. The boxes whose corners are shaped by `corner-shape`
([CSS Borders 4](https://drafts.csswg.org/css-borders-4/#corner-shape-rendering)) are painted with paths: the
`clip-rounded-rect`, `draw-rounded-rect` and `draw-ring` operations of such a box are replaced by `clip-path` and
`draw-path` operations when they are added to the list (`DisplayListBuilder.Add`), so the backends do not know the corner
shapes. The path is the border contour path of the specification for the edge that is painted (`CornerContour`): the
rectangle of the edge (the border edge, the padding edge, the edge of a shadow or of an outline, the overflow clip edge...)
from which the area of each corner is clipped out. The curve of a corner is offset from the curve of the border edge by the
insets of its two sides, moved along the normals of the curve: a superellipse (`squircle`, `superellipse(K)` with K > 0 or
K ≤ -1; `scoop` is concave), the quarter of an ellipse tangent to the two tangents (-1 < K < 0), a straight line (`bevel`)
or the two sides of the corner area (`notch`); the curves are flattened into polygons. A `round` corner is the usual quarter
of an ellipse and a `square` corner has no radius, so a box with only these shapes is a rounded rectangle. When the corner
areas of an edge overlap (two scoops of 50% with a border, a notch deeper than half of the box), their union is clipped out:
the edge is the intersection of the clips of the outside of each corner area, and the area outside of an edge (the inside
of a ring, the area a shadow is clipped to) is a path filled with the non-zero rule. The outsets use the outset-adjusted
border radius (shadows, `overflow-clip-margin`, the margin box of `clip-path`), except the outline, which stays at the
distance of `outline-offset` from the curves.

The borders shaped by `border-shape` ([CSS Borders 4](https://drafts.csswg.org/css-borders-4/#border-shape)) are painted
with the same operations (`BorderShapeGeometry`, see [the layout](layout.md#painting-csslayoutpainting)): the stroke of a
shape is a stroked `draw-path` (a `draw-ring` for a rectangle with circular corners or a circle), the area between two
shapes is the outside of the inner shape clipped to the outer one (a `draw-ring` between two rounded rectangles), and the
edges of the border that the shadows, the outline and the clips follow are paths computed by the library
(`PathOperations`: the area of a stroke, the union and the difference of two areas as a path of trapezoids), or rounded
rectangles when the shape is one.

A `DisplayPaint` is a color (sRGB, not premultiplied) or a shader (linear, radial and conic gradients, image
patterns, the drawing of a display list (an SVG image, the tile of an SVG pattern), and the tiled patterns of any shader with
a spacing between the tiles: the background layers and the border images), filled or stroked (width, caps, joins, miter limit, dashes), with a blend mode (the Porter-Duff operators of the
canvas and the blend modes of CSS) and an optional blur of the coverage (the shadows). Each drawing operation knows the node
it paints (`Source`), for the dumps and the paint timing.

The CSS colors are [CSS gamut mapped](https://drafts.csswg.org/css-color-4/#css-gamut-mapping) to the gamut of the surface
they are painted on when they become display colors (`DisplayColor.FromCss`, the sampled colors of the CSS gradients): sRGB
for the documents, sRGB or Display P3 for a canvas (its components are then extended sRGB). The library uses the Ray Trace
algorithm (`CssColorConversion.GamutMap`): the OkLCh chroma is reduced at constant lightness and hue, and a lightness of at
least 100% is white and of at most 0% black. The browsers clip each component instead: the tests written for clipping (e.g.
`xyz-d50-004`, `color-mix-non-srgb-001`) differ by one level of a component. The legacy sRGB colors (`rgb()`, `hsl()` and
`hwb()`) are 8-bit colors whose components are clamped (`hsl(120, 200%, 50%)` is lime), as their serialization and as in
the browsers. The images are not gamut mapped (they are decoded to sRGB, see the limitations of the canvas).

The glyphs are drawn by id at the positions computed by the managed text shaping, with the font file the layout measured
(`DisplayFont`: the sfnt bytes of `FontFile.Data` after the WOFF/WOFF2 decoding, the face index, the size, the values of
the variation axes, the synthetic bold and oblique): the backend does no layout and no shaping, so the painting uses exactly
the metrics of the layout. Vertical and rotated texts are runs drawn in a rotated coordinate system.

`DisplayList.Dump()` returns a text representation (one operation per line, indented by the saves), used by the unit tests:

```
draw-paint fill #ffffff
save
  save
    save
      draw-rect (0, 0, 54x24) fill #ff0000  [div#a]
      draw-ring (0, 0, 54x24) - (2, 2, 50x20) fill #0000ff  [div#a]
      draw-glyphs "ab" Ahem 10px at (2, 10) glyphs=2 fill #000000  [#text]
    restore
  restore
restore
```

## Painting

The painting of a document (`Css/Layout/Painting/`, see [layout.md](layout.md#painting-csslayoutpainting)) is lazy: the
display list of the viewport is built when something asks for it (a screenshot), and built again only when the layout
(the styles, including the paint-only properties, the DOM, the loaded images) or a scroll position changed. It follows the
painting order of [CSS 2 Appendix E](https://drafts.csswg.org/css2/#elaborate-stacking-contexts): the stacking contexts
by `z-index`, the blocks, the floats, the inline content, the positioned boxes (in tree order), with the outlines of a
stacking context or of a positioned box after its in-flow content and before the positioned boxes that follow (as in Blink and
WebKit; Firefox paints them at the end of the stacking context, the last step of Appendix E), then the top layer, then the view transition layer (the `::view-transition` pseudo-element tree of
an animating [view transition](view-transitions.md), whose images are the snapshots of the old state rasterized by the
backend and the live paintings of the new state; the captured elements are not painted in the document meanwhile). The
pseudo-element tree of a [scoped view transition](view-transitions.md#scoped-view-transitions) is a stacking context
painted right after the stacking context of its root element, in a group that has the clips of the ancestors and the
transform of the root element, without its effects nor its overflow clip; while the rendering of the root element is
suppressed, the display list of its old state is drawn in such a group in place of its stacking context. A stacking context is a group of the display list with its transform (with the
`perspective` of its parent) and its effects, in the order of [Filter Effects](https://drafts.fxtf.org/filter-effects/#placement):
its filters (the CSS filter functions and the SVG filters of `url()`), its clip (`clip`, `clip-path`), its masks, then its
opacity and its blend mode (`mix-blend-mode`, `isolation`; the stacking contexts that contain a blending stacking context are
isolated groups); the overflow clips follow the containing block chain of each box. The fragments of an inline box split
over several lines are one group when its only effects are the ones that composite it (`opacity`, `mix-blend-mode`,
`isolation`: the parts of two fragments that overlap are not composited twice; its filters, clip paths and masks are
applied per fragment), and the floats of an inline box that establishes a stacking context are painted in it. An SVG
image that contains blended groups is an isolated group. The 3D rendering contexts
(`transform-style: preserve-3d`) are painted as planes with their accumulated 3D transforms, sorted from the farthest (the
planes that intersect are split, as the compositors of the browsers do), without the planes that show their back face with
`backface-visibility: hidden`.
The canvas is drawn over the `Canvas` color of the color scheme of the root element for a top-level document (white, or
dark with `color-scheme: dark`; the `Canvas` color of the palette in [forced colors mode](#forced-colors-mode)).

Painted: the canvas background (root or body), the backgrounds (the colors and the layers of images and gradients with
their sizes, positions, repetitions, origins, clips including `text` and `border-area`, `fixed` and `local` attachments and
blend modes; the gradients with their interpolation color spaces, missing components and transition hints; `image-set()`
with the resolution of its option and `cross-fade()`; the backgrounds of the table rows,
row groups, columns and column groups in their cells (positioned in the row, the row group, the column or the column group and
painted in the whole border box of the cell, also the part of a cell that spans other rows or columns): with the table, or with the row or the row group itself when it is a
layer of the painting order, i.e. positioned or a stacking context, so that its background is above what is painted before
it, as in the browsers), the borders (all the styles, the dashes of the browsers, the border
images), the collapsed borders of the tables (painted with the backgrounds of the blocks, after the backgrounds and the borders of the
block-level descendants of the table, so they cover the borders of the blocks of the cells and the collapsed borders of the nested tables: [csswg-drafts#11570](https://github.com/w3c/csswg-drafts/issues/11570);
a grid line inside of a spanning cell that is the edge of no cell does not interrupt the borders of that cell), the box shadows, the outlines, the gap decorations (the column and row rules of the grid, flex and multi-column containers), the texts (horizontal,
right-to-left, vertical, upright and combined upright, with their shadows, decorations with ink skipping, emphasis marks,
strokes, selection and custom highlights, and the color glyphs: see [Color glyphs](#color-glyphs)), the images
(`object-fit`, `object-position`, `object-view-box`,
`image-rendering`, `image-orientation`, the frames of the animated images; the raster and the SVG images; the images of
the `embed` and `object` elements, and their SVG documents in the rectangle of `object-fit`), the bitmaps of the `bitmaprenderer` canvases, the bitmaps of the 2D
canvases and of the placeholders of the `OffscreenCanvas`es, the images of the generated content and of the list markers,
the video posters and the frames of the videos (the frame the media player of the element gives, `MediaPlayer.GetVideoFrame()`:
the library decodes no video; a `video` paints its poster while it represents it, i.e. until it has video data or while it
is paused at its start, else the frame, in the rectangle of `object-fit`, and nothing without a media backend), the cues
of the text tracks over the videos (see [WebVTT cues](#webvtt-cues)), the documents of the iframes, frames, objects and embeds, a simple native appearance of the form controls
with their values (the value, the placeholder and the selection of the text fields and of the `textarea` elements are the
lines of their inner editor, laid out by the inline layout and scrolled by the control; the rows of the list boxes, the
parts of the slider-like controls with primitive appearance and the button of the file upload controls are boxes too, see
[layout.md](layout.md#replaced-elements-csslayoutreplaced)), the MathML rules and stretched glyphs, and the SVG content: the shapes (fills, strokes with their dashes,
`pathLength` and `vector-effect: non-scaling-stroke`, `paint-order`, `shape-rendering`), the paint servers (linear and
radial gradients, patterns, `context-fill` and `context-stroke`, with the attributes inherited through `href`), the markers,
the texts (with their decorations, `text-shadow`, `textPath`, rotations, the glyphs stretched by
`lengthAdjust="spacingAndGlyphs"`, the bidirectional reordering, the lines of the texts wrapped by `inline-size`, the
selection and the highlights), the `image` elements, the `use` elements (their shadow trees are painted with the styles
of the element instances; the elements of external documents are painted from their resource document), the nested
viewports with their clip, the outlines, the conditional processing (`switch`, `requiredExtensions`, `systemLanguage`) and
the `foreignObject` contents, isolated groups for the blend modes (see [docs/layout.md](layout.md#svg-painting)).

The effects: the 2D and 3D transforms (`perspective`, `perspective-origin`, `transform-style: preserve-3d`,
`backface-visibility`), `opacity`, `mix-blend-mode`, `isolation`, `background-blend-mode`, `filter` (the filter functions
and the SVG filter elements: `feFlood`, `feGaussianBlur`, `feOffset`, `feDropShadow`, `feColorMatrix`,
`feComponentTransfer`, `feComposite`, `feBlend`, `feMerge`, `feMorphology`, `feTurbulence`, `feTile`, `feConvolveMatrix`,
`feDisplacementMap`, `feDiffuseLighting`, `feSpecularLighting`, in sRGB or linearRGB), `backdrop-filter`, the `filter()` image
function, `clip`, `clip-path` (the basic shapes `inset()`, `rect()`, `xywh()`, `circle()`, `ellipse()`, `polygon()`, `path()`,
`shape()`, the geometry boxes and the `clipPath` elements), the masks (`mask-image` with images, gradients and `mask`
elements, `mask-mode`, `mask-repeat`, `mask-position`, `mask-size`, `mask-origin`, `mask-clip`, `mask-composite`,
`mask-border`, `mask-type`), also on the SVG elements, and the effects of the root element on the background of the canvas.
`will-change` has no visual effect: it creates the stacking contexts and the containing blocks of the properties it names
(as the animations that are current or in effect do for the properties they target), and `background-attachment: fixed`
behaves as `scroll` in a box that is transformed or names a transform property in `will-change`.
The boxes are transformed by their offset path too (`offset-path`, `offset-distance`, `offset-rotate`, `offset-anchor`,
`offset-position`: see [layout.md](layout.md)), and the checkboxes and radio buttons that are not checked have the `Field`
color of their color scheme.

Not painted yet: the `BackgroundImage` inputs of the SVG filters; the emphasis marks of vertical text and `element()`;
the wrapping of the SVG texts in the shapes of `shape-inside`. The SVG content is painted
with the animated values of the [SVG animations](animations.md#svg-animations-smil) (SMIL) at the time of the frame; an SVG
image is painted at the begin of its timeline.

### Color glyphs

The color glyphs of a font (the layers of its `COLR` table, version 0, see [layout.md](layout.md#color-fonts)) are
resolved by the painting, not by the rendering backend: a glyph run that has color glyphs is split
(`FontPalette.SplitColorGlyphs`) into glyph runs of the same font, in the order of the glyphs. A color glyph is replaced
with the glyphs of its layers at its position, from the bottom layer to the top one, each one in a glyph run painted with
the color of its entry in the palette of the element (`font-palette`, `@font-palette-values`), or with the paint of the
text for the layers in the color of the text; the consecutive glyphs that have the same paint are one glyph run, and the
first glyph run has the text of the run (for the dumps and the paint timing). So a backend only draws ordinary glyph runs
with a solid color (the base glyph of a color glyph is never drawn), and must not draw the color glyphs of a font by
itself.

- The fill of the text (`-webkit-text-fill-color`, the color of a highlight, the `fill` of an SVG text, the fill style of a
  canvas) paints the layers with the colors of the palette, multiplied by the alpha of the fill (a transparent text has no
  color glyph, as in the browsers).
- A shadow (`text-shadow`) and the mask of `background-clip: text` paint the layers with their own paint: the silhouette of
  the glyph (layers that overlap are composited one over the other with a translucent shadow color).
- A stroke (`-webkit-text-stroke`, the `stroke` of an SVG text, `strokeText()`) does not draw the color glyphs, as in the
  browsers.

The color glyphs are painted in the text fragments (with their emphasis marks), the SVG texts and the texts of the 2D
canvases (with the default palette of the font: a canvas has no `font-palette`). They are not painted in the texts drawn by
the native appearance of the form controls nor in the MathML stretched glyphs. The paint graphs of `COLR` version 1 and
the bitmap and SVG color glyphs are not resolved by the painting: their glyphs are drawn as the other glyphs are (the
Imaging backend draws the `COLR` version 1 and the bitmap glyphs with their colors, and the SVG glyphs as their outlines).

### Caret

The text insertion caret ([CSS UI 4 §6](https://drafts.csswg.org/css-ui-4/#insertion-caret), `PaintedCaret`,
`Painter.Caret.cs`) is painted in the document that contains the focused area of the top-level browsing context, when it has
the system focus (`BrowsingContext.HasSystemFocus`):

- **Where**: in the value of the focused text control (a `textarea` or a text field that can be edited: as in Chrome, a
  read-only control has no caret) whose selection is collapsed; else at the focus of the collapsed selection of the
  document when it is in editable content and has the focus. Chrome and Firefox move the focus to the editing host of a
  selection set by a script, and display the caret while the focused element is in the editable content of the selection;
  the library does not move the focus (the Selection API does not specify it; WPT `selection/contenteditable/collapse.html`)
  but displays the caret as they do (`Selection.HasEditingFocus`): from the time the selection is set, or its editing host
  is focused, until the focused area of the document changes to something else (`blur()` hides the caret). A selection
  that is a range is highlighted (`::selection`): it has no caret.
- **Geometry** (the one of Blink): the position along the line is the edge of the character, atomic inline or line break
  the caret is attached to (the caret positions of `Selection.modify()`: bidirectional text, vertical writing modes, the
  affinity at the end of a wrapped line), and the caret is as tall as the fragment of that content (the ascent and descent
  of the font of a text). A block container without line box (an empty editing host, which is as tall as a line, see
  [layout.md](layout.md)) has the caret of its first line: at the position given by `direction`, `text-align` and
  `text-indent`, as tall as its font, centered in the height of a line. The caret is kept in the padding box of its block
  container (or in its line when it overflows the container): a caret at the end of a line that fills its container is
  not clipped.
- **`caret-shape`**: `bar` (and `auto`) is a bar of 1 CSS px at the insertion point; `block` covers the character after
  the insertion point (its typographic character unit), `underscore` is a line of 1 CSS px under it (the
  [line-under](https://drafts.csswg.org/css-writing-modes-4/#line-under) side); at the end of a line, before an atomic
  inline or in an empty line, they are 1ch long, after the insertion point.
- **`caret-color`**: the first color is the color of the caret (`auto` is `currentcolor`), the second one the color of the
  character under a block caret (`auto`, the default: the color of the text). The specification lets the user agents
  "adjust the color of the caret to ensure good visibility and contrast": as in Chrome, an `auto` block caret over a
  character that keeps its color is half transparent.
- **Painting order**: the caret is painted with the inline content of its block container (in its stacking context, with
  its clips and its transforms), after the texts of its line. A block caret that covers a character is an overlay of its
  text fragment, painted as the background of a highlight: under the glyphs, which are painted once, in the second color
  of `caret-color` when it has one.
- **`caret-animation`** and the options of the host: `ViewportOptions.CaretVisible` (true by default) paints the caret or
  not; `ViewportOptions.CaretBlinkInterval` is the time during which a blinking caret (`caret-animation: auto`) is
  displayed, then hidden. The default is zero: the caret does not blink, so that a screenshot does not depend on the time
  it is taken at (the test configurations of the browsers disable the blinking too; set 500 ms for the blinking of the
  browsers). The blinking is a state of the document toggled by a timer of the event loop
  (`BrowsingContextOptions.TimeProvider`), never a function of the time of the painting: it starts when the caret is
  painted, starts again (caret displayed) when the caret moves or the focus changes, and stops, the caret being displayed,
  when the document was not painted during a phase (no timer is left behind a document that is no longer painted).
  `caret-animation: manual` never blinks.

A change of the selection, of the selection of a text control, of the focused area or of the system focus paints the
document again (`LayoutEngine.OnCaretChanged`). Not done: the caret of the selection of a document that is not focused
(caret browsing), the slanted caret of the italic texts, the caret of the SVG texts.

### Scrollbars

The classic scrollbars ([CSS Overflow 3 §3](https://drafts.csswg.org/css-overflow-3/#scrollbar-layout),
[CSS Scrollbars 1](https://drafts.csswg.org/css-scrollbars-1/); `ScrollbarGeometry`, `Painter.Scrollbars.cs`) are painted
in the gutters that the layout reserves for them (see [layout.md](layout.md)), and react to the pointer (see
[User input](browsing-context.md#user-input)).

- **Look**: the same on every platform, without native theme. A scrollbar is a track as long as the scrollport, without
  buttons, and a rectangular thumb inset by 2px from the long edges of the track; the scroll corner (where the two
  scrollbars meet) has the color of the track. The thumb is as long as the track multiplied by the ratio of the scrollport
  to the scrolling area (at least 16px), and its position follows the scroll position; a box that cannot be scrolled in an
  axis (`overflow: scroll` without overflow) has a track without thumb. A gutter of `scrollbar-gutter: stable` without
  scrollbar is empty.
- **`scrollbar-color`**: the color of the thumb, then of the track (`transparent` paints nothing: the background of the
  box shows). `auto`: a thumb `#C1C1C1` on a track `#F1F1F1`; `#686868` on `#2C2C2C` when the used color scheme of the box
  is dark. **`scrollbar-width`**: `auto` is `ViewportOptions.ScrollbarThickness` (15px by default), `thin` 11px, `none` no
  scrollbar (the box still scrolls). In forced colors mode `scrollbar-color` computes to `auto`.
- **`::-webkit-scrollbar`** and the other non-standard scrollbar pseudo-elements (`::-webkit-scrollbar-thumb`,
  `::-webkit-scrollbar-track`, `::-webkit-scrollbar-corner`...) are not supported: their rules are ignored. CSS Scrollbars 1
  only says that `scrollbar-color` and `scrollbar-width`, when they are not `auto`, win over them; the standard properties
  are the way to style the scrollbars (a page that hides its scrollbars with `::-webkit-scrollbar { display: none }` only
  keeps them).
- **Where**: the scrollbars of a scroll container are painted with its box, over its background and its borders (its
  contents are clipped to its padding box, which excludes the gutters); the vertical one on the right (on the left in a
  horizontal writing mode with `direction: rtl`), the horizontal one at the bottom. The scrollbars of a `textarea` and of
  the contents of a `fieldset` are painted the same way. The scrollbars of the viewport are painted over the document,
  with the style of the root element (also for the documents of the nested browsing contexts, unless the `scrolling`
  attribute of their `iframe` or `frame` is `no`).
- **The viewport**: the display list of a document (and a screenshot) is its whole viewport, with the gutters of its
  scrollbars: `ViewportOptions.Width` x `Height`, where the scrollport is at its place (after a gutter on the left or at
  the top, e.g. `scrollbar-gutter: stable both-edges`); the canvas (its background) extends under the gutters and the
  document is clipped to the scrollport. The coordinates of the display list are the client coordinates of the document
  (`getBoundingClientRect()`, the positions of `BrowsingContext.Input`).
- **`ViewportOptions.ScrollbarsVisible`** (true by default): when it is false, the scrollbars take their space but are
  neither painted nor interactive (their gutters show the background of the box, or the canvas). With a
  `ScrollbarThickness` of 0 (overlay scrollbars) there is no gutter and nothing is painted.

Not done: the buttons, the hover and pressed states, the overlay scrollbars that appear while scrolling, the painting of the
resizer of `resize` (it is interactive, see [the user input](browsing-context.md#user-input)); the scrollbars of a nested
document are scaled by the zoom of its container (the browsers keep their thickness).

### Forced colors mode

In [forced colors mode](computed-style.md#forced-colors-mode) (`ViewportOptions.ForcedColors`), the painting uses the used
values of the colors, which are forced to the system colors of the palette for the elements whose `forced-color-adjust` is
`auto`: the text, the backgrounds (with the alpha of the original color), the borders, the outlines, the text decorations
and emphasis marks, the column and row rules, the SVG paints of the content that opts in, and the highlights. The shadows
and the gradients of `background-image` are removed by the computed values; a `url()` background image is kept.

- **Canvas**: the canvas is drawn over the `Canvas` color of the palette. The `forced-color-adjust` of the root element is
  propagated to the viewport, not the one of the body element: the background color propagated from the root or the body
  element is forced when the colors of the root element are.
- **Backplates**: the user agent "may also enforce a backplate underneath text [...] to ensure adequate contrast for
  readability", e.g. over a background image. As in Chromium, each block container whose `forced-color-adjust` is `auto`
  and that is visible paints an opaque rectangle behind the text of its lines: the bounding box of the text fragments of
  each paragraph (the text of its inline boxes too, whatever their `forced-color-adjust`, but not the atomic inlines; two
  consecutive forced line breaks (`br`) end a paragraph, and so does the end of a column), above the backgrounds of the
  blocks and below the floats and the inline content. Its color is the system color the background of the block container
  is forced to (`Canvas`, `ButtonFace` in a button...). The value and the placeholder of a text control are the text of
  anonymous boxes of the control (see [layout.md](layout.md#replaced-elements-csslayoutreplaced)): their backplates have the color of the
  background of the control (`Field`).
- As in Chromium, the inline boxes whose colors are forced do not paint their background images, which would cover the
  backplate (the atomic inlines and the blocks do).
- The colors of the native appearance of the form controls (the frames, the tracks, the check marks) are not forced.

### Visited links

With a history of visited links (`BrowsingContextOptions.VisitedLinks`, none by default), the painting uses the colors of
the visited style of the elements that are in a visited link, with the alpha of the colors of their computed style: see
[Visited links](computed-style.md#visited-links). The layout and the display list items are the same as for an unvisited
link; only their colors differ.

### SVG images

An SVG image (an `img` element, a CSS image such as a background, a border image or a mask, or an SVG `image` element whose
resource is `image/svg+xml`) is not decoded by the backend: its document is parsed once into an isolated document without
browsing context (no script runs; only the `data:` URLs of its own images are loaded), laid out in a viewport of the size the
image is drawn at (the concrete object size), with the `prefers-color-scheme` of the used color scheme of the element that uses
it, and painted by the same painting into a display list that is drawn into the display list of the page (or used as a
picture shader for the tiled backgrounds). As in the browsers, an image with a natural width and height but without
`viewBox` is scaled to the size it is drawn at. The fragment of the URL of the image selects a view of the document
(`svgView(viewBox(...);preserveAspectRatio(...);transform(...))`, the id of a `view` element, or a spatial media fragment
`xywh=x,y,w,h`): it overrides the `viewBox` and the `preserveAspectRatio` of the root element. The image of a replaced element is laid out at its size before the
[pixel snapping](#pixel-snapping) and scaled to the snapped rectangle, so that an image whose size has the ratio of its
`viewBox` fills its rectangle; the strokes of `vector-effect: non-scaling-stroke` of the image are multiplied by the zoom of
the element that draws it. The document of an iframe, frame, object or embed element is scaled by the effective zoom of the
element (its viewport is the content box divided by the zoom).

### Paint worklet images

The `paint()` images of the [CSS Painting API](https://drafts.css-houdini.org/css-paint-api-1/) are drawn by the classes
that the modules of the paint worklet register (`CSS.paintWorklet.addModule()`, see [Worklets](scripting.md#worklets)):

```js
registerPaint('checkerboard', class {
  static get inputProperties() { return ['--size']; }      // the properties the image depends on
  static get inputArguments() { return ['<color>']; }       // the syntax of the arguments of paint(checkerboard, red)
  static get contextOptions() { return { alpha: true }; }
  paint(ctx, size, properties, args) { /* ctx is a PaintRenderingContext2D */ }
});
```

- **`paint( <ident> [, <declaration-value>? ]? )`** is an `<image>`: it is valid in every property that takes an image
  (`background-image`, `border-image-source`, `mask-image`, `list-style-image`, `content`, `shape-outside`, the registered
  custom properties...). Its computed value is the function as specified (its name is not a color, and its arguments
  are only matched against the `inputArguments` of the class when the image is drawn). It has no natural dimensions: a
  list marker is drawn at 1em x 1em, an image of `content` at the default object size. The specification treats it as an
  invalid image in `cursor`; here the grammar of `cursor` only takes URLs, so the declaration is invalid.
- **Registration** (`PaintWorkletGlobalScope.RegisterPaint`): the algorithm of the specification (the validation of the
  name, of the `<syntax>` of the input arguments, of the constructor and of its `paint` function; the input properties
  are filtered to the supported properties and the custom properties). The document keeps a document paint definition
  per name, registered by a task: a paint that the global scopes register with different input properties, argument
  syntaxes or context options is invalid (an error is logged to `ConsoleMessage`).
- **Drawing** ([draw a paint image](https://drafts.css-houdini.org/css-paint-api-1/#draw-a-paint-image)): when the display
  list is painted, the paint callback is invoked synchronously for the concrete object size of the image (e.g. the size
  of a background tile), in one of the two global scopes (the same one for 8 paints in a row), with a
  `PaintRenderingContext2D`, a `PaintSize` in CSS pixels (the size divided by the zoom of the element), a
  `StylePropertyMapReadOnly` of the computed values of the input properties (a snapshot; a registered custom property is
  reified with its syntax, a custom property without value is an empty `CSSUnparsedValue`, as in Chromium) and the
  arguments as `CSSStyleValue` objects (computed with their syntax). The instance of the class is constructed once per
  global scope. The microtasks of the callback run before the image is taken (an `async` paint function can draw after
  an `await` that settles without a task).
- **`PaintRenderingContext2D`** is the 2D context of the canvases (`CanvasRenderingContext2DBase`) without the text, the
  pixel manipulation and the user interface members. Its output bitmap has no pixels: the drawing operations are recorded
  in a display list, used as a picture shader where the image is drawn (scaled by the zoom), so the image is rasterized
  at the resolution of the device. A context without alpha (`contextOptions.alpha` false) is opaque black where nothing
  is drawn. `drawImage()` accepts a `CSSImageValue` of the style map (the image of a URL fetched for the styles of the
  document). As Chromium (the only implementation), and unlike the IDL of the specifications, `Path2D` is exposed in the
  paint worklets and the context has the `filter` attribute.
- **Caching and invalidation**: the recorded image of a paint is cached by name, size, device pixel ratio, values of the
  input properties and arguments (256 images per document): the callback is invoked again only when one of them changes
  (e.g. at each frame of an animation of a registered input property). The registration of a paint lays out and paints
  the document again when it has `paint()` images.
- **Invalid images**: a paint that is not registered (yet) or invalid, arguments that do not match the registered syntaxes,
  an exception of the constructor (the class is then never constructed again) or of the paint function (reported to
  `UnhandledScriptError`), a document without script engine or without rendering backend, and
  `WebPlatformFeatures.PaintWorkletEnabled` false: nothing is painted for the image (the next layer or the background
  color shows).

Not implemented: the paint callback is not invoked for the images that are not painted (there is no speculative paint),
and the `filter` lengths of the context that are relative to a font or to the viewport are resolved with the default font
of the canvases (`10px`) and the viewport of an empty document (Chromium uses a 16px font and a viewport of size zero).

### WebVTT cues

The cues of the text tracks of a `video` element (see [Text tracks and WebVTT](browsing-context.md#text-tracks-and-webvtt))
are rendered over the video as the [rendering rules of WebVTT](https://w3c.github.io/webvtt/#rendering) specify, by the
layout engine of the library:

- **The boxes**: the active cues of the tracks whose mode is `showing` and whose kind is subtitles or captions are
  rendered (never over an `audio` element). The tree of WebVTT node objects of a cue is the document tree of its boxes:
  the root of a cue is a block container whose children are wrapped in an anonymous inline box, the WebVTT cue background
  box; an internal node object (`<b>`, `<i>`, `<u>`, `<c>`, `<v>`, `<lang>`, `<ruby>`, `<rt>`) is an inline box (a ruby
  container, a ruby annotation). The text is laid out by the inline layout (line breaking, bidi, ruby, vertical writing
  modes, `text-combine-upright`).
- **The styles**: the properties of [Applying CSS properties to WebVTT Node Objects](https://w3c.github.io/webvtt/#applying-css-properties)
  are the user agent styles of the boxes (`unicode-bidi: plaintext`, `overflow-wrap: break-word`, `text-wrap: balance`,
  `white-space: pre-line`, `font: 5vh sans-serif` and white text where vh is the height of the video, the
  `rgba(0, 0, 0, 0.8)` background of the cue background box and of the ruby annotations, the italic, bold and underline
  objects, the [default classes](https://w3c.github.io/webvtt/#default-classes) such as `.yellow` and `.bg_blue`). The
  inherited properties are inherited from the video element. The `::cue`, `::cue(selector)` and `::cue-region`
  pseudo-elements of the video element apply the properties that the specification allows (see
  [selectors.md](selectors.md#cue-pseudo-elements)); the rules of the `STYLE` blocks of the WebVTT file apply to the cues
  of their track, after the rules of the document in the order of appearance, and their URLs must be `data:` URLs. The
  images of the backgrounds are fetched when the cues are rendered.
- **The positions** ([processing cue settings](https://w3c.github.io/webvtt/#processing-cue-settings)): the video viewport
  is the content box of the element. The size of a cue box in the axis of its writing direction and its position come
  from the `size`, `position` and `align` settings of the cue (percentages of the video viewport). A cue that snaps to
  lines is placed at its line, counted in line boxes from the start of the viewport or from its end for a negative line
  (`auto` is the last line: the bottom of the video), then moved line by line until it does not overlap the cues that
  are already placed and is within the viewport (in the other direction when it reaches the edge; a cue that fits
  nowhere is not displayed). A cue that does not snap to lines is placed at its percentage, aligned by its line
  alignment, and moved to the closest position where it does not overlap. A cue keeps its position while it is
  displayed (the text track cue display state): the cues that are displayed later are placed around it.
- **The regions**: a cue of a region (`VttCue.Region`, a region with an identifier) is rendered in the box of the region:
  a flex container of the width of the region, at most `lines` lines of 6vh tall, whose anchor is at its viewport anchor,
  with the background of the regions; its cues are stacked and the last ones are visible. The transition of the
  `scroll: up` regions is not animated.
- **Painting**: the boxes are painted over the frame (or the poster) of the video, each as a stacking context, clipped to
  the content box of the video as its other contents (`overflow: clip`), as in the browsers. They are not hit: the
  events target the video element.
- **Updates**: the rules run when the active cues, the mode of a track or a property of a displayed cue changes, at the
  next layout; a document that is rendered (it has a rendering backend, or its layout was computed) updates its layout
  at the rendering opportunity that follows, so that the positions are the ones of the time of the change. The
  `:past` and `:future` pseudo-classes follow the current playback position.

### Pixel snapping

As in the browsers, the painting is aligned to the device pixels while the layout keeps fractional positions (the CSSOM
View returns them):

- the rectangles painted for the boxes (the backgrounds, the borders, the shadows, the outlines, the overflow clips, the
  images, the decorations) have their edges rounded to the nearest device pixel of the viewport (`ViewportOptions.DevicePixelRatio`,
  in the coordinates of the nearest transformed stacking context); a side that is not empty keeps one device pixel;
- a background image that is not repeated is drawn as the image of a replaced box: its rectangle, computed from the
  positioning area of the layout, is snapped to the device pixels (an image that covers no pixel is not painted), so the
  same image positioned by `object-position` and by `background-position` paints the same pixels;
- the baselines of the texts are snapped in the block axis (the glyphs keep their subpixel positions along the line);
- the glyphs of the Ahem font (the font of the tests: its glyphs are rectangles of the em box) are not antialiased, and
  their origins are rounded to the device pixels: a pixel is painted when its center is in the glyph, so a glyph paints
  the same pixels as a box of its size at its position, whatever the font size (the ascent of a 16px Ahem glyph is 12.8px
  and its descent 3.2px: 13 and 3 rows of pixels, the rounded metrics of the layout). The browsers do the same in their
  tests: WebKit never antialiases the font whose family name is Ahem, Gecko has a preference for it
  (`gfx.font_rendering.ahem_antialias_none`), the web tests of Chromium disable the antialiasing of every font, and the
  hinting of FreeType moves the edges of the glyphs to the pixels on Linux;
- the ascent, the descent and the line gap of the fonts are rounded to whole pixels (as Blink, except for the tiny fonts),
  and the half-leading above the text is floored: `line-height: normal` and the baselines of the integer font sizes are on
  whole pixels (e.g. 18px instead of 18.4px for the 16px default font);
- the glyph outlines of the other fonts are not hinted: the edges of a glyph that do not fall on a pixel are antialiased,
  where the browsers on Linux snap them with the FreeType hinting;
- the two lines of a `double` side and the two halves of a `groove` or `ridge` side are whole numbers of device pixels
  (when the width of the side is one), as the lines of Blink: each line of a `double` side is `floor((width + 1) / 3)` wide
  (3 + 4 + 3 for 10px, 2 + 1 + 2 for 5px), and the half at the top or at the left of a `groove` or `ridge` side is the
  larger one (3 + 2 for 5px), whatever the side. The thirds and the halves of a width that is not a whole number of device
  pixels (a transformed or zoomed box) stay fractional.

The same shape painted in two ways paints the same pixels (the reftests compare, for instance, a rule with a border):

- **the dotted and dashed lines** are drawn from their left or top end, whatever the side of the box (the dashes are
  symmetric: the line starts and ends with a whole dash, but a rasterizer does not draw the round caps of the dots the
  same way in the two directions); a side without diagonal (its adjacent sides have no width) is clipped by the rectangle
  of the side, not by a path;
- **the rules** (`column-rule`, the gap decorations, the collapsed borders of the tables) are painted as the only side of
  a border: the same operations, in the same clip;
- **the glyphs of a run** are painted from the line-left end of the line, as the text fragments of a line are: the
  glyphs of a right-to-left run are in the reverse of their logical order. The antialiased edges of two glyphs that
  overlap (the joined letters of Arabic) are composited one after the other, and the rounding of the result depends on
  the order, so a text paints the same pixels whether it is one run or several ones (the same word in several inline
  boxes).

The edges that are shared by two shapes are painted so that the backdrop does not show through them:

- **the joins of the sides of a border** (`BorderPainter`, as the miters of Blink): each side is painted in its trapezoid,
  up to the diagonal between the outer and the inner corner (extended to the chord of the curve of a rounded padding edge). The diagonal between two sides of the same color is not
  antialiased (each pixel is painted by one side: no seam); between two opaque colors, the side painted first covers the
  whole corner and the other one is painted over it up to its antialiased diagonal (the pixels of the diagonal are a mix of
  the two colors only); otherwise (a translucent or missing side, the two shades of a 3D style) both diagonals are
  antialiased. The border of a box with shaped corners (`corner-shape`) is the area between its outer and its inner contour,
  which is not limited to the trapezoids of the sides (the inner contour of a concave corner is in the padding box): the
  painted sides of one solid color are one ring, and the sides of different colors or styles are painted between the lines
  of the diagonals of their two corners, up to the line where the diagonals of the opposite side cross them;
- **the highlighted glyphs** (`::selection`, `::highlight()`): the glyphs of a text are painted once, in the color of the
  topmost highlight that sets a color for the characters it covers and in the color of the text for the other ones (glyphs
  painted twice would have darker antialiased edges). The glyphs whose advance is in a highlighted part are painted whole,
  without clip (their ink can be outside of their advance); only a glyph that is partially highlighted (a ligature) is
  clipped at the edges of the part, snapped to the device pixels.

A screenshot with another `ScreenshotOptions.DevicePixelRatio` keeps the snapping of the viewport ratio.

## Paint timing

[Paint Timing](https://w3c.github.io/paint-timing/) (`first-paint` and `first-contentful-paint`, `PerformancePaintTiming`),
[Largest Contentful Paint](https://w3c.github.io/largest-contentful-paint/) (`LargestContentfulPaint`) and
[Element Timing](https://w3c.github.io/element-timing/) (`PerformanceElementTiming`, the `elementtiming` attribute) report the
paints of a document to the performance timeline of its window (`Dom/Document.PaintTiming.cs`, `Dom/Performance/PaintTiming.cs`):

- **The paint is the display list**: "the user agent paints" when the rendering update paints the document into its display
  list, whether or not a rendering backend rasterizes it (without a backend, the display list is the "virtual paint" of the
  page; the images are drawn without their pixels). `ContentfulPaintScanner` finds the paints in the display list: each
  drawing operation is the paint of its `DisplayItem.Source` (the operations of the nested documents and the default
  background of the canvas are ignored); an operation in a group whose opacity is 0, or whose bounds (with the transforms)
  are empty or do not intersect the scrolling area of the document, is not paintable (the painting already omits the hidden
  boxes and the back faces). A glyph run of a non-empty text (also a transparent one: the painting emits the glyphs of a
  transparent text for this purpose, which the backends skip), an image (`DrawImageItem`, an image shader, an SVG image
  drawn for an element in a save whose `Source` is the element) and the graphics of an SVG element are contentful.
- **Lazy**: the paints of a document are only computed once a script of its window observes the `paint`,
  `largest-contentful-paint` or `element` entries, or reads the `paint` entries of the timeline (`getEntries()`,
  `getEntriesByType("paint")`, `getEntriesByName()`): the document is painted by every rendering update from then on
  (`DomEventLoop` "mark paint timing" at the end of the rendering update, before "report the layout shift"; a rendering update
  whose display list did not change marks nothing), and the layout and image changes schedule rendering updates. A page whose
  scripts never read the paint timing is never laid out nor painted for it; the content painted before the tracking starts is
  considered painted by the next rendering update (the image loads are recorded before, as pending image records).
- **First paint / first contentful paint**: the first rendering update that paints something other than the default
  background, and the first one that paints a paintable contentful element (a first contentful paint is also a first paint;
  the bitmap of a canvas with a context is an image of the canvas element, which is not a candidate of the largest
  contentful paint).
  There is no presentation time (`presentationTime` is null): the entries are flushed at once, and their times
  (`startTime`, `paintTime`, `renderTime`) are the end of the rendering update.
- **Largest contentful paint**: the images (the pending image records of the elements whose image is painted and visible
  in the viewport: `img`, SVG `image`, video posters, CSS images of the elements and of their pseudo-elements) and the
  texts (the elements that own the text nodes painted for the first time: the element of their containing block, or the
  originating element of a generated text) of the rendering update are candidates; the effective visual size is the visible
  area of the painted image reduced by its upscaling, floored to a whole number (a 1x1 image displayed large is not a
  candidate), and the low-content images (less than 0.004 byte per pixel of the effective size) are ignored. As in the
  browsers: the reporting stops after a trusted key, mouse or pointer press or a click (the scrolls of the user and
  the scripted scrolls do not stop it); the 3px rule of "compute a new largest contentful paint candidate" only
  applies to the same element; the texts painted again after a web font is loaded are candidates again; an image loaded while
  its element was not in the document is considered loaded when it is first painted; an element whose box is created again
  (e.g. after `display: none`) paints its image again.
- **Element timing**: the images and the texts of the elements with an `elementtiming` attribute, with their
  `intersectionRect` (the intersection rect of the element for an image element, the visible painted image for a CSS
  image, the border boxes of the owned text nodes in the viewport for a text). Each element reports its text once.
- `WebPlatformFeatures.PaintTimingEnabled`, `LargestContentfulPaintEnabled` and `ElementTimingEnabled` hide the
  interfaces and the entry types. The paint timing of a [render-blocked](browsing-context.md#render-blocking) document is
  not marked: its first paint waits for the pending parser-blocking scripts, style sheets and `blocking=render`
  elements. Not done: the paints of the video frames, the first frame of an
  animated image before it is completely loaded, and `object` images. The Event Timing entries
  are described in [Performance timeline](browsing-context.md#performance-timeline).
