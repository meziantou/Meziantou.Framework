---
title: Limitations
description: What the barcode packages do not do.
---

- **No reading.** The packages generate QR codes and barcodes; they do not detect or decode them in photos or scans.
- **The symbologies listed** only: standard QR codes, Micro QR codes, rMQR codes, Code 39, Code 93, Code 128, EAN-8,
  EAN-13, UPC-A, Codabar and ITF. Other two-dimensional codes (Data Matrix, PDF417, Aztec) and other linear barcodes
  (UPC-E, GS1-128 application identifiers) are not supported.
- **One encoding mode per QR code.** The whole text is encoded in one mode; a text that mixes digits and lowercase
  letters is entirely encoded in the byte mode, which can give a larger code than a mix of modes would. The byte mode
  holds UTF-8 without an ECI designator.
- **No structured append**: data too large for one QR code is not split across several codes.
- **Logos in SVG only.** The PNG renderer and the image renderer do not draw logos; draw them on the image yourself.
- **No text under the bars** in the SVG and PNG renderers: the image renderer prints it, with a font.
- **QR codes only in the terminal**: the console renderer does not draw linear barcodes.
