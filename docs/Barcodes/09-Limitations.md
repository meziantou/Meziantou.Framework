---
title: Limitations
description: What the barcode packages do not do.
---

- **Reading from modules and scan lines only.** [QRCodeDecoder](cref:Meziantou.Framework.Barcodes.QRCodeDecoder),
  [Barcode2DDecoder](cref:Meziantou.Framework.Barcodes.Barcode2DDecoder) and
  [BarcodeDecoder](cref:Meziantou.Framework.Barcodes.BarcodeDecoder) read the symbols from their modules, or from the
  widths of the bars of scan lines. The packages do not yet detect symbols in photos or scans. The decoders do not read
  the FNC1 modes of GS1 QR codes, Macro PDF417, the structured append of Data Matrix and Aztec symbols, the Aztec runes,
  nor the 2D composite components of the GS1 DataBar symbols.
- **The symbologies listed** only: standard QR codes, Micro QR codes, rMQR codes, Data Matrix, PDF417, Aztec, Code 39,
  Code 93, Code 128, GS1-128, EAN-8, EAN-13, UPC-A, UPC-E, GS1 DataBar, Codabar and ITF. The variants of the
  two-dimensional codes are not supported (Data Matrix rectangular extension, Macro and Micro PDF417, Aztec Runes).
- **GS1 dictionary checks are opt-in.** GS1-128 and GS1 DataBar Expanded elements are checked against the syntax rules
  of element strings unless you pass [Gs1ValidationMode.Dictionary](cref:Meziantou.Framework.Barcodes.Gs1ValidationMode.Dictionary). The
  dictionary does not know which GS1 Company Prefixes are allocated: a key is checked for its format and check digit,
  not for its owner.
- **No automatic segments for other non-ASCII texts without ECI.** [QRCode.Create](cref:Meziantou.Framework.Barcodes.QRCode.Create), [QRCode.CreateMicroQR](cref:Meziantou.Framework.Barcodes.QRCode.CreateMicroQR) and
  [QRCode.CreateRMQR](cref:Meziantou.Framework.Barcodes.QRCode.CreateRMQR) keep a text with non-ASCII characters other than kanji and kana in one byte segment, unless
  it has an ECI designator. Pass [QRCodeSegment](cref:Meziantou.Framework.Barcodes.QRCodeSegment) instances to choose the segments yourself.
- **ECI in standard QR codes only, and opt-in.** Without [QRCodeOptions.EciAssignmentNumber](cref:Meziantou.Framework.Barcodes.QRCodeOptions.EciAssignmentNumber), the byte mode holds
  UTF-8 without an ECI designator. Micro QR codes and rMQR codes never have one.
- **Structured append in standard QR codes only**: [QRCode.CreateStructuredAppend](cref:Meziantou.Framework.Barcodes.QRCode.CreateStructuredAppend) splits data across up to 16 QR
  codes, but data too large for one Micro QR, rMQR, Data Matrix, PDF417 or Aztec symbol is not split across several
  symbols.
- **ECI for text only in two-dimensional symbols**: [Barcode2D](cref:Meziantou.Framework.Barcodes.Barcode2D) declares UTF-8 for a text that is not ASCII, and
  encodes bytes without an ECI designator: there is no option to declare another encoding.
- **Logos in SVG only.** The PNG renderer and the image renderer do not draw logos; draw them on the image yourself.
- **No text under the bars** in the SVG and PNG renderers: the image renderer prints it, with a font.
- **QR codes only in the terminal**: the console renderer does not draw linear barcodes or the other two-dimensional
  symbols.
