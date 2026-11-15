---
title: Limitations
description: What the barcode packages do not do.
---

- **Reading photographs.** [ReadQRCodes](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeImageReader.ReadQRCodes),
  [ReadBarcodes2D](cref:Meziantou.Framework.Imaging.Barcodes.Barcode2DImageReader.ReadBarcodes2D) and
  [ReadBarcodes](cref:Meziantou.Framework.Imaging.Barcodes.BarcodeImageReader.ReadBarcodes) read blurred, unevenly lit
  photographs taken at an angle, but not damaged symbols, labels on curved surfaces, symbols with modules smaller than 2
  pixels, or Data Matrix symbols marked on metal (dot peen). The rows of a PDF417 symbol must be about horizontal or
  vertical, and a linear barcode is read when one of the scan lines (at 0°, 30°, 60°, 90°, 120° and 150°) crosses all
  its bars: a long and short barcode turned between two of these angles can be missed.
- **Not every option of the symbologies is read.** The decoders do not read the FNC1 modes of GS1 QR codes, Macro
  PDF417, the structured append of Data Matrix and Aztec symbols, the Aztec runes, the Data Matrix rectangular extension
  (DMRE), nor the 2D composite components of the GS1 DataBar symbols.
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
- **Logos on QR codes only**, from the bytes of a PNG, JPEG, GIF or WebP file: not an SVG image, nor a URL. The logo
  is centered: it cannot be placed elsewhere, and the area it clears is a rectangle. The check of the cleared area
  assumes a perfect print: it does not measure how well a scanner reads the result.
- **The human-readable text in the terminal is one centered line**, written in the font of the terminal: the digits of
  EAN and UPC barcodes are grouped around longer guard bars in SVG, images and PDF only.
- **Larger SVG documents than a hand-written path**: each run of dark modules is a rectangle of absolute coordinates.
