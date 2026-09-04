using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components;

/// <summary>Provides timezone information and conversion services based on the user's browser timezone.</summary>
/// <remarks>
/// <para>
/// This service retrieves the user's timezone from the browser and provides methods to convert between UTC and local times.
/// To use this service, register it in your dependency injection container using <see cref="TimeZoneServiceExtensions.AddTimeZoneServices"/>.
/// </para>
/// <para>
/// The offset of a timezone is not a constant: it changes at daylight saving transitions. Conversions therefore take
/// the instant into account rather than applying a single cached offset to every value.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // In Program.cs or Startup.cs
/// builder.Services.AddTimeZoneServices();
///
/// // In a component
/// @inject TimeZoneService TimeZoneService
///
/// @code {
///     private async Task ConvertToLocalTime()
///     {
///         var utcTime = DateTimeOffset.UtcNow;
///         var localTime = await TimeZoneService.GetLocalDateTimeAsync(utcTime);
///     }
/// }
/// </code>
/// </example>
public sealed class TimeZoneService : IAsyncDisposable
{
    private const string ImportPath = "./_content/Meziantou.AspNetCore.Components/Timezone.js";

    private readonly IJSRuntime _jsRuntime;
    private readonly CancellationTokenSource _cts = new();

    private Task<IJSObjectReference>? _module;
    private TimeZoneInfo? _timeZone;
    private bool _timeZoneResolved;
    private bool _disposed;

    private Task<IJSObjectReference> Module => _module ??= _jsRuntime.InvokeAsync<IJSObjectReference>("import", _cts.Token, ImportPath).AsTask();

    /// <summary>Initializes a new instance of the <see cref="TimeZoneService"/> class.</summary>
    /// <param name="jsRuntime">The JavaScript runtime to use for interoperability.</param>
    public TimeZoneService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>Gets the user's offset from UTC at the current instant.</summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the timezone offset.</returns>
    public ValueTask<TimeSpan> GetOffsetAsync() => GetOffsetAsync(DateTimeOffset.UtcNow);

    /// <summary>Gets the user's offset from UTC at the given instant.</summary>
    /// <param name="instant">The instant at which the offset is evaluated. Daylight saving time makes the offset depend on it.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the timezone offset.</returns>
    public async ValueTask<TimeSpan> GetOffsetAsync(DateTimeOffset instant)
    {
        var timeZone = await GetTimeZoneAsync();
        if (timeZone is not null)
            return timeZone.GetUtcOffset(instant);

        // The browser did not report a timezone identifier the runtime knows about, which happens when globalization
        // data is unavailable. Fall back to asking the browser for the offset at that specific instant.
        var module = await Module;
        var offsetInMinutes = await module.InvokeAsync<int>("blazorGetTimezoneOffset", _cts.Token, instant.ToUnixTimeMilliseconds());
        return TimeSpan.FromMinutes(-offsetInMinutes);
    }

    /// <summary>Gets the user's timezone, or <see langword="null"/> when the browser reports an identifier this runtime does not know.</summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the user's timezone.</returns>
    public async ValueTask<TimeZoneInfo?> GetTimeZoneAsync()
    {
        if (_timeZoneResolved)
            return _timeZone;

        var module = await Module;
        var id = await module.InvokeAsync<string?>("blazorGetTimezone", _cts.Token);
        if (id is not null)
        {
            // The identifier is stable for the lifetime of the scope, unlike an offset, so it is safe to cache
            TimeZoneInfo.TryFindSystemTimeZoneById(id, out _timeZone);
        }

        _timeZoneResolved = true;
        return _timeZone;
    }

    /// <summary>Converts a <see cref="DateTimeOffset"/> to the user's local timezone.</summary>
    /// <param name="dateTime">The date and time to convert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the converted date and time.</returns>
    public async ValueTask<DateTimeOffset> GetLocalDateTimeAsync(DateTimeOffset dateTime)
    {
        var offset = await GetOffsetAsync(dateTime);
        return dateTime.ToOffset(offset);
    }

    /// <summary>Converts a <see cref="DateTime"/> from the user's local timezone to UTC.</summary>
    /// <param name="dateTime">The local date and time to convert. If the value is already UTC, it is returned as-is.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the UTC date and time.</returns>
    public async ValueTask<DateTimeOffset> GetUtcDateTimeAsync(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(dateTime, TimeSpan.Zero);

        var unspecified = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);

        var timeZone = await GetTimeZoneAsync();
        if (timeZone is not null)
        {
            // GetUtcOffset resolves ambiguous and skipped local times deterministically instead of throwing,
            // which ConvertTimeToUtc does for a local time that daylight saving time skipped over.
            var localOffset = timeZone.GetUtcOffset(unspecified);
            return new DateTimeOffset(unspecified, localOffset).ToUniversalTime();
        }

        // Without a known timezone the offset has to be evaluated at an instant. The local time is a good enough
        // approximation of it: the two only disagree within a few hours of a daylight saving transition.
        var offset = await GetOffsetAsync(new DateTimeOffset(unspecified, TimeSpan.Zero));
        return new DateTimeOffset(unspecified.Add(-offset), TimeSpan.Zero);
    }

    /// <summary>Disposes the service and releases all resources.</summary>
    /// <returns>A task that represents the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _cts.CancelAsync();
        if (_module is not null)
        {
            try
            {
                var module = await _module;
                await module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        _cts.Dispose();
    }
}
