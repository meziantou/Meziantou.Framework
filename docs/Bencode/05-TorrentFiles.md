---
title: Torrent files
description: Read the trackers, files and piece hashes of a torrent, compute its info-hash, validate untrusted torrents, and create a torrent.
---

[TorrentFile](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile) is the content of a `.torrent` file (its metainfo). Read one with [ParseAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.ParseAsync) from a stream or [Parse](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Parse)
from bytes, and write it with [WriteToAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.WriteToAsync) or [ToUtf8ByteArray](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.ToUtf8ByteArray):

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_ParseAndWrite;format=md-fenced-code-block;dedent=true -->
```csharp
var content = "d8:announce14:https://t.test4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8.ToArray();

// Parse from a stream, such as File.OpenRead("file.torrent")
await using var input = new MemoryStream(content);
var torrent = await TorrentFile.ParseAsync(input);

Assert.Equal("https://t.test", torrent.Announce);
Assert.Equal("file.txt", torrent.Info.Name);
Assert.Equal("5A47092756A964C26715F69510A7B35B63B3B01C", Convert.ToHexString(torrent.GetInfoHashSha1()));

// Write to a stream, such as File.Create("copy.torrent")
await using var output = new MemoryStream();
await torrent.WriteToAsync(output, canonical: true);
```
<!-- endref -->

[ParseAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.ParseAsync) reads the whole stream in memory before parsing it, so the info-hash can be computed from the original
bytes.

## The model

[TorrentFile](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile) holds the keys of the root dictionary:

| Property | Key | Content |
| --- | --- | --- |
| [Announce](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Announce) | `announce` | The URL of the tracker. |
| [AnnounceList](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.AnnounceList) | `announce-list` | Tiers of tracker URLs. |
| [Comment](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Comment), [CreatedBy](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.CreatedBy), [Publisher](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Publisher), [PublisherUrl](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.PublisherUrl) | `comment`, `created by`, `publisher`, `publisher-url` | Free text. |
| [CreationDate](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.CreationDate) | `creation date` | A `DateTimeOffset`, stored as Unix seconds. |
| [UrlList](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.UrlList) | `url-list` | Web seed URLs. A single string in the file is read as a list of one URL. |
| [HttpSeeds](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.HttpSeeds) | `httpseeds` | HTTP seed URLs. |
| [Nodes](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Nodes) | `nodes` | DHT nodes, each with a [Host](cref:Meziantou.Framework.Bencode.Torrent.TorrentNode.Host) and a [Port](cref:Meziantou.Framework.Bencode.Torrent.TorrentNode.Port). |
| [Encoding](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Encoding) | `encoding` | The name of the code page the file says its text uses. |
| [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info) | `info` | The [TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) that describes the content. |

[TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) holds the keys of the `info` dictionary:

| Property | Key | Content |
| --- | --- | --- |
| [Name](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Name) | `name` | The name of the file, or of the folder for a multi-file torrent. |
| [PieceLength](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.PieceLength) | `piece length` | The size of a piece, in bytes. |
| [Pieces](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Pieces) | `pieces` | The SHA-1 hashes of the pieces, 20 bytes each, concatenated. |
| [Length](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Length) | `length` | The size of the file, for a single-file torrent. |
| [Files](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Files) | `files` | The files of a multi-file torrent, each with a [Path](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfoFile.Path) (its segments), a [Length](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Length) and an optional `Md5Sum`. |
| [IsPrivate](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.IsPrivate) | `private` | Whether clients must only use the trackers of the torrent. |
| [Source](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Source) | `source` | The tag some private trackers add to make the info-hash unique to them. |
| `Md5Sum` | `md5sum` | The MD5 of the content of a single-file torrent. |

## Single-file and multi-file torrents

