---
title: UTF-8 reader
description: Read YAML token by token from UTF-8 bytes without allocating, with the exact position of every token, from a buffer or a stream.
---

[Utf8YamlReader](cref:Meziantou.Framework.Yaml.Utf8YamlReader) is the lowest layer of the library: a `ref struct` that reads UTF-8 YAML one token at a time and
allocates nothing while it reads. The serializer and the [parser](07-ParserAndEmitter.md) are built on it. Use it when you
need speed, when you only need a few values of a large document, or when you need to know where each token is in the
input, for instance to edit a document without changing the rest of its text.

## Read the tokens

[Read](cref:Meziantou.Framework.Yaml.Utf8YamlReader.Read) moves to the next token and returns `false` at the end of the input. [TokenKind](cref:Meziantou.Framework.Yaml.Utf8YamlReader.TokenKind) tells what the token
is: the start or the end of a document, a mapping or a sequence, a scalar, or an alias.
[CurrentDepth](cref:Meziantou.Framework.Yaml.Utf8YamlReader.CurrentDepth) is the number of collections that contain the current position:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Utf8YamlReaderTests.cs;region=Utf8YamlReader_ReadTokens;format=md-fenced-code-block;dedent=true -->
```csharp
var utf8 = "name: web\nports: [80, 443]\n"u8;
var reader = new Utf8YamlReader(utf8);
var keys = new List<string>();
var ports = new List<long>();
while (reader.Read())
{
    if (reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.CurrentDepth == 1 && reader.IndicatorOffset < 0)
    {
        // A key of the root mapping: a scalar that no ':' precedes
        keys.Add(reader.GetString());
    }
    else if (reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.CurrentDepth == 2)
    {
        ports.Add(long.Parse(reader.GetString(), CultureInfo.InvariantCulture));
    }
}

Assert.Equal(["name", "ports"], keys);
Assert.Equal([80L, 443L], ports);
```
<!-- endref -->

The tokens of a mapping are its keys and its values, in order. A block mapping or sequence has no text of its own: its
start and end tokens are empty. A flow collection starts at its `[` or `{` and ends at its `]` or `}`.

## Values

A scalar is decoded only when you ask for its value: [GetString](cref:Meziantou.Framework.Yaml.Utf8YamlReader.GetString) creates the string, [CopyValue](cref:Meziantou.Framework.Yaml.Utf8YamlReader.CopyValue)
writes the UTF-8 value to a buffer of at least [MaxValueLength](cref:Meziantou.Framework.Yaml.Utf8YamlReader.MaxValueLength) bytes, and [ValueEquals](cref:Meziantou.Framework.Yaml.Utf8YamlReader.ValueEquals)
compares it with UTF-8 bytes, a string or a span of characters without allocating, which is how to match the keys of a
mapping. Decoding applies the
rules of the scalar style: escape sequences, line folding, and the chomping of block scalars.
[RawValueSpan](cref:Meziantou.Framework.Yaml.Utf8YamlReader.RawValueSpan) is the content as written, and [ValueIsRaw](cref:Meziantou.Framework.Yaml.Utf8YamlReader.ValueIsRaw) is `true` when it is the value
unchanged.

The reader does not interpret the scalars: a plain `42` and a quoted `"42"` are both scalars, with a different
[ScalarStyle](cref:Meziantou.Framework.Yaml.Utf8YamlReader.ScalarStyle). [GetTag](cref:Meziantou.Framework.Yaml.Utf8YamlReader.GetTag) resolves the tag of a node with the `%TAG` directives of its
document, and [AnchorName](cref:Meziantou.Framework.Yaml.Utf8YamlReader.AnchorName) gives the anchor of a node or the name an alias refers to.

## Positions

