---
title: Diagnostics
description: See the recent queries and the configuration on the diagnostics page, pause the filtering, and read the logs and traces.
---

## The diagnostics page

Open `http://localhost:5080/` (the `HttpPort` setting) in a browser. The page shows:

- the configuration: ports, upstreams, filter lists, custom records, cache and limits, and the number of loaded filter
  rules;
- whether filtering is paused;
- the most recent queries, newest first: time (UTC), client, transport, question, result (see
  [What happens to a query](01-Overview.md#what-happens-to-a-query)), upstream (or `Cache`), latency, response code and
  answers.

The page shows the 200 most recent queries; add `?limit=500` to the URL to see more. The proxy keeps
`DiagnosticsHistoryCapacity` queries (10000 by default), in memory only: they are lost when it stops.

## Pause the filtering

The page has a button that disables filtering for 15 minutes, to check whether a block list breaks a site. During the
pause, queries skip the filter lists; custom records still apply. Pressing the button again restarts the 15 minutes.
Filtering comes back on by itself, or when the proxy restarts.

## Security of the page

- The page is served on the loopback address only, so it is only reachable from the machine itself.
- The pause button is a form with an antiforgery token, so another web page cannot disable the filtering.
- Set `AllowedHosts` to `localhost` in the configuration (as the `appsettings.json` of the project does) to reject
  requests with another `Host` header, which blocks DNS rebinding attacks. The installed tool does not set it by
  default. See [Change the settings](02-RunningTheProxy.md#change-the-settings).

## Logs

The proxy logs to the console, with the logging configuration of ASP.NET Core (the `Logging` section). Warnings to
look for:

- `Cannot load filter list`: a download failed; the cached copy is used.
- The number of skipped lines of a list, with examples.
- `An upstream query failed unexpectedly`: an upstream timed out or failed; the next one was tried.
- `Skipping invalid allowed client network`, and the warnings about invalid custom records.
- The fallback of a DNS over QUIC upstream to DNS over HTTPS.

## Traces

The proxy emits activities you can collect with OpenTelemetry:

- `Meziantou.DnsProxy`: `dns_proxy.filters.refresh` for each refresh of the filter lists, with the number of lists
  loaded and failed and the number of rules, and `dns_proxy.filters.load` for each list.
- `Meziantou.Framework.DnsClient`: `dns.query` for each upstream query. See
  [Tracing](../03-DnsClient/05-ErrorsAndTimeouts.md#tracing).
