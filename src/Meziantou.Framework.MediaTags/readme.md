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
Console.WriteLine($"Title: {tags.Title}");
Console.WriteLine($"Artist: {tags.Artist}");
```

```c#
using Meziantou.Framework.MediaTags;

var tags = new MediaTagInfo
{
    Title = "New Title",
    Artist = "New Artist",
    Album = "New Album",
    Year = 2026,
    Genre = "Rock",
    TrackNumber = 1,
    TrackTotal = 10,
};

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

## API overview

- `MediaFile.ReadTags(...)` reads metadata from a file path or stream.
- `MediaFile.WriteTags(...)` writes tags to a file path or to an output stream.
- `MediaFile.DetectFormat(...)` detects the media format from file content and extension.
- `MediaTagInfo` contains common metadata fields, embedded pictures, ReplayGain values, and custom fields.
- `MediaTagResult` and `MediaTagResult<T>` return operation status, error code, and message.
