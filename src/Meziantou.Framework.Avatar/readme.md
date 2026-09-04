# Meziantou.Framework.Avatar

Generate deterministic avatar SVG strings from a name.

## Usage

```c#
using Meziantou.Framework;

var svg = AvatarGenerator.CreateSvg("John Doe");
```

`AvatarGenerator` extracts a 1-2 grapheme bigram from the name and selects a background/foreground pair from the palette using a hash of the name.

## Customize the output

```c#
using Meziantou.Framework;

var options = new AvatarOptions
{
    Bigram = "JD", // optional explicit 1-2 grapheme bigram
    Shape = AvatarShape.Round, // Round, Square, or RoundedSquare
    Size = 128,
};
options.Palette.Clear();
options.Palette.Add(new AvatarColorPair("#CFDADE", "#153037")); // background, then foreground

var svg = AvatarGenerator.CreateSvg("John Doe", options);
```

## How the bigram is chosen

The name is split on whitespace and on the connectors used inside compound names (`-`, `'`, `.`, `_`, and their typographic variants):

- Two or more words: the first grapheme of the first word plus the first grapheme of the last word. `"John Michael Doe"` gives `JD`, not `JM`. `"Jean-Pierre"` gives `JP` and `"O'Brien"` gives `OB`.
- A single word: its first two graphemes. `"John"` gives `Jo` and `"山田太郎"` gives `山田`.

The unit is a grapheme cluster, not a `char`, so an emoji or a decomposed accent counts as one. Characters that render nothing (zero-width, bidi controls, standalone combining marks) are skipped, and a name with nothing visible renders `?`.

Set `AvatarOptions.Bigram` to bypass this entirely. It does not affect the color, which always derives from the name.

## Accessibility

By default the avatar is exposed as `role="img"` with the bigram as its `aria-label`. Two options change this:

- `AccessibleLabel` sets the announced text — pass the full name so it announces as "John Doe" rather than "JD".
- `IsDecorative` hides the avatar from assistive technologies with `aria-hidden`. Use it when the avatar sits next to the name it represents, so the name is not announced twice.

## Determinism

The color is `hash(name) % Palette.Count`, so the same name always maps to the same pair — **for a fixed palette and a fixed package version**. Two consequences are worth knowing before caching or persisting generated avatars:

- The number of entries in the palette and their order are part of the mapping. Adding or removing a single `AvatarColorPair` re-colors nearly every name, not just the names that mapped to the changed entry.
- The name is trimmed and normalized to Unicode Form C before hashing, so surrounding whitespace is ignored but **case is not**: `"john doe"` and `"John Doe"` get different colors.

Normalization requires ICU. Under `InvariantGlobalization` it is a no-op, so a name that is not already in Form C selects a different palette entry there than it does in a normal deployment. If you render avatars from both kinds of process and need them to agree, normalize the name yourself before calling `CreateSvg`.

## Input handling

The generated string is always well-formed XML. Characters that the XML 1.0 specification forbids — control characters, unpaired surrogates from a name truncated mid-emoji — are removed from `name` and from `AccessibleLabel`, so an untrusted display name can be passed straight through. An explicit `Bigram` containing such a character is rejected with an `ArgumentException` instead, since that indicates a bug in the calling code.
