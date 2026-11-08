---
title: Limitations
description: What the barcode packages do not do.
---

- **No reading.** The packages generate QR codes and barcodes; they do not detect or decode them in photos or scans.
- **The symbologies listed** only: standard QR codes, Micro QR codes, rMQR codes, Code 39, Code 93, Code 128, GS1-128,
  EAN-8, EAN-13, UPC-A, UPC-E, GS1 DataBar, Codabar and ITF. Other two-dimensional codes (Data Matrix, PDF417, Aztec)
  are not supported.
- **GS1 dictionary checks are opt-in.** GS1-128 and GS1 DataBar Expanded elements are checked against the syntax rules
  of element strings unless you pass [Gs1ValidationMode.Dictionary](cref:Meziantou.Framework.Barcodes.Gs1ValidationMode.Dictionary). The
  dictionary does not know which GS1 Company Prefixes are allocated: a key is checked for its format and check digit,
  not for its owner.
- **Mixed modes in standard QR codes only.** [QRCode.Create](cref:Meziantou.Framework.Barcodes.QRCode.Create) splits a text into segments of different modes,
  except a text with non-ASCII characters other than kanji and kana; Micro QR codes and rMQR codes encode the whole
  text in one mode. There is no API to choose the segments yourself.
- **ECI in standard QR codes only, and opt-in.** Without [QRCodeOptions.EciAssignmentNumber](cref:Meziantou.Framework.Barcodes.QRCodeOptions.EciAssignmentNumber), the byte mode holds
  UTF-8 without an ECI designator. Micro QR codes and rMQR codes never have one.
- **No structured append**: data too large for one QR code is not split across several codes.
- **Logos in SVG only.** The PNG renderer and the image renderer do not draw logos; draw them on the image yourself.
- **No text under the bars** in the SVG and PNG renderers: the image renderer prints it, with a font.
- **QR codes only in the terminal**: the console renderer does not draw linear barcodes.
