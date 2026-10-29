---
title: Human-readable text
description: Describe a recurrence rule in English or in French, and know what the description leaves out.
---

## Describe a rule

`GetHumanText` describes a recurrence rule in words, to show it to a user:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_GetHumanTextEnglish;format=md-fenced-code-block;dedent=true -->
```csharp
var culture = CultureInfo.GetCultureInfo("en-US");
Assert.Equal("every week on Monday, Tuesday, Wednesday, Thursday and Friday", RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR").GetHumanText(culture));
Assert.Equal("every month on the last Friday for 5 times", RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR;COUNT=5").GetHumanText(culture));
Assert.Equal("every year on February the 29th", RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29").GetHumanText(culture));
```
<!-- endref -->

The supported languages are English and French. The language comes from the name of the culture you pass, or of the
current UI culture when you pass none:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_GetHumanTextFrench;format=md-fenced-code-block;dedent=true -->
```csharp
var culture = CultureInfo.GetCultureInfo("fr-FR");
Assert.Equal("tous les jours", RecurrenceRule.Parse("FREQ=DAILY").GetHumanText(culture));
Assert.Equal("tous les mois le dernier vendredi pour 5 fois", RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR;COUNT=5").GetHumanText(culture));

// A language that is not supported gives null
Assert.Null(RecurrenceRule.Parse("FREQ=DAILY").GetHumanText(CultureInfo.GetCultureInfo("de-DE")));
```
<!-- endref -->

| Culture | Language |
| --- | --- |
| `en`, `en-*` and the invariant culture | English |
| `fr`, `fr-*`, such as `fr-CA` | French |
| Any other culture | Not supported: `GetHumanText` returns `null` |

The language is read from the culture name, so the result is the same when the application runs in invariant
globalization mode. `GetHumanText` also returns `null` for a class that derives from `RecurrenceRule` outside of the
library.

English always writes dates as month, day and year, and times with a 24-hour clock, whatever the region of the culture,
`en-GB` included. A UTC `UNTIL` is shown in UTC, such as "until January 1, 2025 at 10:30 UTC".

## What the text describes

The text describes the rule parts as they are written, without a start date:

- It does not tell how `INTERVAL` aligns with the start date. `FREQ=HOURLY;INTERVAL=2;BYHOUR=9,10,11` reads "every other
  hour at hours 9, 10 and 11", but from 08:15 it only gives 10:15.
- It does not mention the components that come from the start date. `FREQ=DAILY;BYMINUTE=5` reads "every day at minute
  5", and from 08:15 it gives 08:05 every day.
- `BYSETPOS` selects among the occurrences of each period: `FREQ=WEEKLY;BYDAY=MO,FR;BYSETPOS=1;COUNT=3` reads "every week
  on Monday and Friday, only the first occurrence of each week, for 3 times".
- `WKST` is mentioned when it changes the occurrences for some start date: with `BYWEEKNO`, and in a `WEEKLY` rule with
  `BYDAY` when `INTERVAL` is greater than 1 or when `BYSETPOS` groups the days differently.
  `FREQ=WEEKLY;INTERVAL=2;BYDAY=SU;WKST=SU` reads "every other week on Sunday, with weeks starting on Sunday".
- A rule with `COUNT=0` has no occurrence and reads "never".

When the start date matters to the user, show it next to the text, such as "every month on the last Friday, from
January 26, 2024".
