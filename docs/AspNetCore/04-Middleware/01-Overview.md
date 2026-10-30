---
title: Overview
description: The middleware and diagnostics of the Meziantou.AspNetCore package.
---

`Meziantou.AspNetCore` has two independent features:

- [Default no-cache header](02-NoCache.md): [UseNoCache](cref:Meziantou.AspNetCore.NoCacheApplicationBuilderExtensions.UseNoCache) marks every response that does not set `Cache-Control` as
  non-cacheable, so browsers and proxies never keep a stale copy of a page or an API response.
- [Middleware pipeline debugging](03-PipelineDebugging.md): see the middleware of the application, in order, with their
  branches, and the list of endpoints, from code or from a debug endpoint.

```bash
dotnet add package Meziantou.AspNetCore
```

[UseNoCache](cref:Meziantou.AspNetCore.NoCacheApplicationBuilderExtensions.UseNoCache) is in the [Meziantou.AspNetCore](cref:Meziantou.AspNetCore) namespace; the debugging types are in [Meziantou.AspNetCore.Diagnostics](cref:Meziantou.AspNetCore.Diagnostics).

The [service defaults](../03-ServiceDefaults/01-Overview.md) already add the no-cache middleware: you don't need to call
[UseNoCache](cref:Meziantou.AspNetCore.NoCacheApplicationBuilderExtensions.UseNoCache) in an application that uses them.
