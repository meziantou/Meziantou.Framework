---
title: Appearance
description: Choose the shape and the size of an avatar, and replace the palette of colors.
---

[AvatarOptions](cref:Meziantou.Framework.AvatarOptions) sets the appearance of an avatar. Pass it as the second argument of [CreateSvg](cref:Meziantou.Framework.AvatarGenerator.CreateSvg).

## Shape

[Shape](cref:Meziantou.Framework.AvatarOptions.Shape) takes one of the values of [AvatarShape](cref:Meziantou.Framework.AvatarShape): [Round](cref:Meziantou.Framework.AvatarShape.Round) (the default), [Square](cref:Meziantou.Framework.AvatarShape.Square) or [RoundedSquare](cref:Meziantou.Framework.AvatarShape.RoundedSquare).

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_Shapes;format=md-fenced-code-block;dedent=true -->
```csharp
var round = AvatarGenerator.CreateSvg("Ada Lovelace", new AvatarOptions { Shape = AvatarShape.Round });
var square = AvatarGenerator.CreateSvg("Ada Lovelace", new AvatarOptions { Shape = AvatarShape.Square });
var roundedSquare = AvatarGenerator.CreateSvg("Ada Lovelace", new AvatarOptions { Shape = AvatarShape.RoundedSquare });

Assert.Contains("<circle ", round);
Assert.Contains("<rect width=\"64\" height=\"64\" fill=", square);
Assert.Contains(" rx=\"", roundedSquare);
```
<!-- endref -->

![A round avatar with the initials AL](images/Round.svg)
![A square avatar with the initials AL](images/Square.svg)
![A square avatar with rounded corners and the initials AL](images/RoundedSquare.svg)

Another value throws an `ArgumentOutOfRangeException`.

## Size and colors

[Size](cref:Meziantou.Framework.AvatarOptions.Size) sets the width and the height of the SVG, in pixels: [AvatarOptions.DefaultSize](cref:Meziantou.Framework.AvatarOptions.DefaultSize) (64) by default. It must be
greater than 0. The SVG has a `viewBox`, so it also scales when CSS sets another size.

[Palette](cref:Meziantou.Framework.AvatarOptions.Palette) is the list of color pairs the generator picks from. Each [AvatarColorPair](cref:Meziantou.Framework.AvatarColorPair) has a background color and a
foreground color, the color of the initials. The colors are written as they are in the SVG, so any CSS color works:
`#CFDADE`, `rgb(207 218 222)`, `teal`.

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_CustomOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new AvatarOptions
{
    Bigram = "JD", // optional explicit 1-2 grapheme bigram
    Shape = AvatarShape.Round, // Round, Square, or RoundedSquare
    Size = 128,
};
options.Palette.Clear();
options.Palette.Add(new AvatarColorPair("#CFDADE", "#153037")); // background, then foreground

var svg = AvatarGenerator.CreateSvg("John Doe", options);
Assert.Contains("width=\"128\" height=\"128\"", svg);
Assert.Contains("<circle cx=\"64\" cy=\"64\" r=\"64\" fill=\"#CFDADE\"/>", svg);
```
<!-- endref -->

![A round avatar of 128 pixels, light gray, with the initials JD](images/CustomPalette.svg)

- [Palette](cref:Meziantou.Framework.AvatarOptions.Palette) is a mutable list that starts with the 11 default pairs: clear it to replace them, or add pairs to extend
  them.
- The palette must not be empty, and each pair must have two colors, or [CreateSvg](cref:Meziantou.Framework.AvatarGenerator.CreateSvg) throws an `ArgumentException`.
  The [AvatarColorPair](cref:Meziantou.Framework.AvatarColorPair) constructor rejects a color that is `null` or whitespace; `default(AvatarColorPair)` has no
  colors.
- Choose pairs with enough contrast between the background and the initials for them to stay readable.
- Changing the palette changes the color of most names. See [Stable colors](06-StableColors.md).

## Share the options

An [AvatarOptions](cref:Meziantou.Framework.AvatarOptions) instance can be reused for many avatars, from several threads, as long as nobody changes it at the
same time: the palette is not thread-safe. Create the options once, for instance at startup, and do not change them
afterwards.
