---
title: Images
description: Convert images to another format, render SVG drawings, set the quality of each format, and shrink large images.
---

## Convert an image

The tool reads and writes PNG, APNG, JPEG, GIF, WebP, AVIF, HEIC, JPEG XL, JPEG 2000, TIFF, BMP, QOI, TGA, the Netpbm
formats, ICO, CUR, ANI, OpenEXR and Radiance HDR:

```bash
Meziantou.Framework.FileConversion.Tool photo.jpg --output-format avif
Meziantou.Framework.FileConversion.Tool icon.png --output icon.webp --webp-lossless
```

A multi-page TIFF file gives one image per page: see [Output file names](06-OutputNames.md).

## Render SVG drawings

The tool reads SVG drawings (`.svg`, and `.svgz` compressed with gzip), and renders them with the
[WebPlatform](../WebPlatform/01-Introduction.md) engine, as a browser shows the file. An SVG drawing gives an image of
any output format, or a PDF document whose drawing stays vector graphics, so its text can be selected:

```bash
Meziantou.Framework.FileConversion.Tool logo.svg --output-format png
Meziantou.Framework.FileConversion.Tool "icons/*.svg" --output "png/{name}.png" --svg-width 64
Meziantou.Framework.FileConversion.Tool diagram.svg --output-format pdf
```

The image has the size of the drawing, one pixel per CSS pixel: the `width` and `height` of its root `svg` element. When
one of them is missing or is a percentage, it follows the aspect ratio of the `viewBox`; when both are, the image has the
size of the `viewBox`. A drawing with neither is 300x150 pixels. The parts the drawing does not paint are transparent,
or `--background` in a JPEG image.

| Option | Description |
| --- | --- |
| `--svg-width` | The width of the image, in pixels. The height keeps the aspect ratio |
| `--svg-height` | The height of the image, in pixels. With `--svg-width`, the image fits in both, keeping the aspect ratio |

The drawing loads the images, style sheets and fonts of its folder and subfolders, and nothing from the network.
Scripts do not run. A file without the `.svg` extension is read as SVG when it starts with `<svg`, or with an XML
declaration followed by `<svg`. The PDF page has the size of the drawing (a CSS pixel is 0.75 point): `--svg-width` and
`--svg-height` only apply to images.

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
