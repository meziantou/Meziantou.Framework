---
title: Limitations
description: What the URL patterns, the query string helpers and the Public Suffix List do not do.
---

## URL patterns

- **Regular expressions are .NET regular expressions.** The URL Pattern standard defines the regular expression groups
  with the JavaScript syntax. This library runs them with the .NET engine, so the syntax that differs between the two
  does not behave the way it does in a browser; for instance, the set operations of the JavaScript `v` flag
  (`[\d&&[0-1]]`) are not supported.
- **Regular expressions have no timeout.** A pattern with a regular expression that backtracks a lot can take a long
  time to match some URLs. Do not create patterns from text that users supply, or check that it has no regular
  expression group (`HasRegExpGroups`).
- **No URL type.** The library parses URLs to match them, but returns no parsed URL: use `System.Uri` for that. The
  values in a match result are in the normalized form of the URL standard, which can differ from what `System.Uri`
  returns for the same URL, for instance on percent-encoding.
- **No pattern generation.** A pattern cannot build a URL from the values of its groups.

## Query strings

- **Strings in, strings out.** The helpers work on URL strings and do not validate them: they split the string at the
  first `?` and the first `#`.
- **Names are case-sensitive**, and parameters are written back in the `name=value` form, so a parameter written
  without `=` (`?debug`) gains one (`?debug=`) when the query string is written again.

## Public Suffix List

- **A snapshot.** The list is the one of the package version you use: a suffix added to the list after that version is
  unknown until you update the package. See [Version of the list](07-PublicSuffixList.md#version-of-the-list).
- **Domain names only.** IP addresses are not domain names: `TryGetDomainInfo` returns `false` for a `Uri` whose host
  is one.

## Internationalized host names and invariant globalization

The host names that contain non-ASCII characters are converted to their ASCII form with `IdnMapping`, in URL patterns
and in the Public Suffix List. `IdnMapping` relies on ICU: in an application that runs with
`InvariantGlobalization` enabled, the conversion does not apply the whole mapping of the standard, so some non-ASCII
host names are converted differently from a browser. ASCII host names are not affected.
