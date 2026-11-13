---
title: Introduction
description: Generate QR codes, Micro QR codes, rectangular Micro QR codes, Data Matrix, PDF417, Aztec and linear barcodes, and render them.
---

`Meziantou.Framework.Barcodes` generates QR codes, Data Matrix, PDF417 and Aztec symbols, and linear barcodes in .NET, and renders them as SVG, as PNG files,
or as text for a terminal. It is written entirely in C# and has no native dependency.
`Meziantou.Framework.Imaging.Barcodes` adds the rendering to the images of
[Meziantou.Framework.Imaging](../Imaging/01-Introduction.md): any image format, and drawing on existing images.

## What you can do with it

- **Generate QR codes** of three kinds: standard QR codes, Micro QR codes for very small spaces, and rectangular Micro
  QR codes (rMQR) for narrow ones. The version, the encoding and the mask are chosen for you. See [QR codes](03-QRCodes.md).
- **Generate Data Matrix, PDF417 and Aztec symbols**, including GS1 Data Matrix. See
  [Data Matrix, PDF417 and Aztec](10-TwoDimensionalBarcodes.md).
- **Generate linear barcodes**: Code 39, Code 93, Code 128, GS1-128, EAN-8, EAN-13, UPC-A, UPC-E (with their 2- or
  5-digit add-ons), GS1 DataBar (Omnidirectional, Stacked, Limited, Expanded), Codabar and Interleaved 2 of 5. Check digits are computed or checked. See [Linear barcodes](04-LinearBarcodes.md).
- **Build the content** of QR codes that phones understand: Wi-Fi networks, contacts, events, payments, two-factor
  authentication. See [QR code content](05-QRCodeContent.md).
- **Render** as SVG or PNG with your colors and sizes, or as text in a terminal. See
  [SVG and PNG](06-SvgAndPng.md) and [Terminal output](07-TerminalOutput.md).
- **Draw on images** with any brush, and print the human-readable text under a barcode with the bundled OCR-A and
  OCR-B fonts. See [Images](08-Images.md).
- **Read QR codes from images**, and QR codes, Data Matrix, PDF417, Aztec symbols and linear barcodes from their
  modules or from scan lines, with error correction. See [Reading](11-Reading.md).

See [Limitations](09-Limitations.md) for what the packages do not do.

## Packages and namespaces

| Package | Namespace | Contains |
| --- | --- | --- |
| `Meziantou.Framework.Barcodes` | [Meziantou.Framework.Barcodes](cref:Meziantou.Framework.Barcodes) | QR codes, Data Matrix, PDF417 and Aztec symbols, barcodes, content helpers, SVG, PNG and text renderers, decoders |
| `Meziantou.Framework.Imaging.Barcodes` | [Meziantou.Framework.Imaging.Barcodes](cref:Meziantou.Framework.Imaging.Barcodes) | Rendering to images and drawing on a canvas, OCR fonts, reading the codes of images |

`Meziantou.Framework.Barcodes` writes PNG files on its own. Add `Meziantou.Framework.Imaging.Barcodes` only when you
need another format, effects such as gradients, or to draw a code on an existing image.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and generate your first QR code and barcode.
