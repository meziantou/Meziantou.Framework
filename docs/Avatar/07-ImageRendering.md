---
title: Image rendering
description: Create an avatar as a vector scene, and draw it as a PNG, WebP or JPEG image.
---

[CreateSvg](cref:Meziantou.Framework.AvatarGenerator.CreateSvg) returns markup that a browser draws. When you need
pixels, for an email, a mobile application or a file to upload,
[CreateScene](cref:Meziantou.Framework.AvatarGenerator.CreateScene) returns the same avatar as a
[Scene](cref:Meziantou.Framework.VectorGraphics.Scene) of [Vector graphics](../VectorGraphics/01-Introduction.md):
a shape and a text, with the same colors and initials.

## Draw an avatar as a PNG

`Meziantou.Framework.Imaging.VectorGraphics` draws scenes as images. Add it to the project:

<InstallNuGetPackage Id="Meziantou.Framework.Imaging.VectorGraphics" />

Then lay the avatar out with the measurer of the fonts that draw the image, and draw it with
[ToImage](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.ToImage). The example also uses the
[Meziantou.Framework.Imaging.VectorGraphics](cref:Meziantou.Framework.Imaging.VectorGraphics) and
[Meziantou.Framework.Imaging.Formats](cref:Meziantou.Framework.Imaging.Formats) namespaces:

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_CreatePng;format=md-fenced-code-block;dedent=true -->
```csharp
// Lay the avatar out with the fonts that draw the image, so the initials are centered
var measurer = SceneImageRenderer.CreateMeasurer();
var scene = AvatarGenerator.CreateScene("John Doe", new AvatarOptions { Size = 128 }, measurer);

using var image = scene.ToImage();
Assert.Equal((128, 128), (image.Width, image.Height));

// Save to a file (image.Save("avatar.png")) or to a stream, in any format
using var png = new MemoryStream();
image.Save(png, new PngEncoder());
```
<!-- endref -->

![A round, light gray avatar of 128 pixels with the initials JD](images/JohnDoe.png)

The initials are centered with the metrics of the measurer, so pass the measurer of the fonts that draw the image:
[CreateMeasurer](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.CreateMeasurer) with the same
[SceneImageOptions](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions) as
[ToImage](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.ToImage). Without a measurer, the scene is
laid out with the metrics of Helvetica ([AfmTextMeasurer](cref:Meziantou.Framework.VectorGraphics.AfmTextMeasurer)).
[Scale](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.Scale) multiplies the pixels, for
high-density screens.

## Fonts

The image is drawn with the fonts of `Meziantou.Framework.Imaging.VectorGraphics`: Liberation Sans, bold, subset to the
Latin characters. Initials in other scripts, such as `山田` or an emoji, need a font that has them: pass it in
[SceneImageOptions.Font](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.Font) and
[BoldFont](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.BoldFont), both to
[CreateMeasurer](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.CreateMeasurer) and to
[ToImage](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.ToImage). See the
[fonts of image rendering](../VectorGraphics/03-ImageRendering.md#fonts).

The SVG of [CreateSvg](cref:Meziantou.Framework.AvatarGenerator.CreateSvg) uses a monospace font, while a scene uses
the font of the measurer, so initials such as `WM` are wider: the scene draws them with a smaller font when they would
be wider than the shape.

## Use the scene

The scene is a regular [Scene](cref:Meziantou.Framework.VectorGraphics.Scene): its first element is the shape, a
[SceneCircle](cref:Meziantou.Framework.VectorGraphics.SceneCircle) or a
[SceneRectangle](cref:Meziantou.Framework.VectorGraphics.SceneRectangle), and its second is the
[SceneText](cref:Meziantou.Framework.VectorGraphics.SceneText) of the initials. Add elements to it, such as a status
badge, or write it as SVG with [SvgSceneRenderer](cref:Meziantou.Framework.VectorGraphics.SvgSceneRenderer):

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_CreateScene;format=md-fenced-code-block;dedent=true -->
```csharp
// The scene holds the shape and the initials: add elements, or write it as SVG
var scene = AvatarGenerator.CreateScene("Ada Lovelace", new AvatarOptions { Shape = AvatarShape.RoundedSquare });
var initials = scene.Root.Children.OfType<SceneText>().Single();
Assert.Equal("AL", initials.Text);

var svg = SvgSceneRenderer.Render(scene);
Assert.StartsWith("<svg ", svg);
```
<!-- endref -->

The title of the scene is the accessible label: the bigram, or
[AccessibleLabel](cref:Meziantou.Framework.AvatarOptions.AccessibleLabel). It is `null` when
[IsDecorative](cref:Meziantou.Framework.AvatarOptions.IsDecorative) is `true`.

## Colors

A scene needs colors it can draw, so every pair of the palette must use a color that
[SceneColor.Parse](cref:Meziantou.Framework.VectorGraphics.SceneColor.Parse) accepts: `#rgb`, `#rrggbb`, `#rrggbbaa`,
`rgb()`, `hsl()` or a CSS color name. [CreateScene](cref:Meziantou.Framework.AvatarGenerator.CreateScene) throws an
`ArgumentException` for any other color, such as `var(--accent)` or `currentColor`, which only
[CreateSvg](cref:Meziantou.Framework.AvatarGenerator.CreateSvg) supports.
