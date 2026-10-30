---
title: Getting started
description: Install the package, read the tags of an audio file, and change a tag without losing the others.
---

## Install the package

Add the `Meziantou.Framework.MediaTags` package to a project:

```bash
dotnet add package Meziantou.Framework.MediaTags
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.MediaTags;`.

## Read the tags of a file

`MediaFile.ReadTags` detects the format of the file and returns a result. Check `IsSuccess`, then read the tags from
`Value`:

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

When the file cannot be read, `Error` and `ErrorMessage` tell why. See [Errors](03-ReadingTags.md#errors).

## Change a tag

`MediaFile.WriteTags` replaces all the tags of the file with the ones you give. To change one field, read the tags,
change them, and write them back:

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

Writing a new `MediaTagInfo` instead would remove every field it does not set. See
[Writing tags](04-WritingTags.md).

## Next steps

- [Reading tags](03-ReadingTags.md): the fields, pictures, custom fields, streams and errors.
- [Writing tags](04-WritingTags.md): how writing replaces the tags, streams, removing tags and the write options.
- [Format support](05-FormatSupport.md): which fields each format stores.
