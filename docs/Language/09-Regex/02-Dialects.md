---
title: Dialects
description: Choose the dialect of a pattern, and learn what each one supports and how the others read what they lack.
---

The same characters mean different things in different engines, so a pattern is always parsed in a dialect. Pass one of
the [RegexDialect](cref:Meziantou.Framework.Language.Regex.RegexDialect) values to [ParseText](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.ParseText), or in [RegexParseOptions](cref:Meziantou.Framework.Language.Regex.RegexParseOptions):

| [RegexDialect](cref:Meziantou.Framework.Language.Regex.RegexDialect) | [Family](cref:Meziantou.Framework.Language.Regex.RegexDialect.Family) | Reads |
| --- | --- | --- |
| [Net](cref:Meziantou.Framework.Language.Regex.RegexDialect.Net) | [Dotnet](cref:Meziantou.Framework.Language.Regex.RegexDialectFamily.Dotnet) | The patterns of `System.Text.RegularExpressions`: balancing groups, character class subtraction, conditionals, `(?#…)` comments, extended mode |
| [JavaScript](cref:Meziantou.Framework.Language.Regex.RegexDialect.JavaScript) | [JavaScript](cref:Meziantou.Framework.Language.Regex.RegexDialect.JavaScript) | ECMAScript patterns: the `u` and `v` flags, `\u{…}`, `[]` and `[^]`, class set operations, `(?ims-ims:…)` modifiers, duplicate group names in different alternatives |
| [PcrePerl](cref:Meziantou.Framework.Language.Regex.RegexDialect.PcrePerl) | [Pcre](cref:Meziantou.Framework.Language.Regex.RegexDialectFamily.Pcre) | PCRE and Perl patterns: possessive quantifiers, atomic groups, `\Q…\E`, `\K`, POSIX brackets, recursion and subroutine calls, the `\g` family, callouts, verbs and alpha assertions, branch reset groups |
| [PosixExtended](cref:Meziantou.Framework.Language.Regex.RegexDialect.PosixExtended) | [Posix](cref:Meziantou.Framework.Language.Regex.RegexDialectFamily.Posix) | POSIX extended regular expressions (ERE), with the GNU `\w`, `\s`, `\b`, `\<` and `\>` |
| [PosixBasic](cref:Meziantou.Framework.Language.Regex.RegexDialect.PosixBasic) | [Posix](cref:Meziantou.Framework.Language.Regex.RegexDialectFamily.Posix) | POSIX basic regular expressions (BRE): `\(…\)` groups, `\{n,m\}` bounds, and the GNU `\|`, `\+` and `\?` |

Java, Python and Go patterns are not supported. [RegexDialect.TryParse](cref:Meziantou.Framework.Language.Regex.RegexDialect.TryParse) reads a dialect from its name, and [Name](cref:Meziantou.Framework.Language.Regex.RegexDialect.Name) gives
it.

## What a dialect supports

[Features](cref:Meziantou.Framework.Language.Regex.RegexDialect.Features) lists what a dialect supports, as [RegexDialectFeatures](cref:Meziantou.Framework.Language.Regex.RegexDialectFeatures) flags, and [HasFeature](cref:Meziantou.Framework.Language.Regex.RegexDialect.HasFeature) tests one. Where a dialect
lacks a construct, the parser does what its engine does:

- When the text has an ordinary meaning in the dialect, that is what it gets: `\A` is the letter `A` in JavaScript, and
  `[a-z-[aeiou]]` in PCRE is the class `[a-z-[aeiou]` followed by a `]`.
