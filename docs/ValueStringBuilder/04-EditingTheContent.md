---
title: Editing the content
description: Insert text, replace characters, truncate or clear the content, write directly into the buffer and manage the capacity.
---

## Insert, replace, truncate and clear

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_Edit;format=md-fenced-code-block;dedent=true -->
```csharp
// Not a using declaration: it would make the variable read-only, and Length could not be set
var sb = new ValueStringBuilder(stackalloc char[64]);
sb.Append("Hello World");

sb.Insert(5, ",");
sb.Insert(0, ">> ");
sb[3] = 'h';                          // the indexer returns a reference: assign to replace a character
Assert.Equal(">> hello, World", sb.AsSpan());

sb.Length = 9;                        // setting Length truncates the content
Assert.Equal(">> hello,", sb.AsSpan());

sb.Clear();                           // Length is 0 again; the buffer is kept
sb.Append("Done");

// ToString returns the content and disposes the builder
Assert.Equal("Done", sb.ToString());
```
<!-- endref -->

- `Insert(index, string?)` inserts a string, and `Insert(index, char, count)` a character repeated `count` times. The
  index must be between 0 and [Length](cref:Meziantou.Framework.ValueStringBuilder.Length).
- The indexer returns a reference to a character of the content: read it, or assign it to replace the character. The
  index must be less than [Length](cref:Meziantou.Framework.ValueStringBuilder.Length).
- Setting [Length](cref:Meziantou.Framework.ValueStringBuilder.Length) to a smaller value truncates the content. Setting it to a larger value exposes the characters already
  present in the buffer, which may be anything: see [Pitfalls](05-Pitfalls.md#the-buffer-is-not-cleared).
- [Clear](cref:Meziantou.Framework.ValueStringBuilder.Clear) sets [Length](cref:Meziantou.Framework.ValueStringBuilder.Length) to 0 and keeps the buffer, so the builder can be reused without growing again.

The builder in this example is not declared with `using`: a `using` declaration makes the variable read-only, so
[Length](cref:Meziantou.Framework.ValueStringBuilder.Length) could not be set. See [Pitfalls](05-Pitfalls.md#using-declarations-make-the-builder-read-only).

## Write directly into the buffer

`AppendSpan(length)` reserves `length` characters at the end of the content and returns them as a `Span<char>`, for
APIs that write into a span, such as `TryFormat` or `Convert.TryToBase64Chars`:

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_AppendSpan;format=md-fenced-code-block;dedent=true -->
```csharp
using var sb = new ValueStringBuilder(stackalloc char[64]);
sb.Append("id=");

// Reserve 8 characters at the end of the content and write them directly
var destination = sb.AppendSpan(8);
Assert.True(0xCAFE.TryFormat(destination, out var written, "X8", CultureInfo.InvariantCulture));
Assert.Equal(8, written);

Assert.Equal("id=0000CAFE", sb.AsSpan());
```
<!-- endref -->

[Length](cref:Meziantou.Framework.ValueStringBuilder.Length) is advanced immediately: the reserved characters are part of the content whether you write them or not. Reserve
exactly the number of characters you write, or reduce [Length](cref:Meziantou.Framework.ValueStringBuilder.Length) afterwards.

## Capacity

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_Capacity;format=md-fenced-code-block;dedent=true -->
```csharp
// Without an initial buffer, the builder rents one of at least 256 characters from ArrayPool<char>.Shared
using var sb = new ValueStringBuilder(initialCapacity: 256);
Assert.True(sb.Capacity >= 256);

// Grow once before a series of appends whose total length you know
sb.EnsureCapacity(1024);
Assert.True(sb.Capacity >= 1024);
```
<!-- endref -->

- [Capacity](cref:Meziantou.Framework.ValueStringBuilder.Capacity) is the number of characters the current buffer holds before it has to grow.
- `EnsureCapacity(capacity)` grows the buffer once to hold at least `capacity` characters. Call it before a series of
  appends whose total length you know, rather than letting the buffer grow several times.
- Growing rents a larger array from the pool, copies the content into it, and returns the previous rented array.

## Use the builder with native code

[NullTerminate](cref:Meziantou.Framework.ValueStringBuilder.NullTerminate) writes a null character after the content, growing the buffer if needed, without changing [Length](cref:Meziantou.Framework.ValueStringBuilder.Length).
[GetPinnableReference](cref:Meziantou.Framework.ValueStringBuilder.GetPinnableReference) returns a reference to the first character, so the builder can be used in a `fixed` statement to
pass the content to native code. The content is only null-terminated after a call to [NullTerminate](cref:Meziantou.Framework.ValueStringBuilder.NullTerminate).
