---
title: Loading icon sets
description: Load a file in the IconifyJSON format at run time, and find its icons by name.
---

When the icon sets are not known at build time, for instance when users provide their own icons, load them at run
time with [Iconify.Load](cref:Meziantou.Framework.Iconify.Load):

<!-- ref:../../tests/Meziantou.Framework.Iconify.Tests/IconifyIconSetTests.cs;region=Iconify_Load;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-icons.json");
IconifyIconSet iconSet = Iconify.Load(path);
IconifyIcon icon = iconSet["home"];
Assert.Equal("home", icon.Name);
Assert.Equal(24d, icon.Width);
Assert.Equal("""<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path fill="currentColor" d="M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z"/></svg>""", icon.Svg);

if (iconSet.TryGetIcon("house", out var alias))
{
    Assert.Equal(icon.Body, alias.Body);
}
```
<!-- endref -->

- [Iconify.Load](cref:Meziantou.Framework.Iconify.Load) reads a file or a stream, and
  [Iconify.LoadAsync](cref:Meziantou.Framework.Iconify.LoadAsync) reads it asynchronously.
  [Iconify.Parse](cref:Meziantou.Framework.Iconify.Parse) reads a string.
- The file is in the [IconifyJSON](https://iconify.design/docs/types/iconify-json.html) format, like the files of the
  `json` folder of the [iconify/icon-sets](https://github.com/iconify/icon-sets) repository. A file or a stream must be
  encoded in UTF-8.
- An invalid file throws a `FormatException`.

## Find icons

An [IconifyIconSet](cref:Meziantou.Framework.IconifyIconSet) contains the icons and the aliases of the file, with the
aliases resolved:

- the indexer returns an icon by name, and throws a `KeyNotFoundException` when there is none;
- [TryGetIcon](cref:Meziantou.Framework.IconifyIconSet.TryGetIcon) and
  [ContainsIcon](cref:Meziantou.Framework.IconifyIconSet.ContainsIcon) check whether an icon exists;
- [Icons](cref:Meziantou.Framework.IconifyIconSet.Icons) lists every icon.

The names are compared case-sensitively, as Iconify does: use the names of the file (`account-circle`), not the
property names of the generated code (`AccountCircle`).

The list includes the hidden icons, which the icon set keeps for backward compatibility only:
[IsHidden](cref:Meziantou.Framework.IconifyIcon.IsHidden) is `true` for them. An alias whose parent does not exist is
left out.

## Read the license

[IconifyIconSet.Info](cref:Meziantou.Framework.IconifyIconSet.Info) gives the name, author and license of the icon set,
as an [IconifyIconSetInfo](cref:Meziantou.Framework.IconifyIconSetInfo). It is `null` when the file has no `info`
property.