- When it does not, such as `(?>…)` in JavaScript, which has no atomic groups, it is reported and read as a non-capturing
  group, so its content is still parsed.

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Dialects;format=md-fenced-code-block;dedent=true -->
```csharp
// An atomic group is valid in .NET, and reported in JavaScript, which has no syntax for it
Assert.Empty(RegexSyntaxTree.ParseText("(?>a+)b", RegexDialect.Net).GetDiagnostics());
var diagnostic = Assert.Single(RegexSyntaxTree.ParseText("(?>a+)b", RegexDialect.JavaScript).GetDiagnostics());
Assert.Equal("REGEX0010", diagnostic.Id);

// \A is an anchor in .NET, and the letter A in JavaScript
Assert.Single(RegexSyntaxTree.ParseText(@"\Ahello", RegexDialect.Net).GetRoot().DescendantNodes().OfType<RegexAnchorSyntax>());
Assert.Empty(RegexSyntaxTree.ParseText(@"\Ahello", RegexDialect.JavaScript).GetRoot().DescendantNodes().OfType<RegexAnchorSyntax>());

// What each dialect supports
Assert.True(RegexDialect.Net.HasFeature(RegexDialectFeatures.BalancingGroups));
Assert.False(RegexDialect.JavaScript.HasFeature(RegexDialectFeatures.AtomicGroups));
```
<!-- endref -->

## .NET

The [Net](cref:Meziantou.Framework.Language.Regex.RegexDialect.Net) dialect covers the whole regular-expression language of .NET, balancing groups included (`(?<c-o>…)`,
`(?'c-o'…)` and `(?<-o>…)`), and numbers the capture groups as the engine does.

It follows the current .NET engine, which changes between releases: .NET 10 rejects `(?(name)(?n))`, which .NET 11
accepts, and knows fewer Unicode block names. On an older runtime, the parser may accept a pattern that the runtime's own
engine rejects.

## JavaScript

The [JavaScript](cref:Meziantou.Framework.Language.Regex.RegexDialect.JavaScript) dialect follows the grammar the flags select. Without `u` or `v`, the web-compatible grammar applies: a
malformed escape stands for its own letter, and `\x4` matches `x4`. With them, the grammar is strict and the same text is
an error. `u` makes the pattern a sequence of code points and enables `\u{…}`; `v` adds nested classes, `&&`, `--`, and
`\q{…}` string disjunctions. The names of `\p{…}` are checked against the specification.

The specification forbids two groups of the same name that can both take part in a match, such as
`(?<a>x)(?:y|(?<a>z))`, and so does the parser.

There is no `\A`, `\Z`, `\z` or `\G`, no atomic group, and no extended mode in JavaScript.

## PCRE

The [PcrePerl](cref:Meziantou.Framework.Language.Regex.RegexDialect.PcrePerl) dialect covers recursion and subroutine calls in every spelling (`(?R)`, `(?1)`, `(?-1)`, `(?&name)`,
`(?P>name)`, `\g<name>`), the `\g` references, `(?P=name)`, callouts, backtracking verbs, start-of-pattern options,
alpha assertions such as `(*plb:…)`, every PCRE condition (`(?(<name>)…)`, `(?(R1)…)`, `(?(DEFINE)…)`,
`(?(VERSION>=10.4)…)`, assertions), `\Q…\E` inside and outside a class, the `J`, `U`, `^`, `r` and `aD` options, and loosely
matched `\p{…}` names. A lookbehind must have a bounded length.

Where PCRE and Perl differ, it follows PCRE: `{2}` with nothing to repeat, `[a-\d]` and `{5,2}` are errors.

## POSIX

The [PosixExtended](cref:Meziantou.Framework.Language.Regex.RegexDialect.PosixExtended) and [PosixBasic](cref:Meziantou.Framework.Language.Regex.RegexDialect.PosixBasic) dialects cover bracket expressions, collating elements and equivalence classes, in
which a backslash is an ordinary character. POSIX has no character escapes: `\x41`, `\cA`, `\n` and `\k` are the letters
`x41`, `cA`, `n` and `k`. The shorthand classes they accept, `\w`, `\s`, `\b` and their negations, are GNU extensions.

In a basic expression, `\(…\)` groups, `\{n,m\}` bounds and backreferences are in the tree, and `^`, `$` and `*` are
ordinary characters where they cannot be special. Where POSIX leaves a construct undefined, the parser reads it as
follows: `a**` is valid in an extended expression, a `{` always starts an interval, and a backreference must name a
group already closed in its branch.
