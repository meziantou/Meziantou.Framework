---
title: Supported languages
description: The languages the highlighter supports, their identifiers and aliases, and how to check an identifier.
---

`Highlight` and `TryHighlight` take the identifier of a language or one of its aliases. They are case-insensitive.
`IsSupported` checks an identifier, and `GetSupportedLanguages` returns every identifier and alias, in ordinal order:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_IsSupported;format=md-fenced-code-block;dedent=true -->
```csharp
// Identifiers and aliases are case-insensitive
Assert.True(SyntaxHighlighter.IsSupported("C#"));
Assert.True(SyntaxHighlighter.IsSupported("cs"));
Assert.False(SyntaxHighlighter.IsSupported("not-a-language"));

var languages = SyntaxHighlighter.GetSupportedLanguages();
Assert.Contains("csharp", languages);
```
<!-- endref -->

## Languages

| Language | Identifier and aliases |
| --- | --- |
| Apache configuration | `apache`, `apacheconf`, `htaccess` |
| Arduino | `arduino`, `ino` |
| ARM assembly | `armasm`, `arm` |
| AsciiDoc | `asciidoc`, `adoc` |
| Awk | `awk`, `gawk`, `mawk`, `nawk` |
| Bash | `bash`, `sh`, `zsh`, `ksh` |
| Bicep | `bicep`, `bicepparam` |
| BNF | `bnf` |
| C | `c`, `h` |
| Clojure | `clojure`, `clj`, `edn` |
| Clojure REPL | `clojure-repl` |
| CMake | `cmake`, `cmake.in` |
| CoffeeScript | `coffeescript`, `coffee`, `cson`, `iced` |
| C++ | `cpp`, `c++`, `cc`, `h++`, `hpp`, `hh`, `hxx`, `cxx` |
| Crystal | `crystal`, `cr` |
| C# | `csharp`, `cs`, `c#` |
| CSS | `css` |
| D | `d` |
| Dart | `dart` |
| Delphi | `delphi`, `dpr`, `dfm`, `pas`, `pascal` |
| Diff | `diff`, `patch` |
| Django | `django`, `jinja`, `jinja2`, `j2` |
| DNS zone file | `dns`, `bind`, `zone` |
| Dockerfile | `dockerfile`, `docker` |
| Batch file | `dos`, `bat`, `cmd` |
| Elixir | `elixir`, `ex`, `exs` |
| Elm | `elm` |
| ERB | `erb` |
| Erlang | `erlang`, `erl` |
| Erlang REPL | `erlang-repl` |
| Fortran | `fortran`, `f90`, `f95`, `f03`, `f08`, `f77`, `for`, `f` |
| F# | `fsharp`, `fs`, `f#` |
| Gherkin | `gherkin`, `feature` |
| GLSL | `glsl`, `vert`, `frag` |
| Go | `go`, `golang` |
| GraphQL | `graphql`, `gql` |
| Groovy | `groovy`, `gradle` |
| Haml | `haml` |
| Handlebars | `handlebars`, `hbs`, `html.hbs`, `html.handlebars`, `htmlbars`, `mustache` |
| Haskell | `haskell`, `hs` |
| HCL | `hcl`, `terraform`, `tf`, `tfvars` |
| HTML | `html`, `htm`, `xhtml` |
| HTTP | `http`, `https` |
| INI | `ini`, `gitconfig` |
| Java | `java`, `jsp` |
| JavaScript | `javascript`, `js`, `jsx`, `mjs`, `cjs` |
| JSON | `json`, `jsonc` |
| Julia | `julia`, `jl` |
| Julia REPL | `julia-repl`, `jldoctest` |
| Kotlin | `kotlin`, `kt`, `kts` |
| KQL | `kql`, `kusto` |
| LaTeX | `latex`, `tex` |
| Less | `less` |
| Lisp | `lisp`, `elisp`, `emacs-lisp`, `common-lisp` |
| LLVM IR | `llvm`, `ll` |
| Lua | `lua`, `pluto` |
| Makefile | `makefile`, `mk`, `mak`, `make` |
| Markdown | `markdown`, `md`, `mkdown`, `mkd` |
| MATLAB | `matlab` |
| Mermaid | `mermaid`, `mmd` |
| MSIL | `msil`, `il`, `cil` |
| Nginx | `nginx`, `nginxconf` |
| Nim | `nim`, `nims` |
| Nix | `nix`, `nixos` |
| Node.js REPL | `node-repl` |
| Objective-C | `objectivec`, `mm`, `objc`, `obj-c`, `obj-c++`, `objective-c++` |
| OCaml | `ocaml`, `ml` |
| Perl | `perl`, `pl`, `pm` |
| PostgreSQL | `pgsql`, `postgres`, `postgresql` |
| PHP | `php` |
| Plain text | `plaintext`, `text`, `txt` |
| PowerShell | `powershell`, `pwsh`, `ps`, `ps1` |
| Prolog | `prolog` |
| Properties | `properties` |
| Protocol Buffers | `protobuf`, `proto` |
| Puppet | `puppet`, `pp` |
| Python | `python`, `py`, `gyp`, `ipython` |
| Python REPL | `python-repl`, `pycon` |
| R | `r` |
| Razor | `razor`, `cshtml`, `cshtml-razor` |
| Ruby | `ruby`, `rb`, `gemspec`, `podspec`, `thor`, `irb` |
| Rust | `rust`, `rs` |
| Scala | `scala` |
| Scheme | `scheme`, `scm` |
| SCSS | `scss` |
| Shell session | `shell`, `console`, `shellsession` |
| Solidity | `solidity`, `sol` |
| SQL | `sql` |
| Svelte | `svelte` |
| Swift | `swift` |
| Tcl | `tcl`, `tk` |
| Thrift | `thrift` |
| TOML | `toml` |
| Twig | `twig`, `craftcms` |
| TypeScript | `typescript`, `ts`, `tsx`, `mts`, `cts` |
| URL-encoded form | `urlencoded`, `x-www-form-urlencoded` |
| VB.NET | `vbnet`, `vb` |
| VBScript | `vbscript`, `vbs` |
| VBScript in HTML | `vbscript-html` |
| Verilog | `verilog`, `v`, `sv`, `svh` |
| VHDL | `vhdl`, `vhd` |
| Vim script | `vim`, `vimscript` |
| Vue | `vue` |
| WebAssembly text | `wasm`, `wat`, `wast` |
| x86 assembly | `x86asm` |
| XML | `xml`, `xsd`, `xsl`, `plist`, `rss`, `atom`, `svg` |
| YAML | `yaml`, `yml` |
| Zig | `zig`, `zon` |

`ml` is OCaml, and `gradle` is Groovy.

## Language identifiers from Markdown

The identifiers are the names commonly written after the opening fence of a Markdown code block, such as `csharp` or
`ts`, so you can pass that name as is. It comes from the author of the document: use `TryHighlight` and render the code
as plain text when the language is not supported. See [Untrusted input](06-UntrustedInput.md#unknown-languages).
