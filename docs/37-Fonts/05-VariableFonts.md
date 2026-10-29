---
title: Variable fonts
description: Read the axes and named instances of a variable font, and get the face of an instance.
---

A variable font is one file for a whole family: its axes, such as the weight or the width, vary its outlines, its
metrics and the positions of its layout tables continuously.

## Get an instance

`FontFace.Axes` describes the axes, and `WithCoordinates` gives the face of an instance from the value of each axis:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_VariableFont;format=md-fenced-code-block;dedent=true -->
```csharp
static FontFace WithWeight(FontFace face, int weight)
{
    // One value per axis, in the order of the axes, as 16.16 fixed-point numbers
    var coordinates = new int[face.Axes.Length];
    for (var i = 0; i < face.Axes.Length; i++)
    {
        coordinates[i] = face.Axes[i].Tag == ShapingFeature.ToTag("wght") ? weight << 16 : face.Axes[i].Default;
    }

    // The face of the instance shares the data of the font
    return face.WithCoordinates(coordinates);
}
```
<!-- endref -->

With the variable Noto Sans, the letter `o` is 605 units wide in the default instance (weight 400) and 637 units wide at
weight 900.

- The values are 16.16 fixed-point numbers in the units of the axis: shift a weight of 700 by 16 bits (`700 << 16`).
- `WithCoordinates` takes one value per axis, in the order of `Axes`; an axis without a value stays at its default, and
  a value out of range is brought to the nearest end.
- The face of an instance shares the bytes of the font and is cheap to create. It is a `FontFace` like any other: decode
  its outlines, read its metrics and shape text with it. `Instance` gives its coordinates; it is `null` for the default
  instance.
- A font that does not vary has no axis.

## Axes and named instances

| `FontAxisRecord` | Description |
| --- | --- |
| `Tag` | The tag of the axis, such as `wght`, `wdth`, `slnt`, `ital` or `opsz`, as a big-endian integer: compare it with `ShapingFeature.ToTag("wght")` |
| `Minimum`, `Default`, `Maximum` | The range of the axis and its default value |
| `IsHidden` | The font asks that the axis is not shown to users |
| `NameId` | The name of the axis: `FontFace.GetName(NameId)` |

`NamedInstances` lists the instances the font names, such as "Light" or "Bold Condensed": `Coordinates` are the values
to pass to `WithCoordinates`, and `NameId` their name.

To choose instances by their axis tags without handling coordinates, use
[the variable fonts of Meziantou.Framework.Imaging](../01-Imaging/08-Text/02-Fonts.md#variable-fonts)
(`Font.WithVariations`).
