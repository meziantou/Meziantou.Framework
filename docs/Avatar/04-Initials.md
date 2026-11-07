---
title: Initials
description: How the one or two characters of an avatar are chosen from the name, and how to choose them yourself.
---

The text of an avatar, its bigram, has one or two graphemes. The generator chooses it from the name, unless you set
[AvatarOptions.Bigram](cref:Meziantou.Framework.AvatarOptions.Bigram).

## How the initials are chosen

The name is split into words on whitespace and on the connectors used inside compound names: `-`, `'`, `.`, `_` and
their typographic variants. Then:

- **Two words or more**: the first grapheme of the first word, followed by the first grapheme of the last word.
- **One word**: its first two graphemes.

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_Initials;format=md-fenced-code-block;dedent=true -->
```csharp
var threeWords = AvatarGenerator.CreateSvg("John Michael Doe"); // first and last words
var compound = AvatarGenerator.CreateSvg("Jean-Pierre");       // connectors split the words
var apostrophe = AvatarGenerator.CreateSvg("O'Brien");
var singleWord = AvatarGenerator.CreateSvg("John");            // first two graphemes
var japanese = AvatarGenerator.CreateSvg("山田太郎");

Assert.Contains(">JD</text>", threeWords);
Assert.Contains(">JP</text>", compound);
Assert.Contains(">OB</text>", apostrophe);
Assert.Contains(">Jo</text>", singleWord);
Assert.Contains(">山田</text>", japanese);
```
<!-- endref -->

![A round avatar with the initials JD](images/InitialsJohnMichaelDoe.svg)
![A round avatar with the initials JP](images/InitialsJeanPierre.svg)
![A round avatar with the initials OB](images/InitialsOBrien.svg)
![A round avatar with the letters Jo](images/InitialsJohn.svg)
![A round avatar with the Japanese characters 山田](images/InitialsYamadaTaro.svg)

The case of the name is kept: `"john doe"` gives `jd`.

The unit is a grapheme, what a reader sees as one character, not a `char`: an emoji, a flag or a letter followed by a
combining accent counts as one. Characters that display nothing, such as zero-width characters, bidirectional controls
and combining marks without a base letter, are skipped. A name with nothing visible gives `?`.

## Choose the initials yourself

Set [Bigram](cref:Meziantou.Framework.AvatarOptions.Bigram) to use your own text, for instance initials stored in the user profile:

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_ExplicitBigram;format=md-fenced-code-block;dedent=true -->
```csharp
// The text comes from Bigram, the color still comes from the name
var svg = AvatarGenerator.CreateSvg("Ada Lovelace", new AvatarOptions { Bigram = "A" });
var fromName = AvatarGenerator.CreateSvg("Ada Lovelace");

Assert.Contains(">A</text>", svg);
Assert.Equal(GetBackground(fromName), GetBackground(svg));

static string GetBackground(string svg) => svg.Split("fill=\"")[1].Split('"')[0];
```
<!-- endref -->

![A round avatar with the letter A, in the same color as the avatar of Ada Lovelace](images/ExplicitBigram.svg)

- [Bigram](cref:Meziantou.Framework.AvatarOptions.Bigram) must contain one or two graphemes, without whitespace. Surrounding whitespace is trimmed. Another value
  throws an `ArgumentException`.
- [Bigram](cref:Meziantou.Framework.AvatarOptions.Bigram) does not change the color: the color always comes from the name. See [Stable colors](06-StableColors.md).
- By default, screen readers announce the bigram. See [Accessibility](05-Accessibility.md).