A torrent has either a [Length](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Length), for one file, or [Files](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Files), for a folder named after `Info.Name`:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_MultiFile;format=md-fenced-code-block;dedent=true -->
```csharp
var data = "d4:infod5:filesld6:lengthi100e4:pathl6:images5:a.pngeed6:lengthi50e4:pathl9:readme.mdeee4:name7:project12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8;
var torrent = TorrentFile.Parse(data);

// A torrent has either a Length (one file) or Files (a folder named Info.Name)
Assert.Null(torrent.Info.Length);
Assert.NotNull(torrent.Info.Files);

var files = torrent.Info.Files.Select(file => $"{torrent.Info.Name}/{string.Join('/', file.Path)} ({file.Length} bytes)");
Assert.Equal(["project/images/a.png (100 bytes)", "project/readme.md (50 bytes)"], files);
```
<!-- endref -->

The parser rejects a name or a path segment that is empty, `.` or `..`, or that contains `/` or `\`, so the segments
can be combined into a path inside the download folder without leaving it.

## The info-hash

[GetInfoHashSha1](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.GetInfoHashSha1) returns the BitTorrent v1 info-hash: the SHA-1 of the `info` dictionary. [GetInfoHashSha256](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.GetInfoHashSha256) returns
the SHA-256 of the same bytes. Format them with `Convert.ToHexString`.

- For a parsed torrent, they hash the exact bytes the `info` dictionary was read from, so keys the model does not
  represent still count and the hash matches what other clients compute.
- For a torrent you create, or after you assign a new [TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) to [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info), they hash the canonical encoding of the
  model.
- Changing the properties of the [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info) of a parsed torrent does not change the hash, which still uses the parsed
  bytes. Assign a new [TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) to [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info) to change the content of a torrent.

## Untrusted torrents

[Parse](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Parse) and [ParseAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.ParseAsync) throw a `FormatException` when the data is not bencode, or is not a valid torrent: a missing
`info`, `name`, `piece length` or `pieces`, `pieces` whose length is not a multiple of 20, both `length` and `files` or
neither, a negative length, an unsafe path segment, a key of the wrong type, or text that is not valid UTF-8.
[TryParse](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.TryParse) returns `false` instead:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
// Not bencode
Assert.False(TorrentFile.TryParse("not a torrent"u8, out _));

// The 'info' dictionary has no 'piece length' and no 'pieces'
Assert.False(TorrentFile.TryParse("d4:infod6:lengthi1e4:name1:xee"u8, out _));

// A path segment that would leave the download folder
Assert.False(TorrentFile.TryParse("d4:infod5:filesld6:lengthi1e4:pathl2:..6:passwdeee4:name4:test12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8, out _));
```
<!-- endref -->

## Create a torrent

Hash each piece of the content with SHA-1, then fill the model:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var content = new byte[40_000]; // The file to share, such as File.ReadAllBytes("video.mp4")
var pieceLength = 16 * 1024;

// Each piece of the content is identified by its SHA-1 hash
var pieces = new List<byte>();
for (var offset = 0; offset < content.Length; offset += pieceLength)
{
    var piece = content.AsSpan(offset, Math.Min(pieceLength, content.Length - offset));
    pieces.AddRange(SHA1.HashData(piece));
}

var torrent = new TorrentFile
{
    Announce = "https://tracker.example.com/announce",
    CreatedBy = "MyApp",
    CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    Info = new TorrentInfo
    {
        Name = "video.mp4",
        PieceLength = pieceLength,
        Pieces = pieces.ToArray(),
        Length = content.Length,
    },
};

// Or WriteToAsync to save it, such as to File.Create("video.torrent")
var data = torrent.ToUtf8ByteArray();

var parsed = TorrentFile.Parse(data);
Assert.Equal("video.mp4", parsed.Info.Name);
Assert.Equal(torrent.GetInfoHashSha1(), parsed.GetInfoHashSha1());
```
<!-- endref -->

The last piece can be shorter than [PieceLength](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.PieceLength). Writing checks the model and throws a `FormatException` when it is
not a valid torrent, with the same rules as parsing.

## Writing a parsed torrent

The model only holds the keys of the tables above. Writing a parsed torrent drops every other key, in the root
dictionary and in the `info` dictionary, such as the keys of BitTorrent v2. When the `info` dictionary has such keys,
the written torrent has another info-hash: it is another torrent. To keep every key, edit the bencode values instead;
see [Writing bencode](04-WritingBencode.md).
