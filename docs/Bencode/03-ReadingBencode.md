---
title: Reading bencode
description: Parse bencode from memory, a stream or a pipe, navigate the values, read strings that may be binary, and handle invalid data.
---

## Parse bencode

[BencodeDocument](cref:Meziantou.Framework.Bencode.BencodeDocument) parses bencode from three sources. Each returns a document whose [Root](cref:Meziantou.Framework.Bencode.BencodeDocument.Root) is the decoded value:

| Method | Source |
| --- | --- |
| `BencodeDocument.Parse(ReadOnlySpan<byte>)` | Bytes in memory. |
| `BencodeDocument.ParseAsync(Stream)` | A stream, such as a file. The stream is left open. |
| `BencodeDocument.ParseAsync(PipeReader)` | A `PipeReader` from `System.IO.Pipelines`, such as a network connection. Complete the reader yourself. |

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

The data must hold exactly one value: bytes after it are an error. The asynchronous methods accept a
`CancellationToken`.

## Navigate the values

Every value derives from [BencodeValue](cref:Meziantou.Framework.Bencode.BencodeValue), and its [Kind](cref:Meziantou.Framework.Bencode.BencodeValue.Kind) tells which of the four kinds it is:

| Type | Content |
| --- | --- |
| [BencodeInteger](cref:Meziantou.Framework.Bencode.BencodeInteger) | `Value`, a `long`. |
| [BencodeString](cref:Meziantou.Framework.Bencode.BencodeString) | `Value`, the bytes as a `ReadOnlyMemory<byte>`. |
| [BencodeList](cref:Meziantou.Framework.Bencode.BencodeList) | An `IReadOnlyList<BencodeValue>`. |
| [BencodeDictionary](cref:Meziantou.Framework.Bencode.BencodeDictionary) | An `IReadOnlyDictionary<BencodeString, BencodeValue>`. |

Match the values with a `switch`, and look keys up with the indexer or [TryGetValue](cref:Meziantou.Framework.Bencode.BencodeDictionary.TryGetValue):

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

- Keys are compared byte by byte. Create them from bytes, such as a `u8` string literal.
- The indexer throws a `KeyNotFoundException` for a missing key; use [TryGetValue](cref:Meziantou.Framework.Bencode.BencodeDictionary.TryGetValue) when the key is optional.
- Enumerating a dictionary returns its entries in the order of the data, not sorted.

## Read strings

A bencode string holds bytes, and they are not always text. Choose how to read them:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/BencodeDocumentTests.cs;region=BencodeString_BinaryData;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new BencodeString(new byte[] { 0xFF, 0x00, 0x10 });

// The bytes are always available
Assert.Equal([0xFF, 0x00, 0x10], value.Value.ToArray());

// ToString is for diagnostics: it shows bytes that are not UTF-8 as hexadecimal
Assert.Equal("0xFF0010", value.ToString());

// ToUtf8String throws when the bytes are not valid UTF-8
Assert.Throws<DecoderFallbackException>(() => value.ToUtf8String());
```
<!-- endref -->

- `Value` returns the bytes, which is what binary data such as hashes needs.
- [ToUtf8String](cref:Meziantou.Framework.Bencode.BencodeString.ToUtf8String) decodes UTF-8 and throws a `DecoderFallbackException` when the bytes are not valid UTF-8.
- `ToString` is for logs and the debugger: it returns the text when the bytes are valid UTF-8, and hexadecimal
  otherwise, shortened after 32 bytes.

## Invalid data

Parsing throws a `FormatException` when the data is not valid bencode:

<!-- ref:../../tests/Meziantou.Framework.Bencode.Tests/BencodeDocumentTests.cs;region=BencodeDocument_InvalidData;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Throws<FormatException>(() => BencodeDocument.Parse("d3:cow3:moo"u8));   // unterminated dictionary
Assert.Throws<FormatException>(() => BencodeDocument.Parse("i03e"u8));          // leading zero
Assert.Throws<FormatException>(() => BencodeDocument.Parse("i1ei2e"u8));        // two root values
Assert.Throws<FormatException>(() => BencodeDocument.Parse("d1:ai1e1:ai2ee"u8)); // duplicate key
```
<!-- endref -->

The parser enforces the rules of the encoding: integers and string lengths without leading zeros, no `-0`, integers
that fit in a `long`, no duplicate key in a dictionary. It does not require the keys of a dictionary to be sorted, so
it reads data that was not encoded canonically.

## Untrusted data

The parsers are written for data from the network:

- Lists and dictionaries can be nested 64 levels deep. Deeper data throws a `FormatException` instead of overflowing
  the stack.
- When parsing from a stream or a pipe, the buffer of a string grows with the bytes that arrive, not with the length
  the data declares, and the digits of an integer or a length are bounded. A peer cannot make the parser allocate a
  large buffer by declaring a large string.
- [Parse](cref:Meziantou.Framework.Bencode.BencodeDocument.Parse) reads from memory, so the data is already in memory: limit its size before reading it.
