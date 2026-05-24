# Meziantou.Framework.Bencode

`Meziantou.Framework.Bencode` provides:

- A low-level bencode DOM (`BencodeDocument`, `BencodeValue`, `BencodeDictionary`, `BencodeList`, ...)
- A high-level torrent metainfo model (`TorrentFile`, `TorrentInfo`, `TorrentInfoFile`)

## Parse and write bencode data

```csharp
using Meziantou.Framework.Bencode;

var data = "d3:cow3:moo4:spam4:eggse"u8.ToArray();
var document = BencodeDocument.Parse(data);

var dictionary = (BencodeDictionary)document.Root;
var cow = (BencodeString)dictionary["cow"];
Console.WriteLine(cow.ToUtf8String()); // moo

await using var stream = File.Create("output.bencode");
await document.WriteToAsync(stream);
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
