---
title: Introduction
description: The ASP.NET Core packages of Meziantou.Framework, what each one does, and which one to install.
---

The `Meziantou.AspNetCore.*` packages add the pieces that most ASP.NET Core applications write again and again:
production defaults for a web application, a few middleware, HTTP Basic authentication, tag helpers for MVC and Razor
Pages, and Blazor components. Each package is independent: install only the ones you need.

## The packages

| Package | What it gives you | Documentation |
| --- | --- | --- |
| `Meziantou.AspNetCore.ServiceDefaults` | One call that configures OpenTelemetry, health checks, OpenAPI, HTTP client resilience, JSON conventions, HTTPS and other production defaults | [Service defaults](03-ServiceDefaults/01-Overview.md) |
| `Meziantou.AspNetCore.ServiceDefaults.AutoRegister` | A source generator that applies the service defaults to every `WebApplicationBuilder.Build()` call, so you don't write the two calls yourself | [Applying the defaults automatically](03-ServiceDefaults/05-AutoRegister.md) |
| `Meziantou.AspNetCore` | A middleware that marks responses as non-cacheable when they don't say otherwise, and a diagnostic view of the middleware pipeline | [Middleware](04-Middleware/01-Overview.md) |
| `Meziantou.AspNetCore.Authentication.HttpBasic` | An authentication handler for HTTP Basic, with your own credential check or with ASP.NET Core Identity | [HTTP Basic authentication](05-HttpBasicAuthentication/01-Overview.md) |
| `Meziantou.AspNetCore.Mvc` | Tag helpers for MVC and Razor Pages: conditional rendering, inlined CSS, scripts and images, deferred content, `datetime` attributes | [MVC tag helpers](06-MvcTagHelpers.md) |
| `Meziantou.AspNetCore.Components` | Blazor components (repeater, data grid, infinite scrolling, generated forms, inputs) and browser services (clipboard, query string, time zone) | [Blazor components](07-BlazorComponents/01-Overview.md) |
| `Meziantou.AspNetCore.Components.LogViewer` | A Blazor component that displays log entries with highlighting, ANSI colors, structured payloads and nested entries | [Log viewer](08-LogViewer/01-Overview.md) |
| `Meziantou.AspNetCore.Components.WebAssembly` | A message handler that sets the browser fetch options (cache, credentials, mode) of every `HttpClient` request in Blazor WebAssembly | [Browser fetch options](09-BrowserFetchOptions.md) |

## Which package do I need?

- **A new web API or web application** that should be ready for production: start with
  `Meziantou.AspNetCore.ServiceDefaults`. It already includes the no-cache middleware of `Meziantou.AspNetCore`. Add
  `Meziantou.AspNetCore.ServiceDefaults.AutoRegister` if you don't want to call the setup methods yourself.
- **Scripts, tools or devices that call your API** with a username and a password: `Meziantou.AspNetCore.Authentication.HttpBasic`.
- **Server-rendered pages** (MVC views or Razor Pages): `Meziantou.AspNetCore.Mvc`.
- **A Blazor application**: `Meziantou.AspNetCore.Components`, plus `Meziantou.AspNetCore.Components.LogViewer` to show
  logs, and `Meziantou.AspNetCore.Components.WebAssembly` for a WebAssembly client that calls an API.
- **Debugging the order of your middleware**: `Meziantou.AspNetCore`.

## Principles

- **Opt-in pieces.** Every component, middleware and service is added explicitly. The only exception is the
  auto-register generator, whose whole purpose is to add the service defaults for you.
- **Safe defaults.** The pipeline debug endpoint answers only in Development, HTTP Basic rejects accounts that need a
  second factor, `InputUrl` rejects `javascript:` URLs, and the log viewer renders only `http` and `https` links.
- **Your settings win.** The no-cache middleware never replaces a `Cache-Control` header you set, and the browser fetch
  handler never replaces an option set on a request.

## Next steps

- [Getting started](02-GettingStarted.md): install a package and see each one at work in a few lines.
