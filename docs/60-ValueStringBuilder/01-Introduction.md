---
title: Introduction
description: A string builder that writes into a stack buffer and only rents a pooled array when the text outgrows it.
---

`Meziantou.Framework.ValueStringBuilder` provides `ValueStringBuilder`, a string builder for code that builds many short
strings and should allocate as little as possible: formatters, parsers, serializers, code generators.

`StringBuilder` is a class that allocates its own buffers. `ValueStringBuilder` is a `ref struct` that writes into a
buffer you give it, typically allocated on the stack with `stackalloc`. When the text outgrows that buffer, it rents a
larger array from `ArrayPool<char>.Shared`, and returns it when it is disposed. Building a string that fits in the
initial buffer allocates nothing but the final string.

It is inspired by the `ValueStringBuilder` that the .NET runtime uses internally.

## What you can do with it

- **Build a string** in a stack buffer. See [Getting started](02-GettingStarted.md).
- **Append text and values**: characters, strings, spans, Unicode scalar values, formatted numbers and interpolated
  strings. See [Appending text](03-AppendingText.md).
- **Edit the content**: insert, replace, truncate, or write directly into the buffer. See
  [Editing the content](04-EditingTheContent.md).
- **Avoid the pitfalls** of a mutable struct that holds pooled memory. See [Pitfalls](05-Pitfalls.md).

## When to use it

`ValueStringBuilder` pays off in code that runs often and builds strings of a predictable, modest length. It has the
constraints of a `ref struct`: it cannot be stored in a field of a class, captured by a lambda, or kept across an
`await`. For a long-lived builder, or a string built across asynchronous calls, use `StringBuilder`.

## Namespace

`ValueStringBuilder` is in the `Meziantou.Framework` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and build your first string.
- [Pitfalls](05-Pitfalls.md): read it before using the builder in production code.
