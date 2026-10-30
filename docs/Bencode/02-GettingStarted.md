---
title: Getting started
description: Install the package, read a torrent file and its info-hash, and decode bencode data.
---

## Install the package

Add the `Meziantou.Framework.Bencode` package to a project:

```bash
dotnet add package Meziantou.Framework.Bencode
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Bencode;`, and
`using Meziantou.Framework.Bencode.Torrent;` for torrent files.

## Read a torrent file

`TorrentFile.ParseAsync` reads a `.torrent` file from a stream. Its properties give the tracker, the name of the
content and its info-hash, and `WriteToAsync` writes it back:

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

`GetInfoHashSha1` returns the BitTorrent v1 info-hash, the identifier that trackers, peers and magnet links use. See
[Torrent files](05-TorrentFiles.md).

## Decode bencode data

For any other bencode data, `BencodeDocument.Parse` returns the root value. Cast it, or match it, to the type of value
you expect:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/BencodeDocumentTests.cs;region=BencodeDocument_ReadValues;format=md-fenced-code-block;dedent=true -->
```csharp
var document = BencodeDocument.Parse("d4:name5:Alice3:agei30e4:tagsl3:dev3:opsee"u8);
var root = (BencodeDictionary)document.Root;

var name = (BencodeString)root[new BencodeString("name"u8.ToArray())];
Assert.Equal("Alice", name.ToUtf8String());

Assert.True(root.TryGetValue(new BencodeString("age"u8.ToArray()), out var age));
Assert.Equal(30, ((BencodeInteger)age).Value);

var tags = (BencodeList)root[new BencodeString("tags"u8.ToArray())];
Assert.Equal(["dev", "ops"], tags.Cast<BencodeString>().Select(tag => tag.ToUtf8String()));

// The entries keep the order of the data
Assert.Equal(["string Alice", "integer 30", "list of 2"], root.Values.Select(Describe));

static string Describe(BencodeValue value) => value switch
{
    BencodeInteger integer => $"integer {integer.Value}",
    BencodeString text => $"string {text}",
    BencodeList list => $"list of {list.Count}",
    BencodeDictionary dictionary => $"dictionary of {dictionary.Count}",
    _ => throw new ArgumentOutOfRangeException(nameof(value)),
};
```
<!-- endref -->

Dictionary keys are `BencodeString` values, created from the bytes of the key. See
[Reading bencode](03-ReadingBencode.md).

## Next steps

- [Reading bencode](03-ReadingBencode.md): parse from memory, a stream or a pipe, and read strings safely.
- [Writing bencode](04-WritingBencode.md): build values and encode them, or write them directly.
- [Torrent files](05-TorrentFiles.md): the torrent model, multi-file torrents and creating a torrent.
