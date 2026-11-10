---
title: Format support
description: Which fields each audio and video format stores, its native fields, and what each format does when tags are written.
---

## Fields by format

| Field | MP3 | OGG / Opus | FLAC | MP4 | WAV / AIFF | APE / WavPack / Musepack | ASF |
| --- | --- | --- | --- | --- | --- | --- | --- |
| [Title](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Title), [Artist](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Artist), [Album](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Album), [AlbumArtist](cref:Meziantou.Framework.MediaTags.MediaTagInfo.AlbumArtist), [Genre](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Genre), [Year](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Year) | yes | yes | yes | yes | yes | yes | yes |
| [TrackNumber](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TrackNumber), [TrackTotal](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TrackTotal), [DiscNumber](cref:Meziantou.Framework.MediaTags.MediaTagInfo.DiscNumber), [DiscTotal](cref:Meziantou.Framework.MediaTags.MediaTagInfo.DiscTotal) | yes | yes | yes | yes <sup>1</sup> | yes | yes | yes |
| [Comment](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Comment), [Lyrics](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Lyrics), [Composer](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Composer), [Conductor](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Conductor), [Copyright](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Copyright), [Isrc](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Isrc) | yes | yes | yes | yes | yes | yes | yes |
| [Bpm](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Bpm), [IsCompilation](cref:Meziantou.Framework.MediaTags.MediaTagInfo.IsCompilation) | yes | yes | yes | yes <sup>1</sup> | yes | yes | yes |
| [Duration](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Duration) | read and written <sup>2</sup> | read only | read only | read only | read and written <sup>2</sup> | read only | read only |
| [Pictures](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Pictures) | yes | yes | yes | front cover only | yes | one per type | yes |
| [ReplayGain](cref:Meziantou.Framework.MediaTags.MediaTagInfo.ReplayGain) | yes | yes | yes | yes | yes | yes | yes |
| `MusicBrainz*` | yes | yes | yes | yes | yes | yes | yes |
| [MediaKind](cref:Meziantou.Framework.MediaTags.MediaTagInfo.MediaKind), [Description](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Description), [LongDescription](cref:Meziantou.Framework.MediaTags.MediaTagInfo.LongDescription), `Tv*` | no | no | no | yes | no | no | no |
| [VideoWidth](cref:Meziantou.Framework.MediaTags.MediaTagInfo.VideoWidth), [VideoHeight](cref:Meziantou.Framework.MediaTags.MediaTagInfo.VideoHeight) | no | no | no | read only | no | no | read only |
| [CustomFields](cref:Meziantou.Framework.MediaTags.MediaTagInfo.CustomFields) | yes | yes | yes | yes | yes <sup>3</sup> | yes | yes |
| [NativeFields](cref:Meziantou.Framework.MediaTags.MediaTagInfo.NativeFields) | ID3v2 frames | none | none | items | ID3v2 frames | binary items | other attributes |

<sup>1</sup> MP4 stores these values in 16 bits. A value above 65535 is refused with [MediaTagError.InvalidTagData](cref:Meziantou.Framework.MediaTags.MediaTagError.InvalidTagData)
rather than wrapped.

