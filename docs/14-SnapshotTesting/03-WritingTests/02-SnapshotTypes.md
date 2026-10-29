---
title: Snapshot types
description: Store a value as text, JSON, SVG, PNG or any other format, and understand how each format is compared.
---

A `SnapshotType` gives the format of a snapshot: the extension of its file and, for some serializers, how the value is
written. Without a type, a value is stored as text (`SnapshotType.Default`, a `.txt` file).

## Validate a file in its own format

Pass the type after the value. A string is stored as it is, a `byte[]` or a `Stream` is stored as its bytes:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_TypedSnapshots;format=md-fenced-code-block;dedent=true -->
```csharp
var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"); // A 1x1 PNG image
var svgText = """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10" fill="red" /></svg>""";

Snapshot.Validate(imageBytes, SnapshotType.Png);
Snapshot.Validate(svgText, SnapshotType.Svg);
```
<!-- endref -->

A type also converts from a string, which is the extension of the file: `Snapshot.Validate(spec, "yaml")` stores a
`.yaml` file. `SnapshotType.Create("cs")` does the same, and `SnapshotType.Create(type, mimeType, displayName)` also
sets the `MimeType` and `DisplayName` of the type. A leading dot is ignored, and `jpg` and `tif` are the same types as
`jpeg` and `tiff`.

The built-in types are `Default` (`txt`), `Svg`, `Png`, `Bmp`, `Jpeg`, `Tiff`, `Webp`, `Gif`, `Ico`, `Avif`, `Heic`,
`JpegXl` (`jxl`), `Jpeg2000` (`jp2`), `Pdf`, and `None`, a snapshot without a format, stored as a `.bin` file.

## How each format is compared

- **Text formats** are compared without regard to line endings: `\r\n`, `\r` and `\n` are equivalent, and a leading
  UTF-8 byte order mark is ignored. A verified file whose line endings were changed by an editor, a merge tool or git
  still matches. This applies to every known text format: `txt`, `json`, `yaml`, `xml`, `html`, `svg`, `md`, `csv`,
  `cs`, `vb`, `razor`, `sql`, and their common variants.
- **Other formats**, such as `png`, `pdf` or `bin`, are compared byte for byte.

A comparer registered for a format replaces this behavior. Image formats are compared byte for byte unless you register
the image comparer of `Meziantou.Framework.SnapshotTesting.Imaging`, which compares their pixels; PDF documents are
compared page by page with `Meziantou.Framework.SnapshotTesting.Pdf`. See [Extensions](../07-Extensions/01-Overview.md)
and [Serializers and comparers](../05-SerializersAndComparers.md).

## Text and binary snapshots

Scrubbers, which remove unstable values from a snapshot (see
[Deterministic snapshots](06-DeterministicSnapshots.md)), only apply to text snapshots. A snapshot in a known binary
format (`png`, `jpeg`, `pdf`, `zip`, `bin`, ...) is never scrubbed, even when its bytes happen to be valid UTF-8. A
snapshot in an unknown format is scrubbed when its content is UTF-8 text.
