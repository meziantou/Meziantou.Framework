---
title: Writing bencode
description: Build bencode values and encode them canonically or in insertion order, or write bencode directly with BencodeWriter.
---

## Build and encode values

Create the values with their constructors, then encode the root with [ToUtf8ByteArray](cref:Meziantou.Framework.Bencode.BencodeValueExtensions.ToUtf8ByteArray), or write it to a stream with
[WriteToAsync](cref:Meziantou.Framework.Bencode.BencodeDocument.WriteToAsync):

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/BencodeDocumentTests.cs;region=BencodeValue_Build;format=md-fenced-code-block;dedent=true -->
```csharp
var dictionary = new BencodeDictionary
{
    { new BencodeString("zebra"u8.ToArray()), new BencodeInteger(1) },
    { new BencodeString("apple"u8.ToArray()), new BencodeList([new BencodeString("x"u8.ToArray()), new BencodeInteger(-2)]) },
};

// By default, the keys are sorted by their bytes (canonical bencode)
Assert.Equal("d5:applel1:xi-2ee5:zebrai1ee"u8.ToArray(), dictionary.ToUtf8ByteArray());

// canonical: false keeps the insertion order
Assert.Equal("d5:zebrai1e5:applel1:xi-2eee"u8.ToArray(), dictionary.ToUtf8ByteArray(canonical: false));
```
<!-- endref -->

- [BencodeDictionary.Add](cref:Meziantou.Framework.Bencode.BencodeDictionary.Add) throws an `ArgumentException` when the key is already present.
- [BencodeList](cref:Meziantou.Framework.Bencode.BencodeList) and [BencodeDictionary](cref:Meziantou.Framework.Bencode.BencodeDictionary) also have constructors that take the values or the entries.

## Canonical encoding

By default, the encoders sort the keys of every dictionary by their bytes. This is the canonical bencode: the same
dictionary always encodes to the same bytes, which matters when the bytes are hashed, as the info-hash of a torrent
is. Pass `canonical: false` to [ToUtf8ByteArray](cref:Meziantou.Framework.Bencode.BencodeValueExtensions.ToUtf8ByteArray) or [WriteToAsync](cref:Meziantou.Framework.Bencode.BencodeDocument.WriteToAsync) to keep the insertion order instead.

[BencodeDocument.ToArray](cref:Meziantou.Framework.Bencode.BencodeDocument.ToArray) and [BencodeDocument.WriteToAsync](cref:Meziantou.Framework.Bencode.BencodeDocument.WriteToAsync) always encode canonically. A document parsed from data
whose keys were not sorted is therefore not written back byte for byte.

## Write bencode directly

[BencodeWriter](cref:Meziantou.Framework.Bencode.BencodeWriter) writes bencode to an `IBufferWriter<byte>` without building values first, which suits large or
streamed output:

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

- Inside a dictionary, write a key ([WriteUtf8Key](cref:Meziantou.Framework.Bencode.BencodeWriter.WriteUtf8Key) or [WriteKey](cref:Meziantou.Framework.Bencode.BencodeWriter.WriteKey)) before each value.
- [WriteString](cref:Meziantou.Framework.Bencode.BencodeWriter.WriteString) writes bytes and [WriteUtf8String](cref:Meziantou.Framework.Bencode.BencodeWriter.WriteUtf8String) writes text as UTF-8; [WriteInteger](cref:Meziantou.Framework.Bencode.BencodeWriter.WriteInteger) writes a `long`.
- The writer checks the structure: it throws an `InvalidOperationException` for a value without its key, a container
  closed in the wrong order, or a second root value. [Complete](cref:Meziantou.Framework.Bencode.BencodeWriter.Complete) throws when a container is still open.
- The writer writes the keys in the order you write them: write them sorted to produce canonical bencode.
