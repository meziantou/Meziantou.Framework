---
title: Introduction
description: What RelativeDate does, the languages it supports, and when the text is computed.
---

`Meziantou.Framework.RelativeDate` writes a date relative to now, in words: `5 minutes ago`, `yesterday`, `in 3 days`.
It is the kind of text you show next to a comment, a file, or a notification, where the exact time matters less than how
long ago it was.

## What you can do with it

- **Format a `DateTime` or a `DateTimeOffset`** in the past or in the future. See
  [Getting started](02-GettingStarted.md).
- **Know which text a date gives**: seconds, minutes, hours, days, months, years. See
  [Texts and thresholds](03-TextsAndThresholds.md).
- **Write the text in another language**: Dutch, English, French, German, Italian, Japanese, Korean, Portuguese,
  Simplified Chinese, Spanish and Turkish are included, and you can replace any text. See
  [Languages and custom texts](04-LanguagesAndCustomTexts.md).
- **Test your code** with a fixed clock, by passing a `TimeProvider`. See
  [Getting started](02-GettingStarted.md#use-a-time-provider).

## When the text is computed

`RelativeDate.Get` stores the date, not the text. The text is computed each time you call `ToString`, from the current
time of the time provider, so a `RelativeDate` that you keep and display again later gives an up-to-date text.

## Namespaces

`RelativeDate`, `ILocalizationProvider` and `LocalizationProvider` are in the `Meziantou.Framework` namespace.
