---
title: Torrent files
description: Read the trackers, files and piece hashes of a torrent, including BitTorrent v2 and hybrid torrents, compute its info-hashes, read text in legacy code pages, validate untrusted torrents, and create or rewrite a torrent.
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

[ParseAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.ParseAsync) parses the stream as it reads it, and keeps a copy of the bytes of the `info` dictionary only, to compute the
info-hash from the original bytes. It leaves the stream open. An overload reads from a `PipeReader`, which you
complete yourself.

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
| [Encoding](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Encoding) | `encoding` | The name of the code page of the text fields. See [Text encoding](#text-encoding). |
| [PieceLayers](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.PieceLayers) | `piece layers` | BitTorrent v2: the piece hashes of each file larger than a piece. |
| [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info) | `info` | The [TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) that describes the content. |
| [AdditionalKeys](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.AdditionalKeys) | any other | The keys the model does not represent, as bencode values. |

[TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) holds the keys of the `info` dictionary:

| Property | Key | Content |
| --- | --- | --- |
| [Name](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Name) | `name` | The name of the file, or of the folder for a multi-file torrent. |
| [PieceLength](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.PieceLength) | `piece length` | The size of a piece, in bytes. |
| [Pieces](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Pieces) | `pieces` | The SHA-1 hashes of the pieces, 20 bytes each, concatenated. Empty for a BitTorrent v2 only torrent. |
| [Length](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Length) | `length` | The size of the file, for a single-file torrent. |
| [Files](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Files) | `files` | The files of a multi-file torrent, each with a [Path](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfoFile.Path) (its segments), a `Length`, an optional `Md5Sum`, and `AdditionalKeys`. |
| [IsPrivate](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.IsPrivate) | `private` | Whether clients must only use the trackers of the torrent. |
| [Source](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Source) | `source` | The tag some private trackers add to make the info-hash unique to them. |
| `Md5Sum` | `md5sum` | The MD5 of the content of a single-file torrent. |
| [MetaVersion](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.MetaVersion) | `meta version` | `2` for a BitTorrent v2 or hybrid torrent. |
| [FileTree](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.FileTree) | `file tree` | BitTorrent v2: the files, each a [TorrentFileTreeEntry](cref:Meziantou.Framework.Bencode.Torrent.TorrentFileTreeEntry) with a `Path`, a `Length`, a `PiecesRoot` and `AdditionalKeys`. |
| [AdditionalKeys](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.AdditionalKeys) | any other | The keys the model does not represent, as bencode values. |

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

## BitTorrent v2 and hybrid torrents

A BitTorrent v2 torrent (BEP 52) describes its files with a [FileTree](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.FileTree) and hashes them with SHA-256. A hybrid torrent
has both the v1 keys and the v2 keys, so v1 and v2 clients can share it:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_V2;format=md-fenced-code-block;dedent=true -->
```csharp
// A hybrid torrent, created by libtorrent, holds both the BitTorrent v1 and v2 keys
// Such as File.OpenRead("file.torrent")
await using var stream = typeof(TorrentFileTests).Assembly.GetManifestResourceStream("files/hybrid.torrent")!;
var torrent = await TorrentFile.ParseAsync(stream);

Assert.Equal(2, torrent.Info.MetaVersion);
Assert.NotNull(torrent.Info.FileTree);
var files = torrent.Info.FileTree.Select(file => $"{string.Join('/', file.Path)} ({file.Length} bytes)");
Assert.Equal(["data.bin (40000 bytes)", "docs/readme.txt (14 bytes)", "empty.bin (0 bytes)"], files);

// The SHA-256 merkle root of each file, and the piece hashes of the files larger than a piece
Assert.Equal(32, torrent.Info.FileTree[0].PiecesRoot.Length);
Assert.NotNull(torrent.PieceLayers);
Assert.Equal(torrent.Info.FileTree[0].PiecesRoot.ToArray(), torrent.PieceLayers[0].PiecesRoot.ToArray());

// The v1 info-hash is the SHA-1 of the 'info' dictionary, and the v2 info-hash its SHA-256
Assert.Equal("9AAF1926E4A8E378CC2D2BD2F71008D0C819ABF7", Convert.ToHexString(torrent.GetInfoHashSha1()));
Assert.Equal("EB59F42E745CCF50245148E82AEE5F7817D572D5F274A27F323C5286EA7691C3", Convert.ToHexString(torrent.GetInfoHashSha256()));
```
<!-- endref -->

- [FileTree](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.FileTree) is a flat list of the files of the tree. Each [Path](cref:Meziantou.Framework.Bencode.Torrent.TorrentFileTreeEntry.Path) is relative to the folder named after `Info.Name`
  for a multi-file torrent; a single-file torrent has one file, named like the torrent.
- [PiecesRoot](cref:Meziantou.Framework.Bencode.Torrent.TorrentFileTreeEntry.PiecesRoot) is 32 bytes for a file that is not empty, and empty for an empty file.
- The padding files of a hybrid torrent are entries of [Files](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Files) whose `attr` key, in `AdditionalKeys`, contains `p`.
- A v2 only torrent has no [Pieces](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Pieces), [Length](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Length) or [Files](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.Files).

To create one, set [MetaVersion](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.MetaVersion) to `2` and fill [FileTree](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo.FileTree), and [PieceLayers](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.PieceLayers) for the files larger than a piece.
The writer builds the tree from the paths, and throws a `FormatException` when two paths are the same or a file is
also a folder. The library does not compute the merkle trees for you.

## The info-hash

[GetInfoHashSha1](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.GetInfoHashSha1) returns the BitTorrent v1 info-hash: the SHA-1 of the `info` dictionary. [GetInfoHashSha256](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.GetInfoHashSha256) returns
the SHA-256 of the same bytes, which is the BitTorrent v2 info-hash. Format them with `Convert.ToHexString`.

- For a parsed torrent, they hash the exact bytes the `info` dictionary was read from, so keys the model does not
  represent still count and the hash matches what other clients compute.
- For a torrent you create, or after you assign a new [TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) to [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info), they hash the canonical encoding of the
  model.
- Changing the properties of the [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info) of a parsed torrent does not change the hash, which still uses the parsed
  bytes. Assign a new [TorrentInfo](cref:Meziantou.Framework.Bencode.Torrent.TorrentInfo) to [Info](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Info) to change the content of a torrent.

## Text encoding

The text fields of a torrent are UTF-8, unless its [Encoding](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Encoding) names another encoding. Old torrents use code pages such as
`GBK`, `Shift_JIS` or `windows-1251`: their text is read and written with that encoding:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_Encoding;format=md-fenced-code-block;dedent=true -->
```csharp
// A torrent whose 'encoding' key says its text is in GBK, a Chinese code page
var gbk = CodePagesEncodingProvider.Instance.GetEncoding("GBK")!;
byte[] data = [.. "d7:comment4:"u8, .. gbk.GetBytes("中文"), .. "8:encoding3:GBK4:infod6:lengthi1e4:name8:"u8, .. gbk.GetBytes("文件.txt"), .. "12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8];

var torrent = TorrentFile.Parse(data);
Assert.Equal("GBK", torrent.Encoding);
Assert.Equal("文件.txt", torrent.Info.Name);
Assert.Equal("中文", torrent.Comment);

// Writing uses the same encoding
Assert.Equal(data, torrent.ToUtf8ByteArray());
```
<!-- endref -->

- The names are the ones of `Encoding.GetEncoding`, including the code pages of `CodePagesEncodingProvider`. The
  library uses the provider without registering it, so it does not change the encodings of your application.
- An unknown name is ignored: the text is read and written as UTF-8.
- Bytes that are not valid in the encoding are an error when parsing, and characters it cannot represent are an
  error when writing: both throw a `FormatException`.
- Setting [Encoding](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Encoding) on a torrent you create writes its text with that encoding. The info-hash of a torrent you create
  depends on it, since the name is part of the `info` dictionary.

## Untrusted torrents

[Parse](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.Parse) and [ParseAsync](cref:Meziantou.Framework.Bencode.Torrent.TorrentFile.ParseAsync) throw a `FormatException` when the data is not bencode, or is not a valid torrent: a missing
`info`, `name`, `piece length` or `pieces`, `pieces` whose length is not a multiple of 20, both `length` and `files` or
neither, a negative length, an unsafe path segment, a key of the wrong type, an integer out of the range of its
property, or text that is not valid in its encoding. For BitTorrent v2, they also reject a `file tree` without a
`meta version` of 2 or the reverse, a piece length that is not a power of two of at least 16 KiB, a file that is also a
folder, a `pieces root` that is not 32 bytes, and `piece layers` whose hashes are not a multiple of 32 bytes.
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

The keys the model does not represent are kept in `AdditionalKeys`: of the root dictionary, of the `info` dictionary,
and of each file. Writing the torrent writes them back:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/TorrentFileTests.cs;region=TorrentFile_AdditionalKeys;format=md-fenced-code-block;dedent=true -->
```csharp
// 'x-custom' and 'x-tag' are not part of the model
var data = "d4:infod6:lengthi123e4:name8:file.txt12:piece lengthi16384e6:pieces20:012345678901234567895:x-tag3:abce8:x-custom5:valuee"u8.ToArray();
var torrent = TorrentFile.Parse(data);

var custom = (BencodeString)torrent.AdditionalKeys[new BencodeString("x-custom"u8.ToArray())];
Assert.Equal("value", custom.ToUtf8String());
Assert.True(torrent.Info.AdditionalKeys.ContainsKey(new BencodeString("x-tag"u8.ToArray())));

// They are written back, so the 'info' dictionary and the info-hash do not change
var written = torrent.ToUtf8ByteArray();
Assert.Equal(torrent.GetInfoHashSha1(), TorrentFile.Parse(written).GetInfoHashSha1());

// Add or remove keys like in any dictionary
torrent.AdditionalKeys.Add(new BencodeString("x-other"u8.ToArray()), new BencodeInteger(1));
```
<!-- endref -->

- A key of `AdditionalKeys` that the model represents, such as `private`, throws a `FormatException` when writing: set
  the property instead.
- The writer encodes the model. Writing a parsed torrent canonically gives the `info` dictionary it was read from, so
  the same info-hash, when that dictionary was canonical and its values are written the way they were read. Two cases
  change it: an explicit `private 0`, which is not written, and text that a code page decodes and encodes differently.
- Writing with `canonical: false` writes the keys in the order of the model, not in the order of the data.