Every token knows where it is: [TokenStart](cref:Meziantou.Framework.Yaml.Utf8YamlReader.TokenStart) and [TokenLength](cref:Meziantou.Framework.Yaml.Utf8YamlReader.TokenLength) span its text (with the quotes of a
quoted scalar and the header of a block scalar), [IndicatorOffset](cref:Meziantou.Framework.Yaml.Utf8YamlReader.IndicatorOffset) is the `-`, `:` or `,` before
it, [ExplicitKeyIndicatorOffset](cref:Meziantou.Framework.Yaml.Utf8YamlReader.ExplicitKeyIndicatorOffset) the `?` of an explicit key, and [AnchorStart](cref:Meziantou.Framework.Yaml.Utf8YamlReader.AnchorStart) and
[TagStart](cref:Meziantou.Framework.Yaml.Utf8YamlReader.TagStart) its properties. Every byte of the input that is not whitespace, a line break or a comment
belongs to one of them, so the text between them is exactly the comments and the formatting:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Utf8YamlReaderTests.cs;region=Utf8YamlReader_Positions;format=md-fenced-code-block;dedent=true -->
```csharp
var utf8 = "image: 'nginx' # web server\n"u8;
var reader = new Utf8YamlReader(utf8);
while (reader.Read() && !(reader.TokenKind is Utf8YamlTokenKind.Scalar && reader.ValueEquals("nginx"u8)))
{
}

// The token spans the quotes; the value is decoded on demand
Assert.Equal("'nginx'", Encoding.UTF8.GetString(reader.TokenSpan));
Assert.Equal(7, reader.TokenStart);
Assert.Equal(5, reader.IndicatorOffset);
Assert.Equal(ScalarStyle.SingleQuoted, reader.ScalarStyle);
Assert.Equal("nginx", reader.GetString());
```
<!-- endref -->

The positions are byte offsets. [GetMark](cref:Meziantou.Framework.Yaml.Utf8YamlReader.GetMark) computes the line and column of one, like the errors report
them.

## Read a stream

When the input comes in parts, give each reader the bytes the previous one did not use, followed by the new data, and
the state of the previous reader. [Read](cref:Meziantou.Framework.Yaml.Utf8YamlReader.Read) returns `false` when it needs more data;
[BytesConsumed](cref:Meziantou.Framework.Yaml.Utf8YamlReader.BytesConsumed) tells how many bytes the next reader does not need, and
[CurrentState](cref:Meziantou.Framework.Yaml.Utf8YamlReader.CurrentState) is the state to pass on. Tell the last reader that its block is the final one:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Utf8YamlReaderTests.cs;region=Utf8YamlReader_PartialBuffers;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("items:\n- first\n- second\n- third\n"));
var buffer = new byte[16];
var length = 0;
var isFinalBlock = false;
var state = new Utf8YamlReaderState(new Utf8YamlReaderOptions { MaxDepth = 32 });
var values = new List<string>();
while (true)
{
    if (!isFinalBlock)
    {
        // Grow the buffer when the bytes that are left fill it: a token can be longer than the buffer
        if (length == buffer.Length)
        {
            Array.Resize(ref buffer, buffer.Length * 2);
        }

        var read = stream.Read(buffer, length, buffer.Length - length);
        length += read;
        isFinalBlock = read == 0;
    }

    var reader = new Utf8YamlReader(buffer.AsSpan(0, length), isFinalBlock, state);
    while (reader.Read())
    {
        if (reader.TokenKind is Utf8YamlTokenKind.Scalar)
        {
            values.Add(reader.GetString());
        }
    }

    if (isFinalBlock)
        break;

    // Keep the bytes that the next reader needs, and its state
    var consumed = reader.BytesConsumed;
    state = reader.CurrentState;
    buffer.AsSpan(consumed, length - consumed).CopyTo(buffer);
    length -= consumed;
}

Assert.Equal(["items", "first", "second", "third"], values);
```
<!-- endref -->

A token is only read when the rest of its line is in the buffer, and a multi-line token, such as a block scalar, when
all its lines are. Grow the buffer when a reader consumes nothing from a full buffer.

To write YAML as UTF-8, use the [UTF-8 writer](12-Utf8Writer.md).

## Errors and limits

The reader throws a [YamlException](cref:Meziantou.Framework.Yaml.YamlException) on invalid YAML, invalid UTF-8 and characters that YAML does not allow. In
a block that is not final, an error that more data could resolve makes [Read](cref:Meziantou.Framework.Yaml.Utf8YamlReader.Read) return `false` instead, and the
error is reported when the final block is read. [Utf8YamlReaderOptions.MaxDepth](cref:Meziantou.Framework.Yaml.Utf8YamlReaderOptions.MaxDepth) limits the nesting of the
collections, 64 by default.
