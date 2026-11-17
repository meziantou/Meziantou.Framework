# Extension specifications

The examples of the extensions that `Meziantou.Framework.Markdown` 1.x supported, copied from its tests. Most of them
come from [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause, Copyright (c) 2016-2026, Alexandre Mutel; see
`src/Meziantou.Framework.Markdown/THIRD-PARTY-NOTICES.md`), and the GFM ones from the
[GitHub Flavored Markdown specification](https://github.github.com/gfm/) (CC BY-SA 4.0).

`ExtensionSpecTests` runs the examples of the extensions the parser supports. Where the GFM specification defines a
construct (tables, task lists, strikethrough, autolinks), the expected HTML is the one of the GFM specification.
