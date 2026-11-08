---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **Matching is yours.** The library parses the query and combines your handlers. Wildcards, prefixes, stemming,
  ranking and fuzzy matching are up to the handlers.
- **Filtering only.** A query does not sort, page or select fields: `sort:date` is a key like any other, with the
  handler you give it.
- **One syntax.** The operators, keywords and date keywords are fixed, and in English.
- **No syntax errors to report.** A malformed query is read as text, so there is no error position to show the user;
  see [Malformed queries](03-QuerySyntax.md#malformed-queries).
- **Size limits.** Deeply nested queries, and queries whose `OR` groups expand too much, throw a
  [QueryTooComplexException](cref:Meziantou.Framework.SimpleQueryLanguage.QueryTooComplexException); see [Queries from users](04-InMemoryQueries.md#queries-from-users).
- **Dates written in a query are in UTC.** A date without an offset, such as `2026-03-18`, is read as UTC, even when the
  builder has a [time zone](06-ValuesAndDates.md#time-zone) for the date keywords.
