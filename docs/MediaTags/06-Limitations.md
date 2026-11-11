---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **Supported formats only.** Formats other than those of the [Introduction](01-Introduction.md#supported-formats), such
  as Matroska (MKV, WebM), AVI, DSF or TTA files, are not supported.
- **Stream properties.** Besides the [Duration](cref:Meziantou.Framework.MediaTags.MediaTagInfo.Duration), only the size of a video is read from the streams: there is no codec,
  bit rate, sample rate or channel count.
- **Some fields are kept but not read.** The metadata of iPhone videos (QuickTime `mdta` keys such as
  `com.apple.quicktime.location.ISO6709`) and the ASF attributes of a single stream or language are kept when writing,
  but are neither [native fields](03-ReadingTags.md#native-fields) nor properties.
- **ID3v2.2 frames are dropped.** The frames of an ID3v2.2 tag that no property represents have no general ID3v2.4
  identifier: they are not native fields, and writing removes them.
- **Some ID3v2 frames cannot be decoded.** A frame that is encrypted, or compressed with data that zlib cannot
  inflate (corrupt, or that needs a preset dictionary), sets no property, and the other frames are still read. In an
  ID3v2.4 tag, such a frame is a [native field](03-ReadingTags.md#native-fields) with its data as stored and its
  flags, unless a property represents its identifier; writing removes the ones of an ID3v2.3 tag.
- **APE tags of MP3 files are converted.** The APEv2 tag of an MP3 file is read, but writing replaces it with the ID3v2
  tag, without its binary items.
- **Writing replaces.** There is no merge: read the tags before writing them; see
  [Writing replaces the tags](04-WritingTags.md#writing-replaces-the-tags).
- **No OGG multiplexing.** Multiplexed and chained OGG streams are refused when writing.
- **Seekable streams.** The stream methods need a stream that supports seeking.
- **Synchronous.** The methods are synchronous; run them on a background thread in a UI application.
