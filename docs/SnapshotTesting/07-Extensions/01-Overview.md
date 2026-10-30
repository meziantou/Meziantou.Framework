---
title: Extensions
description: Compare images by their pixels, PDF documents page by page, and source generator output with dedicated packages.
---

The extension packages add serializers, comparers and converters to [SnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings) for values that a text or a byte
comparison does not handle well. Each one has a single setup method, to call once on [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) in a
module initializer.

| Package | Setup | Use it to |
| --- | --- | --- |
| `Meziantou.Framework.SnapshotTesting.Imaging` | [AddImaging()](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging) | Compare images by their pixels, exactly or by similarity, and store the frames of GIF and ICO files as PNG snapshots. See [Images](02-Images.md) |
| `Meziantou.Framework.SnapshotTesting.Pdf` | [AddPdf()](cref:Meziantou.Framework.SnapshotTesting.Pdf.SnapshotSettingsPdfExtensions.AddPdf) | Store a PDF document as one PNG image per page and a JSON file of metadata. See [PDF documents](03-Pdf.md) |
| `Meziantou.Framework.SnapshotTesting.Roslyn` | [AddRoslyn()](cref:Meziantou.Framework.SnapshotTesting.Roslyn.SnapshotSettingsRoslynExtensions.AddRoslyn) | Validate source generator results, syntax trees and diagnostics. See [Roslyn](04-Roslyn.md) |

Register them once for the whole test assembly, not inside a test method. [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) is shared by every
test of the process: registering from a test races against the tests that are serializing a snapshot at the same time,
and repeated calls add the serializers and converters again every time.

To write your own extension, see [Serializers and comparers](../05-SerializersAndComparers.md).