<sup>2</sup> Stored in a `TLEN` frame. A duration computed from the audio is not written; see
[Change some fields](04-WritingTags.md#change-some-fields). A `TLEN` of 0 means the duration is unknown, so the duration
is computed from the audio instead.

<sup>3</sup> Stored in the embedded ID3v2 tag, not as native chunks.

## MP3

- Tags are written as an ID3v2 tag, and an ID3v1 tag at the end of the file unless
  [`WriteId3v1Tag`](04-WritingTags.md#write-options) is `false`. ID3v1 truncates each field to 30 Latin-1 characters.
- The ID3v2 frames this library does not read, such as the publisher, the sort names, ratings, `UFID`, `PRIV` or the
  `COMM` frames of iTunes, are [native fields](03-ReadingTags.md#native-fields). The frames of an ID3v2.3 tag are
  converted to ID3v2.4, and an ID3v2.2 tag has none. [RemoveTags](cref:Meziantou.Framework.MediaTags.MediaFile.RemoveTags) removes them too.
- A native frame has a four-character identifier of upper-case letters and digits. A frame a property represents (`TIT2`,
  `TXXX`, `APIC`, `COMM` and `USLT` without a description…) and the tag alter preservation flag (0x4000) are refused.
- An APEv2 tag at the end of the file, as foobar2000 writes, is read: its values fill the fields the ID3v2 tag does not
  set, before the ID3v1 tag does. Writing replaces it with the ID3v2 tag: its values are in [MediaTagInfo](cref:Meziantou.Framework.MediaTags.MediaTagInfo), but its
  binary items are not kept.
- A file with only an ID3v1 tag is read. "TAG" in the last 128 bytes of the file is not an ID3v1 tag when the ID3v2 tag covers it, as
  when a text frame holds it and less than 128 bytes of audio follow the ID3v2 tag.

## OGG Vorbis, OGG Opus and FLAC

- Tags are Vorbis comments. A comment name cannot contain `=`: writing a custom field with such a name fails with
  [InvalidTagData](cref:Meziantou.Framework.MediaTags.MediaTagError.InvalidTagData).
- A comment whose value cannot be parsed, such as `TRACKNUMBER=A1`, is read into [CustomFields](cref:Meziantou.Framework.MediaTags.MediaTagInfo.CustomFields), so writing the tags
  back keeps it.
- Every comment is a property or a custom field, so these formats have no native fields.
- Multiplexed and chained OGG streams, such as a video stream next to the audio or several concatenated streams, are
  refused rather than rewritten.

## MP4, M4A, M4V and MOV

- Tags are iTunes-style metadata items, in audio and video files alike. The items this library does not read, such as
  the encoder or the gapless playback flag, are [native fields](03-ReadingTags.md#native-fields): they are kept when the
  tags are written, and [RemoveTags](cref:Meziantou.Framework.MediaTags.MediaFile.RemoveTags) removes them.
- A native item has a type of four Latin-1 characters and no flags. The type of an item a property represents (`©nam`,
  `trkn`, `covr`, `----`, `tvsh`…) or of a box of the movie structure (`moov`, `meta`, `stco`…) is refused.
- Only one picture is stored: the front cover.
- The video items are [MediaKind](cref:Meziantou.Framework.MediaTags.MediaTagInfo.MediaKind) (`stik`), [Description](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Description) (`desc`), [LongDescription](cref:Meziantou.Framework.MediaTags.MediaTagInfo.LongDescription) (`ldes`), [TvShow](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TvShow) (`tvsh`),
  [TvNetwork](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TvNetwork) (`tvnn`), [TvEpisodeId](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TvEpisodeId) (`tven`), [TvSeason](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TvSeason) (`tvsn`) and [TvEpisode](cref:Meziantou.Framework.MediaTags.MediaTagInfo.TvEpisode) (`tves`). The video size is
  read from the header of the first video track.
- QuickTime files store the title, the artist, the album, the year, the comment, the genre, the composer, the copyright
  and the description as text records of the `udta` atom (`©nam`, `©ART`…). They are read when the metadata items do
  not set the field, and writing moves them to the metadata items.

## WAV and AIFF

- Tags are written as an embedded ID3v2 chunk, whose native fields are the ones of [MP3](#mp3).
- Writing removes the existing `LIST`/`INFO` chunks of a WAV file and the `NAME`, `AUTH`, `ANNO`, `(c)` and `ISRC`
  chunks of an AIFF file: other programs would otherwise read them instead of the tag just written.

## Monkey's Audio, WavPack and Musepack

- Tags are an APEv2 tag at the end of the file. An APEv1 tag is read too, and written as APEv2. An ID3v1 tag that older
  taggers added is read for the fields the APE tag does not set; writing removes it, and the APE tags that taggers
  stacked, so their values are only in the new APE tag.
- Item keys are case-insensitive: `Title`, `Artist`, `Album`, `Album Artist`, `Year`, `Track` and `Disc` (as
  "number/total"), `Genre`, `Comment`, `Lyrics`, `Composer`, `Conductor`, `Copyright`, `ISRC`, `BPM`, `Compilation`,
  and the ReplayGain and MusicBrainz names of Vorbis comments. Text is UTF-8.
- A picture is a binary item named after its type, such as `Cover Art (Front)`, so there is one per type: writing two
  of the same type fails with [InvalidTagData](cref:Meziantou.Framework.MediaTags.MediaTagError.InvalidTagData). Its file name is the [Description](cref:Meziantou.Framework.MediaTags.MediaPicture.Description). The format stores no MIME
  type: it is read from the first bytes of the image (PNG, GIF, BMP, WebP), and is `image/jpeg` for another image.
- A custom field name, and the key of a native item, has 2 to 255 printable ASCII characters, and cannot be `ID3`,
  `TAG`, `OggS` or `MP+`; another one is refused. A custom field named like a field that is set is not written.
- The native fields are the binary items and the external links (flags 2 and 4, optionally with the read-only flag 1).
  A binary item named after a picture type is a picture.
- The duration is read from the stream header. Musepack files of stream versions 7 and 8 are supported.

## ASF (WMA and WMV)

- The title, the artist, the copyright and the comment are stored in the content description object, as Windows Media
  Player expects. A value too long for it is stored as an attribute of the same name (`Title`, `Author`…).
- The other fields are attributes: `WM/AlbumTitle`, `WM/AlbumArtist`, `WM/Genre`, `WM/Year`, `WM/TrackNumber` (a 32-bit
  integer) and `TotalTracks`, `WM/PartOfSet` (the disc, as "number/total"), `WM/Composer`, `WM/Conductor`, `WM/Lyrics`,
  `WM/ISRC`, `WM/BeatsPerMinute`, `WM/IsCompilation`, `WM/Picture`, the ReplayGain names and `MusicBrainz/Track Id`… The
  0-based `WM/Track` of older files, and the `date` attribute ffmpeg writes, are read too.
- Every text attribute that no property represents is a custom field, as is the rating of the content description,
  which is written back as a `Rating` attribute. A custom field named like a field that is set is not written.
- Every other attribute (byte arrays, numbers, booleans, GUIDs) is a [native field](03-ReadingTags.md#native-fields).
  The data type of a native field must not be text, and its data must have the size of its type. Native fields and
  pictures are written in the metadata library object, which every recent reader reads; the other attributes in the
  extended content description object, or in the metadata library object when they are larger than 64 KB.
- Attributes that describe a single stream or a single language are kept as they are, and are not read.
- The size of the file stored in the file properties object is updated. The data packets and the indexes are copied
  unchanged.

## ID3v2 tags in front of other formats

Some taggers add an ID3v2 tag at the start of a FLAC, OGG, MP4, WAV, AIFF, Monkey's Audio, WavPack or Musepack file. It
is skipped when reading, and copied unchanged when writing.
