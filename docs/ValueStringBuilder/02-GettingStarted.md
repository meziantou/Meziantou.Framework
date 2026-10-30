---
title: Getting started
description: Install the package, build a string in a stack buffer, read the content and dispose the builder.
---

## Install the package

Add the `Meziantou.Framework.ValueStringBuilder` package to a project:

```bash
dotnet add package Meziantou.Framework.ValueStringBuilder
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Build a string

Create the builder with a stack-allocated buffer, append to it, and call `ToString`:

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
Span<char> initialBuffer = stackalloc char[64];
using var sb = new ValueStringBuilder(initialBuffer);

sb.Append("Hello");
sb.Append(' ');
sb.Append("World");

string text = sb.ToString();
Assert.Equal("Hello World", text);
```
<!-- endref -->

- The builder writes into `initialBuffer` while the text fits. Past 64 characters here, it rents a larger array from
  `ArrayPool<char>.Shared` and copies the text into it.
- Choose a buffer size that holds the usual text. Keep `stackalloc` buffers small, a few hundred characters at most:
  the stack is limited.
- `new ValueStringBuilder(initialCapacity)` starts with a rented array of at least that many characters instead of a
  stack buffer. See [Editing the content](04-EditingTheContent.md#capacity).

## Dispose the builder

The builder must be disposed to return the rented array to the pool. Declaring it with `using` is the simplest way.
`ToString` disposes it too: it returns the content **and** resets the builder to its default state, so a second call
returns an empty string, and appending starts a new buffer.

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_ToStringDisposes;format=md-fenced-code-block;dedent=true -->
```csharp
using var sb = new ValueStringBuilder(stackalloc char[64]);
sb.Append("Hello");

Assert.Equal("Hello", sb.ToString()); // and the builder is now disposed
Assert.Equal("", sb.ToString());
```
<!-- endref -->

Disposing twice is safe, so `using` combined with a final `ToString` is fine.

## Read the content without disposing

[AsSpan](cref:Meziantou.Framework.ValueStringBuilder.AsSpan) returns the content written so far as a `ReadOnlySpan<char>`, and the builder stays usable:

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_AsSpan;format=md-fenced-code-block;dedent=true -->
```csharp
using var sb = new ValueStringBuilder(stackalloc char[64]);
sb.Append("Hello");

ReadOnlySpan<char> content = sb.AsSpan(); // the builder is still usable
Assert.Equal("Hello", content);

sb.Append(" World");
Assert.Equal("Hello World", sb.AsSpan());
```
<!-- endref -->

`AsSpan(start)` and `AsSpan(start, length)` return a part of the content. Pass the span to an API that accepts
`ReadOnlySpan<char>`, such as `TextWriter.Write` or `int.Parse`, to use the text without creating a string. The span
points to the buffer of the builder: do not use it after the next append, which can move the content to a new buffer,
or after the builder is disposed.

## Next steps

- [Appending text](03-AppendingText.md): formatted values and interpolated strings.
- [Pitfalls](05-Pitfalls.md): passing the builder to methods, and `using` declarations.
