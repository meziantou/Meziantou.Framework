using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components;

public sealed class TimeZoneService : IAsyncDisposable
{
    private const string ImportPath = "./_content/Meziantou.AspNetCore.Components/Timezone.js";

    private readonly IJSRuntime _jsRuntime;
    private Task<IJSObjectReference>? _module;
    private TimeSpan? _userOffset;

    private readonly CancellationTokenSource _cts = new();

    private Task<IJSObjectReference> Module => _module ??= _jsRuntime.InvokeAsync<IJSObjectReference>("import", _cts.Token, ImportPath).AsTask();

    public TimeZoneService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

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

    public async ValueTask<DateTimeOffset> GetLocalDateTimeAsync(DateTimeOffset dateTime)
    {
        var offset = await GetOffsetAsync();
        return dateTime.ToOffset(offset);
    }

    public async ValueTask<DateTimeOffset> GetUtcDateTimeAsync(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(dateTime, TimeSpan.Zero);

        var offset = await GetOffsetAsync();
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified).Add(-offset), TimeSpan.Zero);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_module != null)
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
