---
title: Reading tags
description: Read the fields, pictures and custom fields of an audio file or stream, detect its format, and handle files that cannot be read.
---

`MediaFile.ReadTags` reads the tags of a file, or of a stream, into a `MediaTagInfo`:

<!-- ref:../../tests/Meziantou.Framework.MediaTags.Tests/FlacTests.cs;region=MediaFile_ReadTags;format=md-fenced-code-block;dedent=true -->
```csharp
var result = MediaFile.ReadTags("TestFiles/basic.flac");
Assert.True(result.IsSuccess, $"{result.Error}: {result.ErrorMessage}");

var tags = result.Value;
Assert.Equal(MediaFormat.Flac, tags.Format);
Assert.True(tags.Duration > TimeSpan.Zero);
Assert.Equal("Test Title", tags.Title);
Assert.Equal("Test Artist", tags.Artist);
```
<!-- endref -->

## The fields

A field is `null` when the file does not have it.

| Property | Content |
| --- | --- |
| `Title`, `Artist`, `Album`, `AlbumArtist`, `Genre` | Text. |
| `Year` | The year of release, between 0 and 9999. |
| `TrackNumber`, `TrackTotal`, `DiscNumber`, `DiscTotal` | The position of the track on its disc, and of the disc in its set. |
| `Comment`, `Lyrics`, `Composer`, `Conductor`, `Copyright` | Text. |
| `Isrc` | The International Standard Recording Code of the track. |
| `Bpm`, `IsCompilation` | The tempo, and whether the album is a compilation. |
| `Duration` | The duration of the audio, read from the audio stream or from a tag. |
| `Pictures` | The embedded pictures, such as the album art. |
| `ReplayGain` | The track and album gains, in dB, and peaks. |
| `MusicBrainzTrackId`, `MusicBrainzArtistId`, `MusicBrainzAlbumId`, `MusicBrainzReleaseGroupId` | MusicBrainz identifiers. |
| `CustomFields` | The other text fields, by name. |
| `Format` | The format of the file the tags were read from. |

Not every format stores every field; see [Format support](05-FormatSupport.md).

## Pictures

Each `MediaPicture` has its `Data`, its `MimeType`, an optional `Description`, and a `PictureType` that tells what it
shows: `FrontCover`, `BackCover`, `Artist`… Files do not always set the type, so fall back to any picture when there is
no front cover:

<!-- ref:../../tests/Meziantou.Framework.MediaTags.Tests/FlacTests.cs;region=MediaFile_ReadPictures;format=md-fenced-code-block;dedent=true -->
```csharp
var tags = MediaFile.ReadTags("TestFiles/with_art.flac").Value;

// Prefer the front cover, and fall back to any picture
var cover = tags.Pictures.FirstOrDefault(picture => picture.PictureType == MediaPictureType.FrontCover)
    ?? tags.Pictures.FirstOrDefault();
Assert.NotNull(cover);
Assert.Equal("image/png", cover.MimeType);
Assert.NotEmpty(cover.Data); // Save it with File.WriteAllBytes("cover.png", cover.Data)
```
<!-- endref -->

## Custom fields

`CustomFields` holds the text fields that have no property, such as a `MOOD` Vorbis comment, keyed by name with a
case-insensitive comparison. It also holds a field whose value cannot be parsed, such as a Vorbis `TRACKNUMBER=A1`,
so that writing the tags back keeps it.

## Errors

`ReadTags` returns the problems of the file in the result instead of throwing them:

<!-- ref:../../tests/Meziantou.Framework.MediaTags.Tests/FlacTests.cs;region=MediaFile_ReadErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var result = MediaFile.ReadTags("TestFiles/cover.png");
Assert.False(result.IsSuccess);
Assert.Equal(MediaTagError.UnsupportedFormat, result.Error);
Assert.NotNull(result.ErrorMessage);

// Value throws when the operation failed
Assert.Throws<InvalidOperationException>(() => result.Value);

var missingFile = MediaFile.ReadTags("TestFiles/missing.mp3");
Assert.Equal(MediaTagError.IoError, missingFile.Error);
```
<!-- endref -->

| `MediaTagError` | Meaning |
| --- | --- |
| `UnsupportedFormat` | The format could not be detected, or is not one this library writes. |
| `CorruptFile` | The structure of the file could not be parsed. |
| `UnexpectedEndOfStream` | The stream ended before the data it declared. |
| `InvalidTagData` | The tags cannot be represented in the format of the file. |
| `EncodingError` | Text in the tag could not be decoded. |
| `IoError` | The file could not be read or written, or the stream cannot seek. |

`Value` throws an `InvalidOperationException` when the operation failed. The methods still throw for invalid
arguments, such as a `null` path, and let unexpected exceptions through rather than reporting a defect of the library
as a corrupt file.

## Read a music library

Because problems are results, a loop over a folder needs no `try` block. `UnsupportedFormat` tells which files are not
audio files:

<!-- ref:../../samples/Meziantou.Framework.MediaTags.Samples/ReadmeExamples.cs;region=MediaFile_ScanLibrary;format=md-fenced-code-block;dedent=true -->
```csharp
foreach (var path in Directory.EnumerateFiles("Music", "*", SearchOption.AllDirectories))
{
    var result = MediaFile.ReadTags(path);
    if (result.Error is MediaTagError.UnsupportedFormat)
        continue; // Not an audio file, such as a playlist or a cover image

    if (!result.IsSuccess)
    {
        Console.WriteLine($"{path}: {result.Error}: {result.ErrorMessage}");
        continue;
    }

    var tags = result.Value;
    Console.WriteLine($"{tags.Artist} - {tags.Album} - {tags.TrackNumber}. {tags.Title} ({tags.Duration})");
}
```
<!-- endref -->

## Detect the format

`MediaFile.DetectFormat` reads the first bytes of a file to recognize its format, and falls back to its extension. It
returns `null` for a file that is not a supported audio file:

<!-- ref:../../tests/Meziantou.Framework.MediaTags.Tests/FlacTests.cs;region=MediaFile_DetectFormat;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(MediaFormat.OggOpus, MediaFile.DetectFormat("TestFiles/basic.opus"));
Assert.Null(MediaFile.DetectFormat("TestFiles/cover.png"));
```
<!-- endref -->

`ReadTags` detects the format itself: call `DetectFormat` only when you need the format without the tags.

## Read from a stream

`ReadTags(Stream, MediaFormat?)` reads the tags of a stream from its current position, so it can read a file embedded
in a larger stream. The stream must support seeking; otherwise the result is an `IoError`. Pass the format when you
know it, or `null` to detect it from the content. `DetectFormat(Stream)` restores the position of the stream.
