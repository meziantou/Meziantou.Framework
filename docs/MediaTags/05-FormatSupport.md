---
title: Format support
description: Which fields each audio format stores, and what each format does when tags are written.
---

## Fields by format

| Field | MP3 | OGG / Opus | FLAC | MP4 | WAV / AIFF |
| --- | --- | --- | --- | --- | --- |
| [Title](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Title), [Artist](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Artist), [Album](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Album), [AlbumArtist](cref:Meziantou.Framework.MediaTags.MediaTagInfo.AlbumArtist), [Genre](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Genre), [Year](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Year) | yes | yes | yes | yes | yes |
| [TrackNumber](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TrackNumber), [TrackTotal](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TrackTotal), [DiscNumber](cref:Meziantou.Framework.MediaTags.MediaTagInfo.DiscNumber), [DiscTotal](cref:Meziantou.Framework.MediaTags.MediaTagInfo.DiscTotal) | yes | yes | yes | yes <sup>1</sup> | yes |
| [Comment](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Comment), [Lyrics](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Lyrics), [Composer](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Composer), [Conductor](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Conductor), [Copyright](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Copyright), [Isrc](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Isrc) | yes | yes | yes | yes | yes |
| [Bpm](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Bpm), [IsCompilation](cref:Meziantou.Framework.MediaTags.MediaTagInfo.IsCompilation) | yes | yes | yes | yes <sup>1</sup> | yes |
| [Duration](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Duration) | read and written <sup>2</sup> | read only | read only | read only | read and written <sup>2</sup> |
| [Pictures](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Pictures) | yes | yes | yes | front cover only | yes |
| [ReplayGain](cref:Meziantou.Framework.MediaTags.MediaTagInfo.ReplayGain) | yes | yes | yes | yes | yes |
| `MusicBrainz*` | yes | yes | yes | yes | yes |
| [CustomFields](cref:Meziantou.Framework.MediaTags.MediaTagInfo.CustomFields) | yes | yes | yes | yes | yes <sup>3</sup> |

<sup>1</sup> MP4 stores these values in 16 bits. A value above 65535 is refused with [MediaTagError.InvalidTagData](cref:Meziantou.Framework.MediaTags.MediaTagError.InvalidTagData)
rather than wrapped.

<sup>2</sup> Stored in a `TLEN` frame. A duration computed from the audio is not written; see
[Change some fields](04-WritingTags.md#change-some-fields).

<sup>3</sup> Stored in the embedded ID3v2 tag, not as native chunks.

## MP3

- Tags are written as an ID3v2 tag, and an ID3v1 tag at the end of the file unless
  [`WriteId3v1Tag`](04-WritingTags.md#write-options) is `false`. ID3v1 truncates each field to 30 Latin-1 characters.
- The ID3v2 frames this library does not read, such as the publisher, the sort names, ratings, `UFID`, `PRIV` or the
  `COMM` frames of iTunes, are copied from the existing ID3v2.3 or ID3v2.4 tag. [RemoveTags](cref:Meziantou.Framework.MediaTags.MediaFile.RemoveTags) removes them too.
- A file with only an ID3v1 tag is read.

## OGG Vorbis, OGG Opus and FLAC

- Tags are Vorbis comments. A comment name cannot contain `=`: writing a custom field with such a name fails with
  [InvalidTagData](cref:Meziantou.Framework.MediaTags.MediaTagError.InvalidTagData).
- A comment whose value cannot be parsed, such as `TRACKNUMBER=A1`, is read into [CustomFields](cref:Meziantou.Framework.MediaTags.MediaTagInfo.CustomFields), so writing the tags
  back keeps it.
- Multiplexed and chained OGG streams, such as a video stream next to the audio or several concatenated streams, are
  refused rather than rewritten.

## MP4 and M4A

- Tags are iTunes-style metadata items. The items this library does not read are kept when the tags are written.
- Only one picture is stored: the front cover.

## WAV and AIFF

- Tags are written as an embedded ID3v2 chunk.
- Writing removes the existing `LIST`/`INFO` chunks of a WAV file and the `NAME`, `AUTH`, `ANNO`, `(c)` and `ISRC`
  chunks of an AIFF file: other programs would otherwise read them instead of the tag just written.

## ID3v2 tags in front of other formats

Some taggers add an ID3v2 tag at the start of a FLAC, OGG, MP4, WAV or AIFF file. It is skipped when reading, and
copied unchanged when writing.
