# Meziantou.Framework.SyntaxHighlighting

`Meziantou.Framework.SyntaxHighlighting` highlights source code and returns HTML markup with CSS classes.

The package uses class names compatible with `highlight.js` by default, so you can reuse existing `highlight.js` themes without changing the generated HTML.

## Usage

```csharp
using Meziantou.Framework.SyntaxHighlighting;

var html = SyntaxHighlighter.Highlight(
    "public class MyClass { }",
    "csharp");

Console.WriteLine(html);
// <span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">MyClass</span> { }
```

## Custom CSS prefix

If you do not want to use the default `hljs-` prefix, you can customize it:

```csharp
using Meziantou.Framework.SyntaxHighlighting;

var html = SyntaxHighlighter.Highlight(
    "public class MyClass { }",
    "csharp",
    new HighlightOptions { ClassPrefix = "syntax-" });

Console.WriteLine(html);
// <span class="syntax-keyword">public</span> <span class="syntax-keyword">class</span> <span class="syntax-title">MyClass</span> { }
```

## Styling

The package only produces HTML. You must provide the CSS rules for the generated classes.

With the default options, the generated markup is designed to work well with highlight.js themes. For example:

```html
<pre><code><span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">MyClass</span> { }</code></pre>
```

## Supported languages

The package currently supports these language identifiers and common aliases:

- `bash`, `sh`, `zsh`, `ksh`
- `bnf`
- `cpp`, `c++`, `cc`, `h++`, `hpp`, `hh`, `hxx`, `cxx`
- `csharp`, `cs`, `c#`
- `css`
- `dockerfile`, `docker`
- `dos`, `bat`, `cmd`
- `fsharp`, `fs`, `f#`
- `graphql`, `gql`
- `html`, `htm`, `xhtml`
- `http`, `https`
- `ini`, `toml`, `gitconfig`
- `javascript`, `js`, `jsx`, `mjs`, `cjs`
- `json`, `jsonc`
- `less`
- `markdown`, `md`, `mkdown`, `mkd`
- `nginx`, `nginxconf`
- `php`
- `powershell`, `pwsh`, `ps`, `ps1`
- `razor`, `cshtml`, `cshtml-razor`
- `scss`
- `sql`
- `typescript`, `ts`, `tsx`, `mts`, `cts`
- `urlencoded`, `x-www-form-urlencoded`
- `vbnet`, `vb`
- `x86asm`
- `xml`, `xsd`, `xsl`, `plist`, `rss`, `atom`, `svg`
- `yaml`, `yml`
