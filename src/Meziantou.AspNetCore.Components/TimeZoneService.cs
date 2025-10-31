using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components;

/// <summary>
/// Provides timezone information and conversion services based on the user's browser timezone.
/// </summary>
/// <remarks>
/// <para>
/// This service retrieves the user's timezone offset from the browser and provides methods to convert between UTC and local times.
/// To use this service, register it in your dependency injection container using <see cref="TimeZoneServiceExtensions.AddTimeZoneServices"/>.
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
    private Task<IJSObjectReference>? _module;
    private TimeSpan? _userOffset;

    private readonly CancellationTokenSource _cts = new();

    private Task<IJSObjectReference> Module => _module ??= _jsRuntime.InvokeAsync<IJSObjectReference>("import", _cts.Token, ImportPath).AsTask();

    /// <summary>Initializes a new instance of the <see cref="TimeZoneService"/> class.</summary>
    /// <param name="jsRuntime">The JavaScript runtime to use for interoperability.</param>
    public TimeZoneService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>Gets the user's timezone offset from UTC.</summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the timezone offset.</returns>
    public async ValueTask<TimeSpan> GetOffsetAsync()
    {
        if (_userOffset == null)
        {
            var module = await Module;
            var offsetInMinutes = await module.InvokeAsync<int>("blazorGetTimezoneOffset", _cts.Token);
            _userOffset = TimeSpan.FromMinutes(-offsetInMinutes);
        }

        return _userOffset.GetValueOrDefault();
    }

    /// <summary>Converts a <see cref="DateTimeOffset"/> to the user's local timezone.</summary>
    /// <param name="dateTime">The date and time to convert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the converted date and time.</returns>
    public async ValueTask<DateTimeOffset> GetLocalDateTimeAsync(DateTimeOffset dateTime)
    {
        var offset = await GetOffsetAsync();
        return dateTime.ToOffset(offset);
    }

    /// <summary>Converts a <see cref="DateTime"/> from the user's local timezone to UTC.</summary>
    /// <param name="dateTime">The local date and time to convert. If the value is already UTC, it is returned as-is.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the UTC date and time.</returns>
    public async ValueTask<DateTimeOffset> GetUtcDateTimeAsync(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(dateTime, TimeSpan.Zero);

        var offset = await GetOffsetAsync();
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified).Add(-offset), TimeSpan.Zero);
    }

    /// <summary>Disposes the service and releases all resources.</summary>
    /// <returns>A task that represents the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_module is not null)
        {
            try
            {
                var module = await _module;
                await module.DisposeAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}
