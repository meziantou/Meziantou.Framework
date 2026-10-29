---
title: Pages and representations
description: Read and write multi-page TIFF documents and the sizes of an icon or a cursor with ImageCollection.
---

Some files hold several images that are not an animation: the pages of a TIFF document, in order, and the sizes and
color depths of the same drawing in an icon or a cursor file. The library keeps them apart from frames, in an
`ImageCollection`. Its entries can each have their own size, pixel format and metadata, and never have a duration or a
play count, so code that plays the frames of an animation never receives pages or icon sizes by accident.

## Write and read a TIFF document

Create a collection of pages, add the images, and save it. Loading it back gives access to each page:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TiffPages;format=md-fenced-code-block;dedent=true -->
```csharp
public static Size WriteAndReadTiffDocument(string inputPath, string documentPath)
{
    using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
    {
        using var source = Image.Load(inputPath);
        using var first = source.CloneFrame(0); // a page is a still image, never an animation frame
        using var second = first.Clone();
        second.Resize(new ResizeOptions(first.Width / 2, first.Height / 2));

        // Add copies the image: the caller keeps and disposes its own
        pages.Add(first);
        pages.Add(second);
        pages.Save(documentPath, new TiffEncoder { Compression = TiffCompression.Deflate });
    }

    // Loading reads the directory structure only: the pixels of a page are read when that page is decoded
    using var document = ImageCollection.Load(documentPath);
    using var page = document[1].Decode();
    return page.Size; // the second page keeps its own size, never the first one's
}
```
<!-- endref -->

- `ImageCollection.Create(ImageCollectionKind.Pages)` creates a document; `ImageCollectionKind.Representations` creates
  the sizes of an icon.
- `Add` and `Insert` copy the image: you keep and dispose your own. `RemoveAt` and `Move` edit the order.
- `ImageCollection.Load` reads only the structure of the file. Each entry describes its page without decoding it:
  `Size`, `PixelFormat`, `ColorModel`, `BitsPerComponent`, `MayHaveTransparency`, `PayloadFormat`, `Hotspot` and
  `Metadata`.
- `entry.Decode()` (or `Decode<TPixel>()`) decodes that entry only, and returns a still image that you own and
  dispose. Nothing is resized or converted to match the other entries.
- `Save` writes pages with the TIFF encoder and representations with the ICO encoder, one entry at a time. A file is
  published only once it is complete, like [`Image.Save`](../05-LoadingAndSaving/04-SavingImages.md).

## Write and read an icon

An icon holds the same drawing at several sizes, and a program picks the one it needs:

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

- `SelectBySize` returns the smallest representation at least as large as the requested size, or else the largest one.
  Ties are broken by bit depth, then by the order in the file. Nothing is resized: decode the entry and resize it if
  you need an exact size.
- A representation stores at most 256 pixels per side.
- A cursor representation has a hotspot (`entry.Hotspot`), which `Add` takes as an argument or, without one, from the
  frame of the image. An icon cannot store a hotspot, which is why the example discards it with
  `MetadataHandling.DiscardUnsupported` when it saves the icon.

## Collections and `Image.Load`

`Image.Load` and `Image.Identify` work on these files too, and give one image:

| File | `Image.Load` gives |
| --- | --- |
| TIFF document | The first page |
| ICO or CUR file | The largest representation (then the deepest), with its hotspot on `Frames[0].Metadata.Hotspot` for a cursor |

`ImageInfo.CollectionEntryCount` tells how many entries the file has. Use `ImageCollection` to reach the other ones.
The [streaming readers](../05-LoadingAndSaving/05-StreamingReadersAndWriters.md) reject TIFF, ICO and CUR files,
because their entries are not a sequence of frames.

## Lifetime of a collection

- A collection is disposable: dispose it. Its entries are borrowed and stay valid until the collection is disposed or
  the entry is removed. The images that `Decode` returns are yours, and stay valid after the collection is disposed.
- A loaded collection keeps its input open until it is disposed, because entries are decoded on demand. A file stays
  open; a stream must be seekable, is seeked freely, and its position afterward is unspecified; a span is copied once.
- `ImageCollection.Load` is synchronous only: the decoder issues many small dependent reads. `Image.LoadAsync` and
  `Image.IdentifyAsync` accept these files by buffering the whole input, up to the `MaxEncodedBytes`
  [limit](../05-LoadingAndSaving/06-ResourceLimits.md).

## See also

- [TIFF](../10-Formats/10-Tiff.md) and [ICO and CUR](../10-Formats/11-IcoAndCur.md): the options of the formats.
- [Images, frames and animations](02-ImagesFramesAndAnimations.md): animated cursors (ANI) are animations, not
  collections.
