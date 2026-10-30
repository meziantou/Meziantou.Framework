---
title: Introduction
description: Parse and write bencode, the encoding of BitTorrent files, and work with torrent metainfo through a typed model.
---

`Meziantou.Framework.Bencode` reads and writes bencode, the binary encoding that BitTorrent uses for `.torrent` files
and for the messages of its protocols. It has two levels:

- **A document model** for any bencode data: integers, byte strings, lists and dictionaries that you read, build and
  encode. See [Reading bencode](03-ReadingBencode.md) and [Writing bencode](04-WritingBencode.md).
- **A torrent model** for `.torrent` files: the trackers, the files and the piece hashes as typed properties, and the
  info-hash that identifies the torrent. See [Torrent files](05-TorrentFiles.md).

## Bencode in short

Bencode has four kinds of values:

| Kind | Encoding | Example | Type |
| --- | --- | --- | --- |
| Integer | `i<digits>e` | `i42e` | [BencodeInteger](cref:Meziantou.Framework.Bencode.BencodeInteger) |
| String | `<length>:<bytes>` | `4:spam` | [BencodeString](cref:Meziantou.Framework.Bencode.BencodeString) |
| List | `l<values>e` | `l4:spami42ee` | [BencodeList](cref:Meziantou.Framework.Bencode.BencodeList) |
| Dictionary | `d<key><value>…e` | `d3:cow3:mooe` | [BencodeDictionary](cref:Meziantou.Framework.Bencode.BencodeDictionary) |

A string is a sequence of bytes, not necessarily text: the piece hashes of a torrent are binary. Dictionary keys are
strings, and the canonical encoding sorts them by their bytes, so a given dictionary always encodes to the same bytes.

## Namespaces

The document model is in the [Meziantou.Framework.Bencode](cref:Meziantou.Framework.Bencode) namespace. The torrent model ([TorrentFile](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile),
[TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo), [TorrentInfoFile](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfoFile), [TorrentNode](cref:Meziantou.Framework.Bencode.Torrent.TorrentNode)) is in [Meziantou.Framework.Bencode.Torrent](cref:Meziantou.Framework.Bencode.Torrent).

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read a torrent file and decode bencode data.
- [Limitations](06-Limitations.md): what the library does not do.
