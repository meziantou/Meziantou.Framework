---
title: Pitfalls
description: Pass the builder by reference, know what using declarations and ToString do, and do not read past the content.
---

[ValueStringBuilder](cref:Meziantou.Framework.ValueStringBuilder) is a mutable struct that owns a rented array. The C# rules for structs, and the pool, lead to a few
mistakes that compile fine.

## Pass the builder by reference

Passing a struct to a method copies it. A method that appends to a copy changes neither the [Length](cref:Meziantou.Framework.ValueStringBuilder.Length) of the caller's
builder nor its content. Worse, when the copy grows, it returns the rented array it shares with the caller to the pool, while
the caller still writes into it. Pass the builder with `ref`:

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_PassByRef;format=md-fenced-code-block;dedent=true -->
```csharp
var sb = new ValueStringBuilder(stackalloc char[64]);
AppendGreeting(ref sb, "Alice");
AppendGreeting(ref sb, "Bob");
Assert.Equal("Hello Alice! Hello Bob! ", sb.ToString());

// A ValueStringBuilder is a struct: pass it by reference, or the method appends to a copy
static void AppendGreeting(ref ValueStringBuilder sb, string name)
{
    sb.Append("Hello ");
    sb.Append(name);
    sb.Append("! ");
}
```
<!-- endref -->

The same applies to assignments: `var other = sb;` creates a second builder that shares the buffer of the first one
until either grows. Never dispose both.

## Using declarations make the builder read-only

A variable declared with `using` is read-only. Methods such as [Append](cref:Meziantou.Framework.ValueStringBuilder.Append) still work on it, but you cannot set [Length](cref:Meziantou.Framework.ValueStringBuilder.Length)
or pass the variable by `ref`: the compiler reports CS1654 or CS1657. In that case, declare the builder without `using`
and dispose it yourself:

- End with `ToString`, which disposes the builder, as in the example above.
- Or call [Dispose](cref:Meziantou.Framework.ValueStringBuilder.Dispose) in a `finally` block when the builder does not end with `ToString`.

If an exception is thrown before the builder is disposed, the rented array is not returned to the pool: the garbage
collector reclaims it, and the pool rents a new one next time. Nothing else is lost.

## ToString disposes the builder

`ToString` returns the content and disposes the builder. Calling it to log the content in the middle of the work
empties the builder. Use [AsSpan](cref:Meziantou.Framework.ValueStringBuilder.AsSpan) to read the content without disposing the builder, and keep
`ToString` for the end.

## The buffer is not cleared

The rented arrays come from `ArrayPool<char>.Shared`, which every part of the application shares:

- [RawChars](cref:Meziantou.Framework.ValueStringBuilder.RawChars) returns the whole buffer, not only the content. Everything past [Length](cref:Meziantou.Framework.ValueStringBuilder.Length) is uninitialized pooled memory,
  which may contain text left by a previous user of the array. Use [AsSpan](cref:Meziantou.Framework.ValueStringBuilder.AsSpan) to read the content.
- Increasing [Length](cref:Meziantou.Framework.ValueStringBuilder.Length) exposes those characters as part of the content.
- [Clear](cref:Meziantou.Framework.ValueStringBuilder.Clear) and [Dispose](cref:Meziantou.Framework.ValueStringBuilder.Dispose) do not erase the content, and neither does growing the buffer, which returns the previous array
  to the pool with the text it holds: the next code that rents the array can read it. Do not build secrets, such as
  passwords or tokens, in a [ValueStringBuilder](cref:Meziantou.Framework.ValueStringBuilder).

## Do not use the builder after it is disposed

After [Dispose](cref:Meziantou.Framework.ValueStringBuilder.Dispose) or `ToString`, the builder is reset: appending starts a new buffer. A span obtained from [AsSpan](cref:Meziantou.Framework.ValueStringBuilder.AsSpan),
[RawChars](cref:Meziantou.Framework.ValueStringBuilder.RawChars) or [AppendSpan](cref:Meziantou.Framework.ValueStringBuilder.AppendSpan) before that may point to an array that was returned to the pool and rented by someone else.
Do not keep such spans past the next change of the builder.
