---
title: Loading fonts
description: Load fonts from files, streams and memory, bound the work of hostile fonts with limits, and handle the errors.
---

## Load a font

[FontLoader](cref:Meziantou.Framework.Fonts.FontLoader) reads a whole font file into memory: the tables of a font refer to each other by offsets, and a face reads
them in place.

| Method | Reads |
| --- | --- |
| `FontLoader.Load(ReadOnlySpan<byte>, environment)` | The bytes of a file |
| `FontLoader.Load(Stream, environment)` | A stream, to its end |
| `FontLoader.LoadAsync(Stream, environment, useEmbeddedBitmaps, cancellationToken)` | A stream, asynchronously |

The result is an array of [FontFace](cref:Meziantou.Framework.Fonts.FontFace): one for a font, one per font of a collection. The faces of a collection share the
bytes of the file.

- What every use of the font needs is validated when it is loaded: the table directory, the required tables, their
  headers, the sizes of the metrics tables and the structure of the character map. The data of a glyph is validated when
  the glyph is decoded.
- `useEmbeddedBitmaps` makes the one-bit bitmaps of the font (`EBLC` and `EBDT`), made for screens of low resolution,
  replace its outlines at the sizes they are made for. It is off by default, so that the rendering of a text does not
  change with the tables a font happens to have.

## The environment

The font code takes what it needs from its caller through a [FontEnvironment](cref:Meziantou.Framework.Fonts.FontEnvironment): the limits of its work
([FontResourceLimits](cref:Meziantou.Framework.Fonts.FontResourceLimits)), and the allocators of its temporary buffers. A face keeps its environment
(`FontFace.Data.Environment`), and the operations on the face rent their buffers from it.

[PooledFontEnvironment](cref:Meziantou.Framework.Fonts.PooledFontEnvironment) is the environment of an application that has no allocator of its own: the temporary buffers
are rented from the shared array pool, and the bytes the buffers of one operation hold at the same time are bounded by
`maxLiveAllocationBytes`. Create one environment and reuse it for every font.

## Limits

A font is untrusted input, like an image: its composite glyphs can refer to each other and its subroutines call each
other, to describe an outline far larger than the file, and its lookups can multiply glyphs. The limits bound this work:

| [FontResourceLimits](cref:Meziantou.Framework.Fonts.FontResourceLimits) | Default | What it bounds |
| --- | --- | --- |
| [MaxEncodedBytes](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxEncodedBytes) | 256 MiB | The bytes read from a font file |
| [MaxFontBytes](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxFontBytes) | 256 MiB | The bytes a font keeps in memory, checked before a WOFF or WOFF2 file is expanded |
| [MaxGlyphComplexity](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxGlyphComplexity) | 1,000,000 | The elementary operations (outline points, components, charstring operators, instructions) to produce one glyph |
| [MaxTextGlyphs](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxTextGlyphs) | 1,000,000 | The glyphs of a shaped text |

The limit of the temporary buffers is the `maxLiveAllocationBytes` of the environment. Lower the limits when you accept
fonts from users.

## Errors

The exceptions of the font code derive from [FontException](cref:Meziantou.Framework.Fonts.FontException):

| Exception | When |
| --- | --- |
| [UnknownFontFormatException](cref:Meziantou.Framework.Fonts.UnknownFontFormatException) | The data does not start with the signature of a font format |
| [InvalidFontException](cref:Meziantou.Framework.Fonts.InvalidFontException) | The font does not follow the rules of its format |
| [UnsupportedFontFeatureException](cref:Meziantou.Framework.Fonts.UnsupportedFontFeatureException) | A valid font uses a feature that is not supported; [Feature](cref:Meziantou.Framework.Fonts.UnsupportedFontFeatureException.Feature) names it |
| [FontResourceLimitException](cref:Meziantou.Framework.Fonts.FontResourceLimitException) | A limit is exceeded; [Kind](cref:Meziantou.Framework.Fonts.FontResourceLimitException.Kind), [Limit](cref:Meziantou.Framework.Fonts.FontResourceLimitException.Limit) and [Requested](cref:Meziantou.Framework.Fonts.FontResourceLimitException.Requested) say which and by how much |

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_LoadErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var environment = new PooledFontEnvironment(FontResourceLimits.Default, maxLiveAllocationBytes: 64 * 1024 * 1024);

// The format is detected from the content: this is not a font
Assert.Throws<UnknownFontFormatException>(() => FontLoader.Load("<!DOCTYPE html>"u8, environment));

// A limit is exceeded: the file is larger than what the application accepts
var limits = new FontResourceLimits { MaxEncodedBytes = 1024 };
var exception = Assert.Throws<FontResourceLimitException>(() => FontLoader.Load(new byte[4096], new PooledFontEnvironment(limits, 1024 * 1024)));
Assert.Equal(FontResourceLimitKind.EncodedBytes, exception.Kind);
```
<!-- endref -->

A glyph is validated when it is decoded: [DecodeOutline](cref:Meziantou.Framework.Fonts.FontFace.DecodeOutline) and the shaping of a text can also throw
[InvalidFontException](cref:Meziantou.Framework.Fonts.InvalidFontException) and [FontResourceLimitException](cref:Meziantou.Framework.Fonts.FontResourceLimitException) for a font that loaded without error.
