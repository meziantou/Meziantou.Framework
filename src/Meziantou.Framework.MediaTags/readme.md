# Meziantou.Framework.MediaTags

`Meziantou.Framework.MediaTags` is a .NET library for reading and writing metadata tags in audio files.

## Supported formats

- MP3 (ID3v1 and ID3v2)
- OGG Vorbis
- OGG Opus
- FLAC
- MP4 / M4A
- WAV
- AIFF

## Usage

```c#
using Meziantou.Framework.MediaTags;

var result = MediaFile.ReadTags("song.flac");
if (!result.IsSuccess)
{
    Console.WriteLine($"{result.Error}: {result.ErrorMessage}");
    return;
}

var tags = result.Value;
Console.WriteLine($"Format: {tags.Format}");
Console.WriteLine($"Duration: {tags.Duration}");
Console.WriteLine($"Title: {tags.Title}");
Console.WriteLine($"Artist: {tags.Artist}");
```

Writing **replaces** the tags of the file, so change one field by reading the tags first, editing the result
and writing it back:

```c#
using Meziantou.Framework.MediaTags;

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

## Write semantics

`WriteTags` **replaces** the whole tag rather than merging into it. A field left `null` is *removed* from the
file, so writing a freshly constructed `MediaTagInfo` deletes every field it does not set:

```c#
// This deletes the artist, album, artwork and everything else the file had.
MediaFile.WriteTags("song.mp3", new MediaTagInfo { Title = "New Title" });
```

Use `MediaFile.RemoveTags` when removing the tags is what you actually want.

Other things worth knowing before writing:

- The file is replaced only once the new content has been written in full and flushed to disk. If the operation
  fails, the original file is left exactly as it was.
- WAV and AIFF tags are stored as an embedded ID3v2 chunk. Existing `LIST`/`INFO` (WAV) and
  `NAME`/`AUTH`/`ANNO`/`(c)`/`ISRC` (AIFF) chunks are removed, because they would otherwise be read back in
  preference to the tag that was just written.
- MP3 files get an ID3v1 tag in addition to the ID3v2 tag. ID3v1 stores at most 30 Latin-1 characters per
  field, so it truncates values the ID3v2 tag holds in full. Pass
  `new MediaTagWriteOptions { WriteId3v1Tag = false }` to suppress it.
- `MediaTagInfo.Format` describes the file the tags were read from. It is ignored when writing: the format
  comes from the file, or from the `MediaFormat` argument of the stream overloads.
- Multiplexed and chained OGG streams (a video stream alongside the audio, several concatenated logical
  streams) are refused rather than rewritten.

## Field support by format

| Field | MP3 | OGG / Opus | FLAC | MP4 | WAV / AIFF |
|---|---|---|---|---|---|
| `Title`, `Artist`, `Album`, `AlbumArtist`, `Genre`, `Year` | yes | yes | yes | yes | yes |
| `TrackNumber`, `TrackTotal`, `DiscNumber`, `DiscTotal` | yes | yes | yes | yes<sup>1</sup> | yes |
| `Comment`, `Lyrics`, `Composer`, `Conductor`, `Copyright`, `Isrc` | yes | yes | yes | yes | yes |
| `Bpm`, `IsCompilation` | yes | yes | yes | yes<sup>1</sup> | yes |
| `Duration` | read and written (`TLEN`) | read only | read only | read only | read and written (`TLEN`) |
| `Pictures` | yes | yes | yes | front cover only | yes |
| `ReplayGain` | yes | yes | yes | yes | yes |
| `MusicBrainz*` | yes | yes | yes | yes | yes |
| `CustomFields` | yes | yes | yes | yes | yes<sup>2</sup> |

<sup>1</sup> MP4 stores these in 16 bits. A value above 65535 is refused with `MediaTagError.InvalidTagData`
rather than silently wrapped.

<sup>2</sup> Stored in the embedded ID3v2 tag, not as native `INFO` chunks.

## Errors

`ReadTags` and `WriteTags` report problems with the *file* through the returned result instead of throwing.
They still throw `ArgumentNullException` / `ArgumentOutOfRangeException` for invalid arguments, and let
unexpected exceptions propagate rather than reporting a defect in this library as a corrupt file.

| `MediaTagError` | Meaning |
|---|---|
| `UnsupportedFormat` | The format could not be detected, or is not one this library writes. |
| `CorruptFile` | The file structure could not be parsed. |
| `UnexpectedEndOfStream` | The stream ended before the data it declared. |
| `InvalidTagData` | The supplied tags cannot be represented in the target format. |
| `EncodingError` | Text in the tag could not be decoded. |
| `IoError` | The file could not be read or written, or the stream does not support seeking. |

## API overview

- `MediaFile.ReadTags(...)` reads metadata from a file path or stream.
- `MediaFile.WriteTags(...)` replaces the tags of a file, or writes a retagged copy to an output stream.
- `MediaFile.RemoveTags(...)` removes every tag this library can write.
- `MediaFile.DetectFormat(...)` detects the media format from file content and extension.
- `MediaTagInfo` contains common metadata fields, embedded pictures, ReplayGain values, and custom fields.
- `MediaTagWriteOptions` controls the ID3v1 tag and the ID3v2 padding size.
- `MediaTagResult` and `MediaTagResult<T>` return operation status, error code, and message.

The stream overloads read from the stream's **current position**, so a media file embedded in a larger stream
can be tagged in place. The stream must support seeking.
