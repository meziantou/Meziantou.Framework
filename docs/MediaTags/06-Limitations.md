---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **Audio formats only.** There is no support for video files, images, or audio formats other than those of the
  [Introduction](01-Introduction.md#supported-formats), such as WMA or APE tags.
- **One set of fields.** `MediaTagInfo` exposes the common fields; the frames and items of a format that the library
  does not read are kept when writing, but you cannot read or change them, except the text fields of `CustomFields`.
- **Writing replaces.** There is no merge: read the tags before writing them; see
  [Writing replaces the tags](04-WritingTags.md#writing-replaces-the-tags).
- **No OGG multiplexing.** Multiplexed and chained OGG streams are refused when writing.
- **Seekable streams.** The stream methods need a stream that supports seeking.
- **Synchronous.** The methods are synchronous; run them on a background thread in a UI application.
- **.NET 10 and later only.**
