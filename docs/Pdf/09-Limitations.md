---
title: Limitations
description: What the library does not do, so that you can plan around it.
---

## Creating documents

- The content builder does not lay out text: it shows each text where it is told to. Line breaking, justification and
  tables are up to the application: see [Wrap a paragraph](03-CreatingDocuments/03-Text.md#wrap-a-paragraph).
- The standard 14 fonts show the characters of `WinAnsiEncoding` (Symbol and ZapfDingbats their own); other characters
  need an embedded font.
- The fonts with CFF2 outlines, the bitmap fonts and the instances of variable fonts are not embedded.
- The API has no method to copy pages from one document to another, nor to create interactive form fields.

## Reading and extracting

- The public-key security handlers (documents encrypted for certificates) are not supported; only the standard security
  handler (passwords) is.
- The text extraction follows the order of the content and joins the glyphs into lines by their positions; it does not
  detect columns, and it does not recognize the text of scanned pages.
- The library reports the permissions of an encrypted document, but does not enforce them.

## Conformance and signatures

- The save checks what the conformance levels require of the document as a whole. The content (the order of the
  structure, the alternate descriptions, the colors of the annotations of PDF/A-1, the values of the numbers) must follow
  the other rules of the level: validate the documents with veraPDF.
- Signatures are invisible: the library does not draw an appearance for the signature field. It writes PAdES B-B and B-T
  signatures; the levels with validation data (B-LT, B-LTA) are not written.
- The verification of a signature does not decide whether the signer is trusted: the application builds the certificate
  chain.
- The writer encrypts the embedded files with the method of the other streams.
