---
title: Introduction
description: What Slug does, and the rules of the slugs it generates.
---

`Meziantou.Framework.Slug` turns a text, such as the title of a blog post or a product name, into a slug: a short text
made of letters, digits and separators that fits in a URL or a file name. `Crème brûlée à la française` becomes
`Creme-brulee-a-la-francaise`.

## Rules

By default, `Slug.Create`:

- removes the diacritics, so `é` becomes `e`;
- keeps the letters `a`-`z` and `A`-`Z` and the digits `0`-`9`, and keeps their case;
- replaces any other run of characters by a single `-`, and never starts or ends the slug with it;
- cuts the slug to 80 characters.

Every rule can be changed with `SlugOptions`. See [Options](03-Options.md).

## Slugs are not unique

Different texts can give the same slug: `Hello, World!` and `hello world` both give `hello-world` in lower case, and
texts that differ after the maximum length give the same slug. When a slug identifies a page or a file, check that it
is not already used, for instance by adding a number. A slug is never a security boundary.

## Namespaces

`Slug`, `SlugOptions` and `CasingTransformation` are in the [Meziantou.Framework](cref:Meziantou.Framework) namespace.
