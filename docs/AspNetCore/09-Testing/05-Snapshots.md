---
title: Snapshots
description: Compare the HTML, the accessibility tree and the screenshot of a component with the expected ones.
---

A rendered component gives three views of its output:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Snapshots;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost(new ComponentTestHostOptions
{
    Viewport = new ViewportOptions { Width = 400, Height = 300 },
    StyleSheets = { "body { margin: 0 } .card { width: 200px; padding: 10px; background: rgb(0, 128, 0); color: white }" },
});
var card = await host.RenderAsync<StyledCard>(parameters => parameters
    .Add(c => c.Title, "Title")
    .AddChildContent("Some <em>content</em>"));

var element = card.Find(".card");
Assert.Equal("rgb(0, 128, 0)", host.BrowsingContext.Window.GetComputedStyle(element).BackgroundColor);
Assert.Equal(220, element.GetBoundingClientRect().Width);

var png = card.TakeScreenshotAsPng();
Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Take(4));

Assert.Equal("""
    - heading "Title" [level=2]
    - paragraph:
      - text: Some
      - emphasis: content
    """, card.GetAccessibilitySnapshot());
```
<!-- endref -->

- `Markup` is the indented HTML of the elements of the component. Blazor sets the value of the fields as properties, so
  [MarkupOptions](cref:Meziantou.AspNetCore.Testing.MarkupOptions) writes the state of the fields as
  attributes (`value`, `checked`, `selected`), and removes the `_bl_` attributes of the element references.
- `GetAccessibilitySnapshot()` is the accessibility tree, in the YAML format of the ARIA snapshots of Playwright: the
  roles, the names and the states that assistive technologies see.
- `TakeScreenshotAsPng()` draws the elements of the component. The fonts are bundled with WebPlatform, so the text has
  the same size and shape on every machine; only the antialiasing of the edges can differ slightly between CPU
  architectures.

Pass the style sheets of the application with
[StyleSheets](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.StyleSheets), or serve them with
[StaticFiles](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.StaticFiles) (for instance `wwwroot`
and the bundle of the CSS isolation) and link them from
[HostPage](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.HostPage).

## Compare screenshots with snapshot files

Rather than asserting pixels, compare a screenshot with an image stored next to the test, with
`Meziantou.Framework.SnapshotTesting.Imaging` (see [Images](../../SnapshotTesting/07-Extensions/02-Images.md)). Register
the image comparer once for the test assembly, with a threshold that ignores the antialiasing differences:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/SnapshotConfiguration.cs;region=ComponentTestHost_SnapshotSetup;format=md-fenced-code-block;dedent=true -->
```csharp
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Compare the images by their pixels. The antialiasing of the edges can differ slightly between CPU architectures
        // (floating-point rounding): a peak signal-to-noise ratio of 40 dB ignores it, not a change of color or layout.
        SnapshotSettings.Default.AddImaging(new ImageComparisonSettings { PsnrThreshold = 40 });
    }
}
```
<!-- endref -->

Then validate the screenshot:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_ScreenshotSnapshot;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost(new ComponentTestHostOptions
{
    Viewport = new ViewportOptions { Width = 400, Height = 300 },
    StyleSheets = { "body { margin: 0 } .card { width: 200px; padding: 10px; background: rgb(0, 128, 0); color: white; font-family: sans-serif }" },
});
var card = await host.RenderAsync<StyledCard>(parameters => parameters
    .Add(c => c.Title, "Title")
    .AddChildContent("Some <em>content</em>"));

// The first run writes __snapshots__/ComponentTestHostTests_StyledCard_ScreenshotMatchesTheSnapshot.actual.png: check it and
// rename it to .verified.png (or run with SNAPSHOTTESTING_STRATEGY=Overwrite); the next runs compare the screenshot with it
Snapshot.Validate(card.TakeScreenshotAsPng(), SnapshotType.Png);
```
<!-- endref -->

A change of color, size or layout makes the test fail and writes the new screenshot as a `.actual.png` file next to the
snapshot: compare the two images, then accept the new one with `SNAPSHOTTESTING_STRATEGY=Overwrite`.
