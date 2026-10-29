---
title: User time zone
description: Get the time zone and the UTC offset of the user's browser, and convert dates between UTC and the user's local time.
---

With Blazor Server, the .NET code runs on the server, so `TimeZoneInfo.Local` is the time zone of the server, not the
user's. `TimeZoneService` asks the browser. Register it with `builder.Services.AddTimeZoneServices()`, then inject it:

<!-- ref:../../../samples/BlazorAppSample/Examples/TimeZoneServiceExample.razor;format=md-fenced-code-block -->
```razor
@inject TimeZoneService TimeZoneService

<button @onclick="ConvertToLocalTime">Show the local time</button>
<p>@localTime</p>

@code {
    private DateTimeOffset? localTime;

    private async Task ConvertToLocalTime()
    {
        var utcTime = DateTimeOffset.UtcNow;
        localTime = await TimeZoneService.GetLocalDateTimeAsync(utcTime);
    }
}
```
<!-- endref -->

| Method | Returns |
| --- | --- |
| `GetLocalDateTimeAsync(DateTimeOffset)` | The same instant, in the user's offset at that instant |
| `GetUtcDateTimeAsync(DateTime)` | The UTC instant of a local time of the user. A `DateTime` whose `Kind` is `Utc` is returned unchanged |
| `GetOffsetAsync()` | The user's offset from UTC now |
| `GetOffsetAsync(DateTimeOffset)` | The user's offset from UTC at the given instant |
| `GetTimeZoneAsync()` | The user's `TimeZoneInfo`, or `null` when the runtime does not know the identifier reported by the browser |

## Daylight saving time

The offset of a time zone changes during the year. Each conversion uses the offset at the instant being converted,
not the current offset: converting a date in July and a date in January can give two different offsets.

`GetUtcDateTimeAsync` does not throw for a local time that does not exist (skipped when the clocks move forward) or
that exists twice (when they move back): it picks one offset.

## How the time zone is found

The service reads the IANA identifier of the browser time zone (such as `Europe/Paris`) once per scope and looks it up
with `TimeZoneInfo.TryFindSystemTimeZoneById`. When the runtime does not know it, for instance without time zone data,
`GetTimeZoneAsync` returns `null` and the offsets are asked to the browser for each instant instead.

The service calls JavaScript: call it from `OnAfterRenderAsync` or from an event handler, not during prerendering. To
let the user type a date in their own time zone, pass the offset to [`InputDateTime`](06-InputComponents.md#dates-with-a-time).
