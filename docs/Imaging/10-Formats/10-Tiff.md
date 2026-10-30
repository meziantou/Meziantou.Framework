---
title: TIFF and BigTIFF
description: Read and write TIFF documents of one or several pages, compressed without loss, as JPEG or as bilevel scans.
---

TIFF is the format of scanned documents, multi-page files and imaging pipelines. `TiffEncoder` writes every page without
loss by default, compressed with Deflate, in a layout that follows the pixel format. **It needs a seekable
destination.**

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Container | Classic TIFF and BigTIFF, both byte orders, strips and tiles, multi-page documents | Classic TIFF (default) or BigTIFF, both byte orders, strips, multi-page documents |
| Samples | Bilevel, 2, 4, 8 and 16-bit gray, gray and alpha, RGB, RGBA, palette, YCbCr; signed 8 and 16-bit samples; 16, 24 and 32-bit floating-point samples; CMYK with a color conversion | Gray, gray and alpha, RGB and RGBA at 8 or 16 bits, bilevel, palette, 32-bit floating-point |
| Compression | None, Deflate, LZW, PackBits, CCITT (modified Huffman, Group 3, Group 4), JPEG, JPEG 2000 | None, Deflate (default), LZW, PackBits, CCITT for bilevel pages, JPEG |
| Metadata | Resolution, orientation, ICC profile, XMP | The same |

`Image.Load` reads the first page. Floating-point pages load as `RgbaHalf` (16 bits) or `RgbaFloat`.

## Write a document of several pages

A document is an `ImageCollection` of pages, whose sizes and pixel formats may differ. `Image.Save` writes a document of
one page:

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

Loading a collection reads the structure of the document only; a page is decoded when you call `Decode`. See
[Pages and representations](../04-Concepts/03-PagesAndRepresentations.md). An animated image is rejected: the pages of a
document are not animation frames.

## Save a scanned document

A bilevel page stores one bit per pixel, which the CCITT compressions of fax machines code compactly; Group 4 is the
smallest. Every pixel must be opaque and exactly black or exactly white, so binarize the scan first:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Threshold;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveScanAsBilevel(string inputPath, string tiffPath, string pbmPath)
{
    using var page = Image.Load(inputPath);

    // Sauvola's threshold follows uneven lighting: each pixel is compared with the mean and the contrast of the
    // 31 x 31 pixels around it, so the text becomes black and the paper white, even in a shadow. The pixels are now
    // exactly black or white, in the same pixel format.
    page.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 31 });

    // A bilevel page with Group 4 compression, the usual format of scanned documents
    page.Save(tiffPath, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, Compression = TiffCompression.CcittGroup4 });

    // A portable bitmap: one bit per pixel
    page.Save(pbmPath, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap });
}
```
<!-- endref -->

See [Binarization](../06-Processing/09-Binarization.md). A bilevel page loads back as `Gray8`, with one bit per component.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `Compression` | `None`, `Deflate`, `Lzw`, `PackBits`, `CcittModifiedHuffman`, `CcittGroup3`, `CcittGroup4` or `Jpeg` (lossy) | `Deflate` |
| `PixelLayout` | `Auto` follows the pixel format; `Bilevel` writes one bit per pixel; `Indexed` writes a palette of at most 256 colors | `Auto` |
| `JpegQuality` | The quality of `Jpeg` compression, as `JpegEncoder.Quality` | 90 |
| `JpegChromaSubsampling` | The chroma subsampling of `Jpeg` compression | `Auto` (4:2:0) |
| `BigTiff` | Writes a BigTIFF (64-bit offsets), which fewer readers accept | `false` |
| `BigEndian` | Writes big-endian instead of little-endian files; the pixels are the same | `false` |
| `RowsPerStrip` | The rows of a strip; 0 chooses strips of about 64 KiB of samples | 0 |
| `MetadataHandling` | What to do with metadata TIFF cannot store | `Strict` |

## Exact or rejected

`TiffEncoder` has no background color, no bit-depth reduction and no color reduction: the bilevel and palette layouts
store a page exactly or reject it with an `UnsupportedImageFeatureException` before anything of the page is written.

- `Bilevel` rejects a pixel that is not opaque and exactly black or white; `Indexed` rejects a page with more than 256
  colors or a pixel that is not opaque.
- The CCITT compressions require `PixelLayout = Bilevel`, and `Jpeg` requires `Auto`: another combination throws an
  `ArgumentException`.
- `Jpeg` is the only lossy compression. It stores 8-bit samples without alpha: a 16-bit pixel format or a pixel that is
  not opaque is rejected.

## Pitfalls

- The destination must be seekable: a directory stores the offsets of the strips it describes, which are only known once
  they are written. A non-seekable stream is rejected before anything is written; paths are always seekable.
- EXIF blocks and text entries are not written: they follow `MetadataHandling`, which rejects them by default.
- Floating-point pages are written as 32-bit floats with `PixelLayout = Auto` and any compression but `Jpeg`. A
  floating-point page without an ICC profile is read as linear light, so the image must be labeled
  `ColorTransferFunction.Linear` (the label of a new floating-point image) or have an ICC profile. See
  [Pixel formats](../04-Concepts/05-PixelFormats.md).
- CMYK pages load only with `ImageDecodeOptions.CmykConversion`, which converts them to RGB (see
  [Color management](../09-Color/03-ColorManagement.md)). CMYK is never written.
- The orientation of a page is reported, never applied: call `AutoOrient`.
- Old-style JPEG compression, L\*a\*b\* data, planar storage of several samples and 64-bit floating-point samples are
  rejected with an `UnsupportedImageFeatureException`. Compressed data is never repaired: a damaged strip is an
  `InvalidImageContentException`, never a page completed with white.
- Some writers store one `BitsPerSample` value for all the samples, extra values, or no byte counts for a page of one
  uncompressed strip. These files are rejected by default; the
  [lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md) reads them the way libtiff and Apple ImageIO do.
