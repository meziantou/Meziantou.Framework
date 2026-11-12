using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The callback of a position (https://w3c.github.io/geolocation/#dom-positioncallback).</summary>
/// <seealso href="https://w3c.github.io/geolocation/#dom-positioncallback"/>
[IdlInterface("PositionCallback", "https://w3c.github.io/geolocation/#dom-positioncallback")]
public delegate void PositionCallback(GeolocationPosition position);

/// <summary>The callback of an error of a request of a position (https://w3c.github.io/geolocation/#dom-positionerrorcallback).</summary>
/// <seealso href="https://w3c.github.io/geolocation/#dom-positionerrorcallback"/>
[IdlInterface("PositionErrorCallback", "https://w3c.github.io/geolocation/#dom-positionerrorcallback")]
public delegate void PositionErrorCallback(GeolocationPositionError positionError);

/// <summary>The options of a request of a position (https://w3c.github.io/geolocation/#position_options_interface).</summary>
/// <seealso href="https://w3c.github.io/geolocation/#dom-positionoptions"/>
[IdlInterface("PositionOptions", "https://w3c.github.io/geolocation/#dom-positionoptions")]
public sealed class PositionOptions
{
    /// <summary>Gets whether the document asks for the most accurate position.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-positionoptions-enablehighaccuracy"/>
    [IdlMember("enableHighAccuracy", "https://w3c.github.io/geolocation/#dom-positionoptions-enablehighaccuracy")]
    public bool EnableHighAccuracy { get; init; }

    /// <summary>Gets the maximum time to acquire the position, in milliseconds (no limit by default).</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-positionoptions-timeout"/>
    [IdlMember("timeout", "https://w3c.github.io/geolocation/#dom-positionoptions-timeout")]
    public uint Timeout { get; init; } = uint.MaxValue;

    /// <summary>Gets the maximum age of a cached position that can be returned, in milliseconds (0 by default: a new position is acquired).</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-positionoptions-maximumage"/>
    [IdlMember("maximumAge", "https://w3c.github.io/geolocation/#dom-positionoptions-maximumage")]
    public uint MaximumAge { get; init; }
}

/// <summary>A position of the device and the time at which it was acquired (https://w3c.github.io/geolocation/#position_interface).</summary>
/// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationposition"/>
[IdlInterface("GeolocationPosition", "https://w3c.github.io/geolocation/#dom-geolocationposition")]
public sealed class GeolocationPosition
{
    internal GeolocationPosition(GeolocationCoordinates coords, ulong timestamp, bool isHighAccuracy)
    {
        Coords = coords;
        Timestamp = timestamp;
        IsHighAccuracy = isHighAccuracy;
    }

    /// <summary>Gets the coordinates of the position.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationposition-coords"/>
    [IdlMember("coords", "https://w3c.github.io/geolocation/#dom-geolocationposition-coords")]
    public GeolocationCoordinates Coords { get; }

    /// <summary>Gets the time at which the position was requested, in milliseconds since the Unix epoch.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationposition-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/geolocation/#dom-geolocationposition-timestamp")]
    public ulong Timestamp { get; }

    /// <summary>https://w3c.github.io/geolocation/#dfn-ishighaccuracy</summary>
    internal bool IsHighAccuracy { get; }
}

/// <summary>The coordinates of a position (https://w3c.github.io/geolocation/#coordinates_interface).</summary>
/// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates"/>
[IdlInterface("GeolocationCoordinates", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates")]
public sealed class GeolocationCoordinates
{
    internal GeolocationCoordinates(GeolocationReading reading)
    {
        Accuracy = reading.Accuracy;
        Latitude = reading.Latitude;
        Longitude = reading.Longitude;
        Altitude = reading.Altitude;
        AltitudeAccuracy = reading.Altitude is null ? null : reading.AltitudeAccuracy;
        Speed = reading.Speed;
        Heading = reading.Speed is 0 ? null : reading.Heading;
    }

    /// <summary>Gets the accuracy of the latitude and the longitude, in meters.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-accuracy"/>
    [IdlMember("accuracy", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-accuracy")]
    public double Accuracy { get; }

    /// <summary>Gets the latitude, in decimal degrees.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-latitude"/>
    [IdlMember("latitude", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-latitude")]
    public double Latitude { get; }

    /// <summary>Gets the longitude, in decimal degrees.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-longitude"/>
    [IdlMember("longitude", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-longitude")]
    public double Longitude { get; }

    /// <summary>Gets the height above the WGS84 ellipsoid, in meters, or null.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-altitude"/>
    [IdlMember("altitude", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-altitude")]
    public double? Altitude { get; }

    /// <summary>Gets the accuracy of the altitude, in meters, or null.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-altitudeaccuracy"/>
    [IdlMember("altitudeAccuracy", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-altitudeaccuracy")]
    public double? AltitudeAccuracy { get; }

    /// <summary>Gets the direction of travel, in degrees clockwise from the true north, or null (when the device is stationary, its speed is 0).</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-heading"/>
    [IdlMember("heading", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-heading")]
    public double? Heading { get; }

    /// <summary>Gets the horizontal speed, in meters per second, or null.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationcoordinates-speed"/>
    [IdlMember("speed", "https://w3c.github.io/geolocation/#dom-geolocationcoordinates-speed")]
    public double? Speed { get; }
}

/// <summary>The error of a request of a position (https://w3c.github.io/geolocation/#position_error_interface).</summary>
/// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationpositionerror"/>
[IdlInterface("GeolocationPositionError", "https://w3c.github.io/geolocation/#dom-geolocationpositionerror")]
public sealed class GeolocationPositionError
{
    /// <summary>The document is not allowed to read the position.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationpositionerror-permission_denied"/>
    [IdlMember("PERMISSION_DENIED", "https://w3c.github.io/geolocation/#dom-geolocationpositionerror-permission_denied")]
    public const ushort PermissionDenied = 1;

    /// <summary>The position cannot be acquired.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationpositionerror-position_unavailable"/>
    [IdlMember("POSITION_UNAVAILABLE", "https://w3c.github.io/geolocation/#dom-geolocationpositionerror-position_unavailable")]
    public const ushort PositionUnavailable = 2;

    /// <summary>The position was not acquired before the timeout elapsed.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationpositionerror-timeout"/>
    [IdlMember("TIMEOUT", "https://w3c.github.io/geolocation/#dom-geolocationpositionerror-timeout")]
    public const ushort Timeout = 3;

    internal GeolocationPositionError(ushort code)
    {
        Code = code;
        Message = code switch
        {
            PermissionDenied => "User denied Geolocation",
            Timeout => "Timeout expired",
            _ => "Position unavailable",
        };
    }

    /// <summary>Gets the code of the error.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationpositionerror-code"/>
    [IdlMember("code", "https://w3c.github.io/geolocation/#dom-geolocationpositionerror-code")]
    public ushort Code { get; }

    /// <summary>Gets a description of the error, for the developers.</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocationpositionerror-message"/>
    [IdlMember("message", "https://w3c.github.io/geolocation/#dom-geolocationpositionerror-message")]
    public string Message { get; }
}

/// <summary>
/// The Geolocation API of a window (https://w3c.github.io/geolocation/#geolocation_interface, <c>navigator.geolocation</c>):
/// the positions of the device, read from the <see cref="GeolocationProvider"/> of the browsing context
/// (<see cref="BrowsingContextOptions.Geolocation"/>).
/// </summary>
/// <remarks>
/// The positions are only given to the documents of a secure context that are allowed to use the <c>geolocation</c>
/// feature by their permissions policy and whose <c>geolocation</c> permission is granted (see
/// <see cref="BrowsingContextOptions.RequestPermission"/>). The requests of a hidden document wait until it is visible.
/// </remarks>
/// <seealso href="https://w3c.github.io/geolocation/#dom-geolocation"/>
[IdlInterface("Geolocation", "https://w3c.github.io/geolocation/#dom-geolocation")]
public sealed class Geolocation
{
    private const string PermissionName = "geolocation";

    private readonly Window _window;
    private readonly Dictionary<int, PositionWatch> _watches = [];
    private readonly List<Action> _waitingForVisibility = [];
    private int _lastWatchId;
    private bool _positionChangedWhileHidden;
    private GeolocationPosition? _cachedPosition;

    internal Geolocation(Window window)
    {
        _window = window;
        window.Options.Geolocation?.Register(this);
    }

    /// <summary>
    /// Requests the position of the device (https://w3c.github.io/geolocation/#getcurrentposition-method): the success
    /// callback is invoked with the position, or the error callback with the reason of the failure.
    /// </summary>
    /// <param name="successCallback">The callback invoked with the position.</param>
    /// <param name="errorCallback">The callback invoked when the position cannot be acquired.</param>
    /// <param name="options">The options of the request.</param>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocation-getcurrentposition"/>
    [IdlMember("getCurrentPosition", "https://w3c.github.io/geolocation/#dom-geolocation-getcurrentposition")]
    public void GetCurrentPosition(PositionCallback successCallback, PositionErrorCallback? errorCallback = null, PositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(successCallback);
        options ??= new PositionOptions();
        if (!_window.Document.IsFullyActive)
        {
            CallBackWithError(errorCallback, GeolocationPositionError.PositionUnavailable, documentIsInactive: true);
            return;
        }

        RequestPosition(successCallback, errorCallback, options, watchId: null);
    }

    /// <summary>
    /// Watches the position of the device (https://w3c.github.io/geolocation/#watchposition-method): the success callback is
    /// invoked with the position, then each time it changes, until <see cref="ClearWatch"/> is called. Returns the
    /// identifier of the watch (0 when the document is not fully active).
    /// </summary>
    /// <param name="successCallback">The callback invoked with the positions.</param>
    /// <param name="errorCallback">The callback invoked when a position cannot be acquired.</param>
    /// <param name="options">The options of the requests.</param>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocation-watchposition"/>
    [IdlMember("watchPosition", "https://w3c.github.io/geolocation/#dom-geolocation-watchposition")]
    public int WatchPosition(PositionCallback successCallback, PositionErrorCallback? errorCallback = null, PositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(successCallback);
        options ??= new PositionOptions();
        if (!_window.Document.IsFullyActive)
        {
            CallBackWithError(errorCallback, GeolocationPositionError.PositionUnavailable, documentIsInactive: true);
            return 0;
        }

        var watchId = ++_lastWatchId;
        _watches.Add(watchId, new PositionWatch(successCallback, errorCallback, options));
        RequestPosition(successCallback, errorCallback, options, watchId);
        return watchId;
    }

    /// <summary>Stops a watch created by <see cref="WatchPosition"/> (https://w3c.github.io/geolocation/#clearwatch-method).</summary>
    /// <param name="watchId">The identifier of the watch.</param>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-geolocation-clearwatch"/>
    [IdlMember("clearWatch", "https://w3c.github.io/geolocation/#dom-geolocation-clearwatch")]
    public void ClearWatch(int watchId) => _watches.Remove(watchId);

    /// <summary>https://w3c.github.io/geolocation/#dfn-request-a-position</summary>
    private void RequestPosition(PositionCallback successCallback, PositionErrorCallback? errorCallback, PositionOptions options, int? watchId)
    {
        var document = _window.Document;

        // https://wicg.github.io/nav-speculation/prerendering.html#patch-geolocation: a prerendering document requests the position once activated
        if (document.Prerendering)
        {
            document.RunAfterPrerendering(() => RequestPosition(successCallback, errorCallback, options, watchId));
            return;
        }

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Geolocation) || !_window.IsSecureContext)
        {
            Deny();
            return;
        }

        if (document.Hidden)
        {
            _waitingForVisibility.Add(Continue);
            return;
        }

        Continue();

        void Continue()
        {
            // The permission is requested in parallel: the method returns before the user answers
            _window.QueueGlobalTask(() =>
            {
                if (Permissions.RequestPermissionToUse(_window, PermissionName) == PermissionState.Denied)
                {
                    Deny();
                    return;
                }

                AcquirePosition(successCallback, errorCallback, options, watchId);
            });
        }

        void Deny()
        {
            if (watchId is { } id)
            {
                _watches.Remove(id);
            }

            CallBackWithError(errorCallback, GeolocationPositionError.PermissionDenied);
        }
    }

    /// <summary>https://w3c.github.io/geolocation/#dfn-acquire-a-position</summary>
    private void AcquirePosition(PositionCallback successCallback, PositionErrorCallback? errorCallback, PositionOptions options, int? watchId)
    {
        if (watchId is { } id && !_watches.ContainsKey(id))
            return;

        var timeProvider = _window.EventLoop.TimeProvider;
        var acquisitionTime = (ulong)Math.Max(0, (timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds);
        if (Permissions.GetPermissionState(_window, PermissionName) == PermissionState.Denied)
        {
            CallBackWithError(errorCallback, GeolocationPositionError.PermissionDenied);
            return;
        }

        // A cached position that is recent enough
        if (_cachedPosition is { } cachedPosition && options.MaximumAge > 0 && cachedPosition.Timestamp > acquisitionTime - Math.Min(acquisitionTime, options.MaximumAge) && cachedPosition.IsHighAccuracy == options.EnableHighAccuracy)
        {
            CallBackWithPosition(successCallback, cachedPosition, watchId);
            return;
        }

        if (_window.Options.Geolocation is not { } provider)
        {
            CallBackWithError(errorCallback, GeolocationPositionError.PositionUnavailable);
            return;
        }

        // A timeout of 0 elapses before a new position can be acquired
        if (options.Timeout == 0)
        {
            CallBackWithError(errorCallback, GeolocationPositionError.Timeout);
            return;
        }

        var request = new GeolocationRequest(options.EnableHighAccuracy);
        CancellationTokenSource? cancellation = options.Timeout == uint.MaxValue ? new CancellationTokenSource() : new CancellationTokenSource(TimeSpan.FromMilliseconds(options.Timeout), timeProvider);
        try
        {
            ValueTask<GeolocationReading?> reading;
            try
            {
                reading = provider.GetPositionAsync(request, cancellation.Token);
            }
#pragma warning disable CA1031 // An error of the provider is an unavailable position
            catch (Exception)
#pragma warning restore CA1031
            {
                // The timer starts before the provider is called: a provider that fails because the timeout elapsed timed out
                CallBackWithError(errorCallback, cancellation.IsCancellationRequested ? GeolocationPositionError.Timeout : GeolocationPositionError.PositionUnavailable);
                return;
            }

            if (reading.IsCompleted)
            {
                var completed = reading.AsTask();
                Complete(completed, timedOut: !completed.IsCompletedSuccessfully && cancellation.IsCancellationRequested);
                return;
            }

            var eventLoop = _window.EventLoop;
            eventLoop.BeginOperation();
#pragma warning disable CA2025 // WaitForPositionAsync owns the source and disposes it (the finally block sees null)
            _ = WaitForPositionAsync(reading.AsTask(), cancellation, eventLoop);
#pragma warning restore CA2025
            cancellation = null;
        }
        finally
        {
            cancellation?.Dispose();
        }

        // The position is acquired in parallel, until the timeout elapses
        async Task WaitForPositionAsync(Task<GeolocationReading?> readingTask, CancellationTokenSource timeout, DomEventLoop eventLoop)
        {
            using (timeout)
            {
                var timedOut = false;
                try
                {
                    await readingTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    timedOut = true;
                }
#pragma warning disable CA1031 // An error of the provider is an unavailable position (see Complete)
                catch (Exception)
#pragma warning restore CA1031
                {
                    // A provider that fails because the timeout elapsed timed out, whichever of the two completions is observed first
                    timedOut = timeout.IsCancellationRequested;
                }

                _window.QueueGlobalTask(() => Complete(readingTask, timedOut));
                eventLoop.EndOperation();
            }
        }

        void Complete(Task<GeolocationReading?> completed, bool timedOut)
        {
            if (watchId is { } watch && !_watches.ContainsKey(watch))
                return;

            if (timedOut)
            {
                CallBackWithError(errorCallback, GeolocationPositionError.Timeout);
                return;
            }

            if (!completed.IsCompletedSuccessfully || completed.Result is not { } result)
            {
                CallBackWithError(errorCallback, GeolocationPositionError.PositionUnavailable);
                return;
            }

            var position = new GeolocationPosition(new GeolocationCoordinates(result), acquisitionTime, options.EnableHighAccuracy);
            _cachedPosition = position;
            CallBackWithPosition(successCallback, position, watchId);
        }
    }

    private void CallBackWithPosition(PositionCallback callback, GeolocationPosition position, int? watchId)
    {
        _window.QueueGlobalTask(() =>
        {
            if (watchId is { } id && !_watches.ContainsKey(id))
                return;

            Invoke(() => callback(position));
        });
    }

    /// <summary>
    /// https://w3c.github.io/geolocation/#dfn-call-back-with-error. The task of a document that is not fully active is queued
    /// on the event loop: it would never run as a task of the document, and the callback may come from another document.
    /// </summary>
    private void CallBackWithError(PositionErrorCallback? callback, ushort code, bool documentIsInactive = false)
    {
        if (callback is null)
            return;

        var error = new GeolocationPositionError(code);
        if (documentIsInactive)
        {
            _window.EventLoop.QueueTask(() => Invoke(() => callback(error)));
        }
        else
        {
            _window.QueueGlobalTask(() => Invoke(() => callback(error)));
        }
    }

    private void Invoke(Action callback)
    {
        try
        {
            callback();
        }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, _window);
        }
    }

    /// <summary>
    /// The position of the provider changed (a significant change of position, https://w3c.github.io/geolocation/#dfn-request-a-position,
    /// step 6): the watches acquire the position again. Called from any thread.
    /// </summary>
    internal void OnPositionChanged()
    {
        _window.QueueGlobalTask(() =>
        {
            if (_watches.Count == 0)
                return;

            if (_window.Document.Hidden)
            {
                _positionChangedWhileHidden = true;
                return;
            }

            AcquireWatchedPositions();
        });
    }

    /// <summary>The document became visible: the requests that waited for it continue, and the watches get the position that changed while it was hidden.</summary>
    internal void OnDocumentVisible()
    {
        var waiting = _waitingForVisibility.ToArray();
        _waitingForVisibility.Clear();
        foreach (var request in waiting)
        {
            request();
        }

        if (_positionChangedWhileHidden)
        {
            _positionChangedWhileHidden = false;
            AcquireWatchedPositions();
        }
    }

    private void AcquireWatchedPositions()
    {
        foreach (var (watchId, watch) in _watches.ToArray())
        {
            AcquirePosition(watch.SuccessCallback, watch.ErrorCallback, watch.Options, watchId);
        }
    }

    private sealed record PositionWatch(PositionCallback SuccessCallback, PositionErrorCallback? ErrorCallback, PositionOptions Options);
}

// The Geolocation API (https://w3c.github.io/geolocation/)
public sealed partial class Navigator
{
    private Geolocation? _geolocation;

    /// <summary>Gets the Geolocation API of the window (https://w3c.github.io/geolocation/#navigator_interface).</summary>
    /// <seealso href="https://w3c.github.io/geolocation/#dom-navigator-geolocation"/>
    [IdlMember("geolocation", "https://w3c.github.io/geolocation/#dom-navigator-geolocation")]
    public Geolocation Geolocation => _geolocation ??= new Geolocation(_window);

    internal Geolocation? GeolocationOrNull => _geolocation;
}
