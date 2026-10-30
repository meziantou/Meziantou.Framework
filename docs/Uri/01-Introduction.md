---
title: Introduction
description: Match URLs with the URL Pattern syntax of the web platform, edit query strings, and look up the Public Suffix List.
---

`Meziantou.Framework.Uri` gathers helpers to work with URLs in .NET:

- **URL patterns**: an implementation of the [WHATWG URL Pattern](https://urlpattern.spec.whatwg.org/) standard, the
  pattern syntax browsers use (`https://*.example.com/books/:id`). Test whether a URL matches a pattern and extract the
  values of its groups.
- **Query strings**: parse a query string into names and values, and add, replace or remove parameters of a URL.
- **Public Suffix List**: find the public suffix (`co.uk`) and the registrable domain (`example.co.uk`) of a host
  name, from a snapshot of the [Public Suffix List](https://publicsuffix.org/list/) compiled into the assembly.

## What you can do with it

- **Route or filter URLs** with patterns that read like URLs, instead of regular expressions. See
  [Matching URLs](03-MatchingUrls.md) and [Pattern syntax](04-PatternSyntax.md).
- **Find which pattern of a list matches** a URL, such as the route of a request. See
  [Pattern collections](05-PatternCollections.md).
- **Build URLs** by adding, replacing and removing query string parameters, with the values encoded for you. See
  [Query strings](06-QueryStrings.md).
- **Decide whether two hosts belong to the same site**, or which part of a host name a registrant controls, for
  cookies, security checks or grouping URLs by site. See [Public Suffix List](07-PublicSuffixList.md).

## Namespace

Every type is in the [Meziantou.Framework](cref:Meziantou.Framework.Uri!N:Meziantou.Framework) namespace: [UrlPattern](cref:Meziantou.Framework.UrlPattern), [UrlPatternInit](cref:Meziantou.Framework.UrlPatternInit), [UrlPatternOptions](cref:Meziantou.Framework.UrlPatternOptions),
[UrlPatternResult](cref:Meziantou.Framework.UrlPatternResult), [UrlPatternCollection](cref:Meziantou.Framework.UrlPatternCollection), [QueryStringUtilities](cref:Meziantou.Framework.QueryStringUtilities), [QueryStringParameterCollection](cref:Meziantou.Framework.QueryStringParameterCollection),
[PublicSuffixList](cref:Meziantou.Framework.PublicSuffixList) and [DomainInfo](cref:Meziantou.Framework.DomainInfo).

## Next steps

- [Getting started](02-GettingStarted.md): install the package and try each feature.
- [Limitations](08-Limitations.md): what the library does not do.
