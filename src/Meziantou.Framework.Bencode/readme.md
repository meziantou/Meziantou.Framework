# Meziantou.Framework.Bencode

`Meziantou.Framework.Bencode` provides:

- A low-level bencode DOM (`BencodeDocument`, `BencodeValue`, `BencodeDictionary`, `BencodeList`, ...)
- A high-level torrent metainfo model (`TorrentFile`, `TorrentInfo`, `TorrentInfoFile`)

## Parse and write bencode data

The types are in the `Meziantou.Framework.Bencode` namespace, and `PipeReader` in `System.IO.Pipelines`.

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/BencodeDocumentTests.cs;region=BencodeDocument_ParseAndWrite;format=md-fenced-code-block;dedent=true -->
```csharp
var data = "d3:cow3:moo4:spam4:eggse"u8.ToArray();
var document = BencodeDocument.Parse(data);

// Parse from a stream, such as File.OpenRead("input.bencode")
await using var input = new MemoryStream(data);
var pipeReader = PipeReader.Create(input);
var streamedDocument = await BencodeDocument.ParseAsync(pipeReader);
await pipeReader.CompleteAsync();
Assert.IsType<BencodeDictionary>(streamedDocument.Root);

var dictionary = (BencodeDictionary)document.Root;
var cow = (BencodeString)dictionary[new BencodeString("cow"u8.ToArray())];
Assert.Equal("moo", cow.ToUtf8String());

var encoded = dictionary.ToUtf8ByteArray();
Assert.Equal("d3:cow3:moo4:spam4:eggse"u8.ToArray(), encoded);

// Write to a stream, such as File.Create("output.bencode")
await using var stream = new MemoryStream();
await dictionary.WriteToAsync(stream);
```
<!-- endref -->

You can also write bencode directly (`ArrayBufferWriter<T>` is in the `System.Buffers` namespace):

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/BencodeDocumentTests.cs;region=BencodeWriter_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new ArrayBufferWriter<byte>();
var writer = new BencodeWriter(buffer);

writer.WriteStartDictionary();
writer.WriteUtf8Key("cow");
writer.WriteUtf8String("moo");
writer.WriteEndDictionary();
writer.Complete();

var data = buffer.WrittenSpan.ToArray();
Assert.Equal("d3:cow3:mooe"u8.ToArray(), data);
```
<!-- endref -->

## Parse and write torrent files

The torrent types are in the `Meziantou.Framework.Bencode.Torrent` namespace.

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
