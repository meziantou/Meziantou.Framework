---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **Matching is yours.** The library parses the query and combines your handlers. Wildcards, prefixes, stemming,
  ranking and fuzzy matching are up to the handlers.
- **Filtering only.** A query does not sort, page or select fields: `sort:date` is a key like any other, with the
  handler you give it.
- **One syntax.** The operators, keywords and date keywords are fixed, and in English.
- **Size limits.** Deeply nested queries, and queries whose `OR` groups expand too much, throw a
  [QueryTooComplexException](cref:Meziantou.Framework.SimpleQueryLanguage.QueryTooComplexException); see [Queries from users](04-InMemoryQueries.md#queries-from-users).
- **Dates in UTC.** Date keywords and relative dates use UTC days; there is no time zone option.
