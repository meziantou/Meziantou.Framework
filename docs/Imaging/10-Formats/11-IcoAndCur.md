---
title: ICO and CUR
description: Read and write Windows icons and cursors with several sizes, PNG or bitmap representations, and hotspots.
---

An icon (`.ico`) or a cursor (`.cur`) file holds several representations of one drawing, usually at several sizes, from
which Windows picks the one it shows. A cursor also stores a hotspot per representation: the pixel that points.
[IcoEncoder](cref:Meziantou.Framework.Imaging.Formats.IcoEncoder) writes both.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Representations | PNG and bitmap (DIB) payloads of 1 to 32 bits, with their AND mask | Still PNG or 32-bit DIB payloads with alpha |
| Sizes | Up to 256 pixels per side | Up to 256 pixels per side |
| Hotspots | Read for each representation of a cursor | Written for each representation of a cursor |
| Metadata | None | None |

[Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) reads the largest representation (then the deepest), with the hotspot of a cursor on its frame. Every
representation stored as a bitmap loads as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32).

## Write an icon and a cursor of several sizes

Every representation is an entry of an [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) of [Representations](cref:Meziantou.Framework.Imaging.ImageCollectionKind.Representations). [SelectBySize](cref:Meziantou.Framework.Imaging.ImageCollection.SelectBySize) returns the entry that
fits a size best, without resizing anything:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_IcoRepresentations;format=md-fenced-code-block;dedent=true -->
```csharp
public static Point WriteAndReadIcon(string inputPath, string iconPath, string cursorPath)
{
    using var loaded = Image.Load(inputPath);
    using var source = loaded.CloneFrame(0); // a representation is a still image, never an animation frame
    using var representations = ImageCollection.Create(ImageCollectionKind.Representations);
    foreach (var side in (int[])[16, 32, 256])
    {
        using var sized = source.Clone();
        sized.Resize(new ResizeOptions(side, side) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
        representations.Add(sized, hotspot: new Point(side / 2, side / 2));
    }

    // An icon cannot store a hotspot, so the entry hotspots are unsupported metadata there
    representations.Save(iconPath, new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
    representations.Save(cursorPath, new IcoEncoder { Kind = IconKind.Cursor });

    using var icon = ImageCollection.Load(iconPath);
    using var best = icon.SelectBySize(new Size(24, 24)).Decode(); // the 32x32 representation: nothing is resized
    using var cursor = ImageCollection.Load(cursorPath);
    return cursor.SelectBySize(new Size(16, 16)).Hotspot!.Value;
}
```
<!-- endref -->

[Image.Save](cref:Meziantou.Framework.Imaging.Image.Save) writes an icon or a cursor of one representation. See
[Pages and representations](../04-Concepts/03-PagesAndRepresentations.md).

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Kind](cref:Meziantou.Framework.Imaging.Formats.IcoEncoder.Kind) | [Icon](cref:Meziantou.Framework.Imaging.Formats.IconKind.Icon) or [Cursor](cref:Meziantou.Framework.Imaging.Formats.IconKind.Cursor) | [Icon](cref:Meziantou.Framework.Imaging.Formats.IconKind.Icon) ([Cursor](cref:Meziantou.Framework.Imaging.Formats.IconKind.Cursor) for the `.cur` extension) |
| [PayloadFormat](cref:Meziantou.Framework.Imaging.Formats.IcoEncoder.PayloadFormat) | [Auto](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Auto) writes a 32-bit DIB up to 64 pixels per side and a PNG above, or for 16-bit pixels; [Png](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Png) always writes PNG (which readers older than Windows Vista do not understand); [Dib](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Dib) always writes a 32-bit DIB | [Auto](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Auto) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata, which an icon cannot store, and with hotspots in an icon | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

## Pitfalls

- An icon cannot store a hotspot: an entry or a frame that has one follows [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects it by
  default. The example discards them for the icon. A cursor always stores one, the top-left corner when the entry has none.
- A representation larger than 256 pixels per side is rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): resize it
  first.
- A DIB stores 8-bit samples: [Auto](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Auto) writes 16-bit pixels as PNG, and [Dib](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Dib) rejects them instead of reducing them.
- Animated images are rejected: an animated cursor is an [ANI](12-Ani.md) file.
- The file stores no metadata: an ICC profile, EXIF, XMP, a resolution, an orientation or text entries follow
  [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling). PNG payloads are written without metadata.
- The destination does not need to be seekable: each payload is encoded in memory before it is written.
