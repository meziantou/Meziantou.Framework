---
title: Accessibility
description: Announce the full name to screen readers, or hide a decorative avatar.
---

By default, an avatar is exposed to assistive technologies as an image (`role="img"`) whose label is the bigram: a screen
reader announces "JD". Two options change this:

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_Accessibility;format=md-fenced-code-block;dedent=true -->
```csharp
// By default, the avatar is an image whose label is the bigram
var byDefault = AvatarGenerator.CreateSvg("Ada Lovelace");
Assert.Contains("role=\"img\" aria-label=\"AL\"", byDefault);

// Announce the full name instead
var labeled = AvatarGenerator.CreateSvg("Ada Lovelace", new AvatarOptions { AccessibleLabel = "Ada Lovelace" });
Assert.Contains("role=\"img\" aria-label=\"Ada Lovelace\"", labeled);

// Or hide it when the name is already displayed next to it
var decorative = AvatarGenerator.CreateSvg("Ada Lovelace", new AvatarOptions { IsDecorative = true });
Assert.Contains("aria-hidden=\"true\"", decorative);
Assert.DoesNotContain("aria-label", decorative);
```
<!-- endref -->

## Announce the full name

Set `AccessibleLabel` to the text to announce, usually the full name, so the avatar is announced as "Ada Lovelace"
rather than "AL". Use it when the avatar is shown alone, such as in a list of participants or on a profile button.

The characters that XML forbids are removed from `AccessibleLabel`, like from the name, so you can pass a name typed
by a user.

## Hide a decorative avatar

Set `IsDecorative` to `true` when the name is displayed next to the avatar, such as in a comment header: the name would
otherwise be announced twice. The SVG then has `aria-hidden="true"` instead of a role and a label, and
`AccessibleLabel` is ignored.
