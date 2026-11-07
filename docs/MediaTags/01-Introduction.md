---
title: Introduction
description: Read and write the title, artist, album, artwork and other tags of audio files with one API for every format.
---

`Meziantou.Framework.MediaTags` reads and writes the metadata of audio files: title, artist, album, track numbers,
artwork, lyrics, ReplayGain values and more. One type, [MediaTagInfo](cref:Meziantou.Framework.MediaTags.MediaTagInfo), holds the tags of every format, so the same code
works for an MP3 file and a FLAC file.

## Supported formats

| Format | [MediaFormat](cref:Meziantou.Framework.MediaTags.MediaFormat) | Tags |
| --- | --- | --- |
| MP3 | [Mp3](cref:Meziantou.Framework.MediaTags.MediaFormat.Mp3) | ID3v2, and ID3v1 |
| OGG Vorbis | [OggVorbis](cref:Meziantou.Framework.MediaTags.MediaFormat.OggVorbis) | Vorbis comments |
| OGG Opus | [OggOpus](cref:Meziantou.Framework.MediaTags.MediaFormat.OggOpus) | Vorbis comments |
| FLAC | [Flac](cref:Meziantou.Framework.MediaTags.MediaFormat.Flac) | Vorbis comments and picture blocks |
| MP4 / M4A | [Mp4](cref:Meziantou.Framework.MediaTags.MediaFormat.Mp4) | iTunes-style metadata |
| WAV | [Wav](cref:Meziantou.Framework.MediaTags.MediaFormat.Wav) | An embedded ID3v2 tag, or `LIST`/`INFO` chunks |
| AIFF | [Aiff](cref:Meziantou.Framework.MediaTags.MediaFormat.Aiff) | An embedded ID3v2 tag |

The fields each format stores are listed in [Format support](05-FormatSupport.md).

## What you can do with it

- **Read the tags** of a file or a stream, with the duration and the embedded pictures. See
  [Reading tags](03-ReadingTags.md).
- **Change the tags** of a file, safely: the file is only replaced once the new content is complete. See
  [Writing tags](04-WritingTags.md).
- **Remove the tags** of a file.
- **Detect the format** of a file from its content.

Problems with a file, such as a corrupt or unsupported file, are returned as a result rather than thrown, so a program
that walks a music library does not need a `try` block for each file.

## Namespace

Every type is in the [Meziantou.Framework.MediaTags](cref:Meziantou.Framework.MediaTags) namespace: [MediaFile](cref:Meziantou.Framework.MediaTags.MediaFile), [MediaTagInfo](cref:Meziantou.Framework.MediaTags.MediaTagInfo), [MediaPicture](cref:Meziantou.Framework.MediaTags.MediaPicture),
[ReplayGainInfo](cref:Meziantou.Framework.MediaTags.ReplayGainInfo), [MediaTagWriteOptions](cref:Meziantou.Framework.MediaTags.MediaTagWriteOptions) and the result types.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read the tags of a file and change one.
- [Limitations](06-Limitations.md): what the library does not do.
