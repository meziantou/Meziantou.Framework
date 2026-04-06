# Meziantou.Framework.Avatar

Generate deterministic avatar SVG strings from a name.

## Usage

```c#
using Meziantou.Framework;

var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());
```

`AvatarGenerator` extracts a 1-2 character bigram and selects a foreground/background pair from the palette using a hash of the provided name.

## Customize the output

```c#
using Meziantou.Framework;

var options = new AvatarOptions
{
    Bigram = "JD", // optional explicit 1-2 character bigram
    Shape = AvatarShape.Round, // Round or Square
    Size = 128,
};
options.Palette.Clear();
options.Palette.Add(new AvatarColorPair("#CFDADE", "#153037"));

var svg = AvatarGenerator.CreateSvg("John Doe", options);
```
