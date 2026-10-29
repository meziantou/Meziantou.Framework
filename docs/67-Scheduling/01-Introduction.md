---
title: Introduction
description: Recurrence rules, cron expressions and iCalendar events for .NET, with time zone support.
---

`Meziantou.Framework.Scheduling` computes when something happens: the occurrences of a schedule, from a start date, in
the time zone you choose. It supports the two common ways to describe a schedule, and the calendar format that carries
recurring events:

- **Recurrence rules** (`RRULE`), as defined by RFC 5545 (iCalendar) and RFC 2445: `FREQ=MONTHLY;BYDAY=-1FR` is the last
  Friday of every month. Calendar applications use them for recurring meetings.
- **Cron expressions**: `0 9 * * MON-FRI` is 09:00 every weekday. Job schedulers use them.
- **iCalendar** (`.ics`) files, which hold events, their recurrence rule and their time zone.

## What you can do with it

- **Enumerate the occurrences** of a recurrence rule, from the start of a series. See
  [Recurrence rules](03-RecurrenceRules.md).
- **Find the next run** of a cron expression, to schedule a job. See [Cron expressions](04-CronExpressions.md).
- **Follow the time zone of the user**, so a daily meeting stays at 09:00 across daylight saving time changes. See
  [Time zones](05-TimeZones.md).
- **Describe a recurrence rule in words**, in English or in French, such as "every month on the last Friday". See
  [Human-readable text](06-HumanReadableText.md).
- **Read and write iCalendar files**, to import events from a calendar application or to export your own. See
  [Reading iCalendar](07-ReadingICalendar.md) and [Writing iCalendar](08-WritingICalendar.md).

## Recurrence rule or cron expression?

| | Recurrence rule | Cron expression |
| --- | --- | --- |
| Typical use | Calendar events, user-facing schedules | Jobs and maintenance tasks |
| Start of the schedule | A start date (`DTSTART`) that defines the series: intervals and counts are computed from it | None: every matching date is an occurrence |
| Intervals | Any interval: every 3 weeks, every other month | Steps within a field: every 15 minutes, every 2 hours |
| End | A number of occurrences (`COUNT`) or an end date (`UNTIL`) | A year field, at most |
| Interoperability | iCalendar files, calendar applications | Unix cron and job schedulers |

Both types implement `IRecurrenceRule`, so the methods that compute occurrences, such as `GetNextOccurrence`, work with
either.

## Principles

- **Standards-compliant.** Recurrence rules follow RFC 5545 strictly: an unknown rule part or a combination the RFC
  forbids is an error, and the occurrences are the ones the RFC defines.
- **Deterministic.** Results depend on the start date and the time zone you pass, never on the clock or the time zone of
  the machine.
- **Lazy.** Occurrences are computed as you enumerate them, so a rule without an end is not a problem: take the ones you
  need.

## Namespace

Every type is in the `Meziantou.Framework.Scheduling` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and compute your first occurrences.
