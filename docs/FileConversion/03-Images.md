---
title: Images
description: Convert images to another format, set the quality of each format, and shrink large images.
---

## Convert an image

The tool reads and writes PNG, APNG, JPEG, GIF, WebP, AVIF, HEIC, JPEG XL, JPEG 2000, TIFF, BMP, QOI, TGA, the Netpbm
formats, ICO, CUR, ANI, OpenEXR and Radiance HDR:

```bash
Meziantou.Framework.FileConversion.Tool photo.jpg --output-format avif
Meziantou.Framework.FileConversion.Tool icon.png --output icon.webp --webp-lossless
```

A multi-page TIFF file gives one image per page: see [Output file names](06-OutputNames.md).

## Set the quality

Each format has its options. They only apply to the files of their format:

| Option | Format | Values |
| --- | --- | --- |
| `--jpeg-quality` | JPEG | 1 to 100, 90 by default |
| `--background` | JPEG | The color the transparent pixels are composited over, `#ffffff` by default |
| `--webp-quality`, `--webp-lossless` | WebP | 0 to 100, or a lossless image |
| `--avif-quality`, `--avif-effort`, `--avif-lossless` | AVIF | The quality (0 to 100), the compression effort (0, the fastest, to 9, the smallest), or a lossless image |
| `--heic-quality` | HEIC | 0 to 100 |
| `--jxl-quality`, `--jxl-lossless` | JPEG XL | 0 to 100, or a lossless image |
| `--png-compression-level` | PNG | `Optimal`, `Fastest`, `NoCompression` or `SmallestSize` |
| `--tiff-compression` | TIFF | `Deflate`, `Lzw`, `PackBits`, `Jpeg`, `None`... |
| `--gif-max-colors` | GIF | 2 to 256 colors |

```bash
Meziantou.Framework.FileConversion.Tool "photos/*.png" --output-format jpg --jpeg-quality 80
```

JPEG has no transparency: the transparent pixels are composited over `--background`.

## Shrink the images

`--max-width` and `--max-height` shrink the images that are larger, keeping their aspect ratio. The smaller images are
not enlarged:

```bash
Meziantou.Framework.FileConversion.Tool "photos/*.jpg" --output "thumbnails/{name}.webp" --max-width 400 --max-height 400
```

They apply to every output: the images of a PDF or comic book output too. A JPEG file that is shrunk is decoded and encoded
again.
