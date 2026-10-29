---
title: Coming from Markdig
description: The differences with Markdig, the library this one is derived from, and how to migrate.
---

`Meziantou.Framework.Markdown` is derived from [Markdig](https://github.com/xoofx/markdig) by Alexandre Mutel. Code written
for Markdig works after the changes below.

## Differences

- **Namespaces.** They start with `Meziantou.Framework.Markdown` instead of `Markdig`: replace `using Markdig;` with
  `using Meziantou.Framework.Markdown;`, and `Markdig.Syntax` with `Meziantou.Framework.Markdown.Syntax`.
- **`MarkdownConverter`.** The static `Markdig.Markdown` class is named `MarkdownConverter`, so that it does not share
  its name with the namespace: `Markdown.ToHtml(...)` becomes `MarkdownConverter.ToHtml(...)`.
- **.NET 10 and later only.** .NET Framework and .NET Standard are not supported.
- **`HostProviderBuilder`** is a static class.
- **Generic attributes are filtered.** By default, `UseGenericAttributes` only writes the attributes that describe the
  content (see [Attributes](04-Extensions/06-GenericAttributes.md#choose-the-allowed-attributes)). Markdig writes every
  attribute; pass `_ => true` as the filter of `UseGenericAttributes` to do the same.
- **No self pipeline.** `UseSelfPipeline` is removed: it let the Markdown document choose the extensions of the
  pipeline, including removing the ones that the host used to make the output safe.
