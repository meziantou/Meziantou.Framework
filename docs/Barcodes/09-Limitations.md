---
title: Limitations
description: What the barcode packages do not do.
---

- **No reading.** The packages generate QR codes and barcodes; they do not detect or decode them in photos or scans.
- **The symbologies listed** only: standard QR codes, Micro QR codes, rMQR codes, Code 39, Code 93, Code 128, GS1-128,
  EAN-8, EAN-13, UPC-A, Codabar and ITF. Other two-dimensional codes (Data Matrix, PDF417, Aztec) and other linear
  barcodes (UPC-E, GS1 DataBar) are not supported.
- **GS1 syntax only.** GS1-128 elements are checked against the syntax rules of element strings, not against the GS1
  dictionary of application identifiers: the format of the data and its check digits are not verified.
- **Mixed modes in standard QR codes only.** [QRCode.Create](cref:Meziantou.Framework.Barcodes.QRCode.Create) splits a text into segments of different modes,
  except a text with non-ASCII characters other than kanji and kana; Micro QR codes and rMQR codes encode the whole
  text in one mode. There is no API to choose the segments yourself. The byte mode holds UTF-8 without an ECI
  designator.
- **No structured append**: data too large for one QR code is not split across several codes.
- **Logos in SVG only.** The PNG renderer and the image renderer do not draw logos; draw them on the image yourself.
- **No text under the bars** in the SVG and PNG renderers: the image renderer prints it, with a font.
- **QR codes only in the terminal**: the console renderer does not draw linear barcodes.
