---
title: Writing tags
description: Change the tags of a file without losing the others, write a retagged copy to a stream, remove tags, and control the ID3 tags of MP3 files.
---

## Writing replaces the tags

`MediaFile.WriteTags` does not merge: the `MediaTagInfo` you write becomes the whole tag, and a field left `null` is
removed from the file:

<!-- ref:../../tests/Meziantou.Framework.MediaTags.Tests/FlacTests.cs;region=MediaFile_WriteReplacesTheTags;format=md-fenced-code-block;dedent=true -->
```csharp
using var input = File.OpenRead("TestFiles/basic.flac");
using var output = new MemoryStream();
var result = MediaFile.WriteTags(input, output, new MediaTagInfo { Title = "Only a title" }, MediaFormat.Flac);
Assert.True(result.IsSuccess, result.ErrorMessage);

output.Position = 0;
var written = MediaFile.ReadTags(output).Value;
Assert.Equal("Only a title", written.Title);
Assert.Null(written.Artist); // The file had an artist: it is removed
```
<!-- endref -->

So writing a new `MediaTagInfo` to a file deletes everything it does not set:

<!-- ref:../../samples/Meziantou.Framework.MediaTags.Samples/ReadmeExamples.cs;region=MediaFile_ReplaceTags;format=md-fenced-code-block;dedent=true -->
```csharp
// This deletes the artist, album, artwork and everything else the file had.
MediaFile.WriteTags("song.mp3", new MediaTagInfo { Title = "New Title" });
```
<!-- endref -->

## Change some fields

Read the tags first, change the fields, and write the same object back:

<!-- ref:../../samples/Meziantou.Framework.MediaTags.Samples/ReadmeExamples.cs;region=MediaFile_UpdateTags;format=md-fenced-code-block;dedent=true -->
```csharp
var read = MediaFile.ReadTags("song.mp3");
if (!read.IsSuccess)
    return;

var tags = read.Value;
tags.Title = "New Title";
tags.Year = 2026;

tags.Pictures.Clear();
tags.Pictures.Add(new MediaPicture
{
    PictureType = MediaPictureType.FrontCover,
    MimeType = "image/png",
    Description = "Album cover",
    Data = File.ReadAllBytes("cover.png"),
});

var writeResult = MediaFile.WriteTags("song.mp3", tags);
if (!writeResult.IsSuccess)
{
    Console.WriteLine($"{writeResult.Error}: {writeResult.ErrorMessage}");
}
```
<!-- endref -->

- `Pictures` and `CustomFields` are collections: clear them, or add and remove items.
- The properties check their values: setting `Year` outside 0 to 9999, or a negative track number, disc number or
  `Bpm`, throws an `ArgumentOutOfRangeException`.
- `Format` is ignored when writing: the format comes from the file.
- A `Duration` the library computed from the audio is not written as a tag, so it cannot override the audio on a later
  read. A duration read from a tag, or one you set, is written by the formats that store it.

Some data is kept even though `MediaTagInfo` does not show it: the ID3v2 frames and the MP4 items this library does not
read, such as the publisher or the sort names, are copied from the existing tag. See
[Format support](05-FormatSupport.md).

## A safe replacement

`WriteTags` writes the new file next to the original, and replaces the original only once the new content has been
written in full and flushed to disk. If anything fails, the original file is left as it was, and the result says
why. The file keeps its permissions, and a symbolic link is written through to its target.

The new file is created in the folder of the original, so the program needs the right to create files there, not only
to write the file.

## Write to a stream

`WriteTags(inputStream, outputStream, tags, format)` reads the original from one stream, from its current position, and
writes the retagged file to another. The input stream must support seeking:

<!-- ref:../../tests/Meziantou.Framework.MediaTags.Tests/FlacTests.cs;region=MediaFile_WriteToStream;format=md-fenced-code-block;dedent=true -->
```csharp
var tags = MediaFile.ReadTags("TestFiles/basic.flac").Value;
tags.Title = "New Title";
tags.CustomFields["MOOD"] = "Calm";
tags.ReplayGain = new ReplayGainInfo { TrackGain = -6.5, TrackPeak = 0.98 };

using var input = File.OpenRead("TestFiles/basic.flac");
using var output = new MemoryStream(); // Such as File.Create("copy.flac")
var result = MediaFile.WriteTags(input, output, tags, MediaFormat.Flac);
Assert.True(result.IsSuccess, result.ErrorMessage);

output.Position = 0;
var written = MediaFile.ReadTags(output).Value;
Assert.Equal("New Title", written.Title);
Assert.Equal("Test Artist", written.Artist); // Kept, because the tags were read first
Assert.Equal("Calm", written.CustomFields["MOOD"]);
Assert.Equal(-6.5, written.ReplayGain?.TrackGain);
```
<!-- endref -->

When the result is a failure, discard what was written to the output stream: it is not a valid file.

## Remove the tags

`MediaFile.RemoveTags` removes every tag this library can write, including the ID3v2 frames it does not read:

<!-- ref:../../samples/Meziantou.Framework.MediaTags.Samples/ReadmeExamples.cs;region=MediaFile_RemoveTags;format=md-fenced-code-block;dedent=true -->
```csharp
var result = MediaFile.RemoveTags("song.mp3");
if (!result.IsSuccess)
{
    Console.WriteLine($"{result.Error}: {result.ErrorMessage}");
}
```
<!-- endref -->

`RemoveTags(inputStream, outputStream, format)` does the same between two streams.

## Write options

`MediaTagWriteOptions` controls the tags of MP3 files:

<!-- ref:../../samples/Meziantou.Framework.MediaTags.Samples/ReadmeExamples.cs;region=MediaFile_WriteOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var tags = MediaFile.ReadTags("song.mp3").Value;
tags.Title = "A title longer than the 30 characters of an ID3v1 field";

var options = new MediaTagWriteOptions
{
    WriteId3v1Tag = false,  // Only write the ID3v2 tag
    Id3v2PaddingSize = 0,   // The smallest possible tag
};
MediaFile.WriteTags("song.mp3", tags, options);
```
<!-- endref -->

| Option | Default | Effect |
| --- | --- | --- |
| `WriteId3v1Tag` | `true` | Appends an ID3v1 tag to MP3 files, for players that read nothing else. ID3v1 stores at most 30 Latin-1 characters per field, so it truncates what the ID3v2 tag holds in full. |
| `Id3v2PaddingSize` | `1024` | The number of empty bytes after the ID3v2 tag, so another tagger can grow it without rewriting the file. `0` gives the smallest tag; a negative value throws. |

## When writing fails

`WriteTags` returns the same errors as `ReadTags`; see [Errors](03-ReadingTags.md#errors). `InvalidTagData` means the
tags cannot be stored in the format of the file, such as a track number above 65535 in an MP4 file, or a Vorbis custom
field whose name contains `=`.
