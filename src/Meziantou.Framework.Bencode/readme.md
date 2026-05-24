# Meziantou.Framework.Bencode

`Meziantou.Framework.Bencode` provides:

- A low-level bencode DOM (`BencodeDocument`, `BencodeValue`, `BencodeDictionary`, `BencodeList`, ...)
- A high-level torrent metainfo model (`TorrentFile`, `TorrentInfo`, `TorrentInfoFile`)

## Parse and write bencode data

```csharp
using System.IO.Pipelines;
using Meziantou.Framework.Bencode;

var data = "d3:cow3:moo4:spam4:eggse"u8.ToArray();
var document = BencodeDocument.Parse(data);

await using var input = File.OpenRead("input.bencode");
var pipeReader = PipeReader.Create(input);
var streamedDocument = await BencodeDocument.ParseAsync(pipeReader);
await pipeReader.CompleteAsync();

var dictionary = (BencodeDictionary)document.Root;
var cow = (BencodeString)dictionary[new BencodeString("cow"u8.ToArray())];
Console.WriteLine(cow.ToUtf8String()); // moo

var encoded = dictionary.ToArray();

await using var stream = File.Create("output.bencode");
await dictionary.WriteToAsync(stream);
```

You can also write bencode directly:

```csharp
using System.Buffers;
using Meziantou.Framework.Bencode;

var buffer = new ArrayBufferWriter<byte>();
var writer = new BencodeWriter(buffer);

writer.WriteStartDictionary();
writer.WriteUtf8Key("cow");
writer.WriteUtf8String("moo");
writer.WriteEndDictionary();
writer.Complete();

var data = buffer.WrittenSpan.ToArray(); // d3:cow3:mooe
```

## Parse and write torrent files

```csharp
using Meziantou.Framework.Bencode.Torrent;

await using var input = File.OpenRead("file.torrent");
var torrent = await TorrentFile.ParseAsync(input);

Console.WriteLine(torrent.Announce);
Console.WriteLine(torrent.Info.Name);
Console.WriteLine(Convert.ToHexString(torrent.GetInfoHashSha1()));

await using var output = File.Create("copy.torrent");
await torrent.WriteToAsync(output, canonical: true);
```
