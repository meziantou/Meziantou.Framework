---
title: Performance
description: When grammars are compiled, and how to make a long-running process highlight faster.
---

The grammar of a language is compiled the first time you highlight code in that language, and kept for the next calls.
The first call for a language is slower than the next ones.

## Compiled regular expressions

By default, the grammars run with the regular expression interpreter, which starts fast. A long-running process that
highlights a lot of code, such as a web server or a site generator, can use compiled regular expressions instead: they
cost more to create, and scan several times faster. Turn on the `Meziantou.Framework.SyntaxHighlighting.UseCompiledRegex`
switch, either in the project file of the application, with
`<RuntimeHostConfigurationOption Include="Meziantou.Framework.SyntaxHighlighting.UseCompiledRegex" Value="true" />` in an
`ItemGroup`, or in code, with `AppContext.SetSwitch("Meziantou.Framework.SyntaxHighlighting.UseCompiledRegex", true)`
before the first call to the highlighter.

The switch is read when a grammar is compiled: setting it after the first call does not change the grammars already
compiled.

## Timeouts

Each `MatchTimeout` value compiles the grammars again: keep the same value for the whole application. See
[Bound the matching time](06-UntrustedInput.md#bound-the-matching-time).
