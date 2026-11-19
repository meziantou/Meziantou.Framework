---
title: Parameters of SVG images
description: Pass colors and other CSS values to the SVG images of a page with link-parameters, param() and the fragment of their URL.
---

An SVG image drawn by an `img` element or a CSS image is an external document: the styles of the page do not apply to it.
CSS Linked Parameters pass values to it: the image reads them with `env()`, and each element that draws the image can
pass its own values.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Imaging.Tests/DocumentationImageTests.cs;region=WebPlatformImaging_LinkParameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 180, Height = 60 },
    RenderingBackend = new ImagingRenderingBackend(),
});

// The icon reads its color with env(), black when no parameter is passed
const string Icon = "data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><circle cx='20' cy='20' r='18' fill='env(--color, black)'/></svg>";
context.Navigate("https://example.com/", _ => $$"""
    <!DOCTYPE html>
    <style>
      body { margin: 10px; display: flex; gap: 20px }
      .blue { link-parameters: param(--color: royalblue) }
      .text { color: seagreen; link-parameters: param(--color <color>: currentcolor) }
    </style>
    <img src="{{Icon}}">
    <img class="blue" src="{{Icon}}">
    <img class="text" src="{{Icon}}">
    """);
context.EventLoop.RunUntilIdle();

using var screenshot = context.TakeScreenshot();
```
<!-- endref -->

![Three circles drawn from the same SVG image: black, blue and green](images/LinkParameters.png)

## Read the parameters in the image

A parameter named `--color` is the custom environment variable `env(--color)` of the image. Give it a fallback, used when
the element passes no parameter: `fill="env(--color, black)"`, or once for the whole image:
`:root { --color: env(--color, black) }`, then `var(--color)` everywhere.

The `color` and `accent-color` parameters set the `color` and the `accent-color` of the root element of the image, so an
image that paints with `currentcolor` follows `param(color: blue)`.

## Pass the parameters

| Where | Applies to |
| --- | --- |
| The `link-parameters` property: `link-parameters: param(--color: green), param(--size: 4px)` | The images of the element: the image of an `img`, an SVG `image` or `feImage` element, and its CSS images (`background-image`, `list-style-image`, `border-image-source`...) |
| A `param()` modifier of `url()`: `background-image: url("icon.svg" param(--color: green))` | That URL only |
| The fragment of the URL: `<img src="icon.svg#:~:param(--color:green)">` | That URL only; several parameters are separated by `&` (`#:~:param(--a:red)&param(--b:blue)`) |

The parameters are combined in this order, and the last one of a name wins: the `link-parameters` property, the fragment of
the URL, then the `param()` modifiers. The fragment directive (`:~:` and what follows) is not part of the URL of the image
document: `icon.svg#box:~:param(--color:green)` still targets the element `box` of the image.

## Values and types

- `param(--name: value)` passes the value as written: `param(--color: currentcolor)` passes the `currentcolor` keyword,
  which the image resolves with its own `color`. The `var()` of the value are substituted on the element.
- `param(--name <type>: value)` (or `type(<color> | <length>)` for several types) resolves the value on the element:
  `param(--color <color>: currentcolor)` passes the color of the element. A value that does not match its type ignores the
  parameter, so `param(--c <color>: blue), param(--c <color>: color-mix(...))` falls back to the first one.
- `param(--name)` passes the value of the custom property `--name` of the element; `param(--name: )` passes an empty
  value.
- In an SVG document, a number without a unit substituted in an attribute is invalid, as with `var()`: pass
  `param(--x: 50px)` to an image that uses `x="env(--x, 0)"`.

The computed value of `link-parameters` is the list as specified, after the substitution of `var()`:
`getComputedStyle(element).getPropertyValue("link-parameters")` returns `param(--a <color>: currentcolor)` unchanged.
