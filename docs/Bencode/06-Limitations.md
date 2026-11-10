---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **No BitTorrent client features.** The library does not hash the content of a torrent for you (neither the SHA-1
  pieces nor the SHA-256 merkle trees of BitTorrent v2), talk to trackers or peers, or read magnet links. It does not
  check that the `piece layers` of a v2 torrent match its `file tree`.
- **Writing is in memory.** [WriteToAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.WriteToAsync) encodes the whole value in memory before writing it. [BencodeWriter](cref:Meziantou.Framework.Bencode.BencodeWriter) writes to
  an `IBufferWriter<byte>` as you go. When reading, a string is read whole: a [BencodeReader](cref:Meziantou.Framework.Bencode.BencodeReader) token holds all its bytes.
- **The `name.utf-8` and `path.utf-8` keys are not used.** Some clients add them next to text in a code page. They are
  in `AdditionalKeys`.
- **Nesting depth of 64.** Deeper lists and dictionaries are refused with a `FormatException`.
- **Integers have at most 4096 digits.** Longer bencode integers are refused with a `FormatException`.
