---
title: Layout and geometry
description: Read the positions and the sizes of the elements, find the element at a point, scroll, and observe the changes of size and visibility.
---

The library lays the pages out as a browser does: blocks and inline text, floats, flexbox, grid, tables, positioning,
multi-column, writing modes, SVG and MathML. The layout is internal: it runs when a web API needs the geometry, and its
results are read through the APIs of the browsers.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/CssomViewTests.cs;region=WebPlatform_Geometry;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 800, Height = 600 },
});

var document = context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <style>
      body { margin: 0 }
      header { height: 60px }
      .sidebar { position: absolute; top: 60px; width: 25%; height: 300px }
    </style>
    <header>Title</header>
    <div class="sidebar">Menu</div>
    """);

// The layout runs when the geometry is read
var sidebar = document.QuerySelector(".sidebar")!;
var rect = sidebar.GetBoundingClientRect();
Assert.Equal((0, 60, 200, 300), (rect.X, rect.Y, rect.Width, rect.Height));
Assert.Same(sidebar, document.ElementFromPoint(100, 100));

// A new viewport size lays the document out again
context.Viewport = new ViewportOptions { Width = 1200, Height = 600 };
Assert.Equal(300, sidebar.GetBoundingClientRect().Width);
```
<!-- endref -->

## The geometry APIs

| API | Returns |
| --- | --- |
| `GetBoundingClientRect()`, `GetClientRects()` | The border box of an element in the viewport, transforms included, or the boxes of each fragment (the lines of an inline element) |
| `OffsetLeft`, `OffsetTop`, `OffsetWidth`, `OffsetHeight`, `OffsetParent` | The position and the size of an element relative to its offset parent |
| `ClientWidth`, `ClientHeight`, `ScrollWidth`, `ScrollHeight` | The size of the padding box and of the scrollable area |
| `ElementFromPoint(x, y)`, `ElementsFromPoint(x, y)` | The element at a point of the viewport, found by the hit testing of the browsers |
| `Window.GetComputedStyle(element)` | The used values: `width: 50%` reads as a length in pixels |
| `CheckVisibility()` | Whether the element is rendered and visible |
| `InnerText` | The text as rendered, with the line breaks |

The geometry is in CSS pixels. Changing the document, its styles or the viewport invalidates the layout, which runs again
the next time a geometry API is called: read the geometry after a batch of changes, not between each change.

## Scrolling

`Window.ScrollTo`, `Element.ScrollTo`, `Element.ScrollIntoView`, `ScrollTop` and `ScrollLeft` scroll the viewport and the
scroll containers, with the `scroll` events, the scroll snapping and the scroll anchoring. The user input scrolls too: the wheel, a panning touch and the keys (see
[User input](06-UserInput.md)).

## Observers

`ResizeObserver` and `IntersectionObserver` report the changes of size and of visibility of the elements, and the lazy
loading of the images and frames (`loading="lazy"`) follows the viewport. They are run by the rendering updates of the
event loop: run the event loop to get their callbacks (see [Time and the event loop](07-TimeAndTheEventLoop.md)).

## Fonts and text

The text is measured with the fonts bundled with the library (Tinos for `serif`, Arimo for `sans-serif`, Cousine for
`monospace`, metric-compatible with Times New Roman, Arial and Courier New), the web fonts of the page (`@font-face`), and,
when you allow it, the fonts installed on the machine: see [Fonts](../07-Rendering/06-Fonts.md). The positions of the
text are the same on every machine with the bundled fonts.
