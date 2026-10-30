---
title: Introduction
description: Generate an SVG avatar with the initials of a name, a color derived from the name, and accessible markup.
---

`Meziantou.Framework.Avatar` generates avatars: small SVG images that show the initials of a name on a colored
background. Use them for the users, teams or projects that have no picture.

![A round avatar with the initials JD](images/JohnDoe.svg)
![A round avatar with the initials JP](images/InitialsJeanPierre.svg)
![A round avatar with the initials OB](images/InitialsOBrien.svg)
![A round avatar with the Japanese characters 山田](images/InitialsYamadaTaro.svg)

## What you can do with it

- **Generate an avatar** from a name, as an SVG string you embed in a page or save as a file. See
  [Getting started](02-GettingStarted.md).
- **Change its appearance**: the shape, the size and the palette of colors. See [Appearance](03-Appearance.md).
- **Control the initials**: the library picks one or two graphemes from the name, or uses the text you give. See
  [Initials](04-Initials.md).
- **Make it accessible**: announce the full name, or hide a decorative avatar from screen readers. See
  [Accessibility](05-Accessibility.md).
- **Rely on stable colors**: the same name always gets the same color, for a given palette. See
  [Stable colors](06-StableColors.md).

## Principles

- **Deterministic.** The output depends only on the name and the options: no randomness, no state.
- **Text only.** The result is an SVG string. Rendering it to pixels is the job of the browser, or of whatever displays
  it. The initials are drawn with the bold monospace font of the viewer.
- **Safe with untrusted names.** The output is always well-formed XML, whatever the name contains.

## Namespace

The types are in the [Meziantou.Framework](cref:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| `AvatarGenerator` | The `CreateSvg` methods |
| `AvatarOptions` | The shape, the size, the palette, the initials and the accessibility options |
| `AvatarShape` | `Round`, `Square` and `RoundedSquare` |
| `AvatarColorPair` | A background color and a foreground color |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and generate your first avatar.
