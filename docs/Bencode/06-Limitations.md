---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **BitTorrent v1 model only.** The torrent model has no property for the BitTorrent v2 keys (`file tree`,
  `meta version`, `piece layers`), and writing a parsed torrent drops them; see
  [Writing a parsed torrent](05-TorrentFiles.md#writing-a-parsed-torrent). Read them with the document model.
- **No BitTorrent client features.** The library does not hash the content of a torrent for you, talk to trackers or
  peers, or read magnet links.
- **Text must be UTF-8.** The text fields of a torrent must be valid UTF-8, even when `Encoding` names another code
  page. Read such torrents with the document model and decode the bytes yourself.
- **In memory.** A `TorrentFile` stream is read whole before it is parsed, and `WriteToAsync` encodes the whole value in
  memory before writing it. `BencodeWriter` writes to an `IBufferWriter<byte>` as you go.
- **Nesting depth of 64.** Deeper lists and dictionaries are refused with a `FormatException`.
- **Integers fit in a `long`.** Larger bencode integers are refused.
