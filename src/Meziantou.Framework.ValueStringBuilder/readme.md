# Meziantou.Framework.ValueStringBuilder

`Meziantou.Framework.ValueStringBuilder` provides a high-performance `ValueStringBuilder` as a `ref struct`, inspired by the .NET runtime implementation. It is in the `Meziantou.Framework` namespace.

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

## Disposing the builder

The builder rents its buffer from `ArrayPool<char>.Shared` as soon as it outgrows the initial span, so it must be
disposed to return that buffer. Declaring it with `using` is the simplest way to guarantee it.

`ToString()` is destructive: it returns the content **and** disposes the builder. After calling it, the builder is
reset to its default state, so a second call returns an empty string and appending starts a new buffer.

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_ToStringDisposes;format=md-fenced-code-block;dedent=true -->
```csharp
using var sb = new ValueStringBuilder(stackalloc char[64]);
sb.Append("Hello");

Assert.Equal("Hello", sb.ToString()); // and the builder is now disposed
Assert.Equal("", sb.ToString());
```
<!-- endref -->

Use `AsSpan()` when you need to read the content without disposing the builder:

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

Disposing twice is safe, so combining `using` with a final `ToString()` is fine.

## Reading past the content

`RawChars` exposes the whole buffer, not just the part that has been written. Everything beyond `Length` is
uninitialized pooled memory and may contain data left by a previous user of the array.
