---
title: Loading fonts
description: Load fonts from files, streams and memory, PostScript Type 1 fonts included, bound the work of hostile fonts with limits, and handle the errors.
---

## Load a font

[FontLoader](cref:Meziantou.Framework.Fonts.FontLoader) reads a whole font file into memory: the tables of a font refer to each other by offsets, and a face reads
them in place.

| Method | Reads |
| --- | --- |
| `FontLoader.Load(ReadOnlySpan<byte>, environment)` | The bytes of a file |
| `FontLoader.Load(Stream, environment)` | A stream, to its end |
| `FontLoader.LoadAsync(Stream, environment, useEmbeddedBitmaps, cancellationToken)` | A stream, asynchronously |
| `FontLoader.LoadType1(ReadOnlySpan<byte>, metrics, environment)` | A PostScript Type 1 font, with the metrics of its AFM file |

The result is an array of [FontFace](cref:Meziantou.Framework.Fonts.FontFace): one for a font, one per font of a collection. The faces of a collection share the
bytes of the file.

- What every use of the font needs is validated when it is loaded: the table directory, the required tables, their
  headers, the sizes of the metrics tables and the structure of the character map. The data of a glyph is validated when
  the glyph is decoded.
- `useEmbeddedBitmaps` makes the one-bit bitmaps of the font (`EBLC` and `EBDT`), made for screens of low resolution,
  replace its outlines at the sizes they are made for. It is off by default, so that the rendering of a text does not
  change with the tables a font happens to have.

## PostScript Type 1 fonts

The [Load](cref:Meziantou.Framework.Fonts.FontLoader.Load) methods also read PostScript Type 1 fonts: PFB files
(binary) and PFA files (text). The format is detected from the content. The font is converted to an OpenType font with
CFF outlines when it is loaded, so it is measured, shaped, drawn and subset like any other font:
[Outlines](cref:Meziantou.Framework.Fonts.FontFace.Outlines) is `Cff`, and the
[Bytes](cref:Meziantou.Framework.Fonts.FontData.Bytes) of [FontFace.Data](cref:Meziantou.Framework.Fonts.FontFace.Data)
are the bytes of the converted font.

- The glyphs are the ones of the font program, in its order, with `.notdef` first. The outlines are the ones of the
  charstrings: the subroutines, the flex mechanism and the accented characters made with `seac` are expanded. The
  hints are not kept.
- The character map comes from the names of the glyphs and the Adobe Glyph List (`A`, `Aacute`, `fi`, `uni2206`): a
  glyph whose name has no character, such as a variant (`a.sc`), is only reached by its identifier.
- The units per em come from the font matrix (1000 for most fonts). The ascender and the descender are the top and the
  bottom of the bounding box of the font, and the cap height and the x-height the tops of `H` and `x`.

The metrics of a Type 1 font are in a separate AFM file. Give them to
[LoadType1](cref:Meziantou.Framework.Fonts.FontLoader.LoadType1) to add the kerning pairs of the file to the font, and
to take its ascender, descender, cap height and x-height. The advance widths are always the ones of the font program.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_LoadType1;format=md-fenced-code-block;dedent=true -->
```csharp
var environment = new PooledFontEnvironment(FontResourceLimits.Default, maxLiveAllocationBytes: 64 * 1024 * 1024);

// A PFB or PFA file is detected from its content, like the other formats: it becomes a face with CFF outlines
var face = FontLoader.Load(pfbFile, environment)[0];
Assert.Equal(FontOutlineKind.Cff, face.Outlines);

// The characters come from the names of the glyphs: "A" is the character A
var glyph = face.MapCodePoint('A');
Assert.Equal(600, face.GetAdvanceWidth(glyph));

// The AFM file of the font adds its kerning
var metrics = AfmFontMetrics.Parse(afmFile);
var kerned = FontLoader.LoadType1(pfbFile, metrics, environment);
Assert.Equal(-50, kerned.GetLegacyKerning(glyph, kerned.MapCodePoint('O')));
```
<!-- endref -->

Multiple master fonts, CID-keyed fonts and stroked fonts (`PaintType 2`) are
[UnsupportedFontFeatureException](cref:Meziantou.Framework.Fonts.UnsupportedFontFeatureException). Every charstring
is run when the font is loaded: a malformed one fails the loading with an
[InvalidFontException](cref:Meziantou.Framework.Fonts.InvalidFontException) that names the glyph.

## WOFF metadata and private data

A WOFF or WOFF2 file can carry two blocks next to the font: an XML document of metadata (vendor, credits, license,
copyright, ...) and private data that only the vendor of the font can interpret. They are not needed to use the font,
so the face does not read them when it loads:

- [GetWoffMetadata](cref:Meziantou.Framework.Fonts.FontData.GetWoffMetadata) expands the metadata block and returns
  its XML document, without parsing it, or `null` when the file has none or is not a WOFF or WOFF2 file. The block is
  kept compressed with the font and expanded at each call, in a buffer rented from the environment; its document is
  bounded by `MaxFontBytes`.
- [GetWoffPrivateData](cref:Meziantou.Framework.Fonts.FontData.GetWoffPrivateData) returns the bytes of the private
  data block, empty when there is none.

The blocks the font keeps count with the font against `MaxFontBytes` when the file is loaded. A block that lies outside
a WOFF file, or whose compressed data is malformed, does not prevent the font from loading: the method that reads it
throws [InvalidFontException](cref:Meziantou.Framework.Fonts.InvalidFontException).

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_WoffMetadata;format=md-fenced-code-block;dedent=true -->
```csharp
static (string? Metadata, ReadOnlyMemory<byte> PrivateData) ReadWoffBlocks(byte[] woff)
{
    var environment = new PooledFontEnvironment(FontResourceLimits.Default, maxLiveAllocationBytes: 64 * 1024 * 1024);
    var face = FontLoader.Load(woff, environment)[0];

    // The XML document of the metadata block, or null when the file has none
    var metadata = face.Data.GetWoffMetadata();

    // The private data block, empty when the file has none
    var privateData = face.Data.GetWoffPrivateData();
    return (metadata, privateData);
}
```
<!-- endref -->

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
| [MaxGlyphComplexity](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxGlyphComplexity) | 1,000,000 | The elementary operations (outline points, components, charstring operators, instructions) to produce one glyph, also when the charstrings of a Type 1 font are converted |
| [MaxTextGlyphs](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxTextGlyphs) | 1,000,000 | The glyphs of a shaped text |
| [MaxCollectionFaces](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxCollectionFaces) | 1,024 | The faces of a font collection (`.ttc`, `.otc`), checked before they are read: each face is read when the collection is loaded |

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
