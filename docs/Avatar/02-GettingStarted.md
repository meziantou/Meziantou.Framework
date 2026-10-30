---
title: Getting started
description: Install the package, generate an avatar from a name and use it in a web page or a file.
---

## Install the package

Add the `Meziantou.Framework.Avatar` package to a project:

```bash
dotnet add package Meziantou.Framework.Avatar
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Generate an avatar

`AvatarGenerator.CreateSvg` returns the SVG of the avatar of a name, as a string:

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_CreateSvg;format=md-fenced-code-block;dedent=true -->
```csharp
var svg = AvatarGenerator.CreateSvg("John Doe");
Assert.StartsWith("<svg ", svg);
Assert.Contains("aria-label=\"JD\"", svg);
```
<!-- endref -->

![A round, light gray avatar with the initials JD](images/JohnDoe.svg)

By default, the avatar is a circle of 64 by 64 pixels. The initials come from the first and last words of the name, and
the colors are picked from a palette of 11 color pairs using a hash of the name. The `aria-label` attribute gives the
text that screen readers announce.

`CreateSvg` throws an `ArgumentException` when the name is `null`, empty or only whitespace.

## Use the avatar

The SVG string can be used in several ways:

- **Inline in an HTML page**: write the string into the page. The `svg` element is valid HTML.
- **As a file**: save the string with `File.WriteAllText("avatar.svg", svg)`, or serve it from an endpoint with the
  `image/svg+xml` content type. Avatars are small and deterministic, so they cache well; see
  [Stable colors](06-StableColors.md) before caching them for a long time.
- **As a data URI**, in an `img` element or in CSS: encode the string, for instance with
  `"data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))`.

## Names typed by users

You can pass a display name typed by a user as it is. The markup characters are escaped, and the characters that XML
forbids, such as control characters or an unpaired surrogate left by a name truncated in the middle of an emoji, are
removed. The result is always well-formed XML:

<!-- ref:../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs;region=AvatarGenerator_UntrustedName;format=md-fenced-code-block;dedent=true -->
```csharp
// A display name typed by a user, with markup and a control character
var name = "<Ada> & \u0007Lovelace";
var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions { AccessibleLabel = name });

// The result is always well-formed XML: the markup is escaped and the control character is removed
var document = new XmlDocument();
document.LoadXml(svg);
Assert.Equal("<Ada> & Lovelace", document.DocumentElement!.GetAttribute("aria-label"));
```
<!-- endref -->

The same applies to `AvatarOptions.AccessibleLabel`. An explicit `AvatarOptions.Bigram` that contains a character
forbidden by XML is rejected with an `ArgumentException` instead, because it comes from your code, not from a user.

## Next steps

- [Appearance](03-Appearance.md): the shape, the size and the colors.
- [Initials](04-Initials.md): how the initials are chosen, and how to choose them yourself.
