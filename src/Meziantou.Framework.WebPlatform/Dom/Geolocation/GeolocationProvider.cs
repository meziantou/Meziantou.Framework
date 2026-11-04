namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The source of the positions of the device (https://w3c.github.io/geolocation/) of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="BrowsingContextOptions.Geolocation"/>), read by
/// <c>navigator.geolocation.getCurrentPosition()</c> and <c>watchPosition()</c>. The default implementation returns
/// <see cref="Position"/>, set by the application (e.g. to simulate a location, as the <c>geolocation</c> option of
/// Playwright). Override <see cref="GetPositionAsync"/> to read another source (e.g. the location of the operating system),
/// and call <see cref="OnPositionChanged"/> when the position changes, to update the watches of the documents.
/// </summary>
/// <remarks>
/// The library checks the <c>geolocation</c> permission and the permissions policy before reading the position, and handles
/// the <c>timeout</c> and <c>maximumAge</c> options. A provider can be shared by several browsing contexts, and used from any thread.
/// </remarks>
public class GeolocationProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<Geolocation>> _geolocations = [];
    private GeolocationReading? _position;

    /// <summary>Raised when the position changes (<see cref="OnPositionChanged"/>).</summary>
    public event EventHandler? PositionChanged;

    /// <summary>
    /// Gets or sets the position returned by the default implementation of <see cref="GetPositionAsync"/>, or null when the
    /// position is unavailable (<c>POSITION_UNAVAILABLE</c>). Setting it updates the watches of the documents.
    /// </summary>
    public GeolocationReading? Position
    {
        get
        {
            lock (_lock)
            {
                return _position;
            }
        }
        set
        {
            lock (_lock)
            {
                _position = value;
            }

            OnPositionChanged();
        }
    }

    /// <summary>
    /// Returns the position of the device, or null when it is unavailable (<c>POSITION_UNAVAILABLE</c>). An exception is also
    /// reported as an unavailable position. The default implementation returns <see cref="Position"/>.
    /// </summary>
    /// <param name="request">The options of the request.</param>
    /// <param name="cancellationToken">Canceled when the timeout of the request elapses, or the document is unloaded.</param>
    public virtual ValueTask<GeolocationReading?> GetPositionAsync(GeolocationRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(Position);

    /// <summary>
    /// Raises <see cref="PositionChanged"/>, then reads the position again for the watches of the documents
    /// (<c>watchPosition()</c>): it is a significant change of the position. It can be called from any thread.
    /// </summary>
    protected void OnPositionChanged()
    {
        PositionChanged?.Invoke(this, EventArgs.Empty);
        List<Geolocation> geolocations = [];
        lock (_lock)
        {
            foreach (var reference in _geolocations)
            {
                if (reference.TryGetTarget(out var geolocation))
                {
                    geolocations.Add(geolocation);
                }
            }
        }

        foreach (var geolocation in geolocations)
        {
            geolocation.OnPositionChanged();
        }
    }

    /// <summary>Registers the <c>navigator.geolocation</c> object of a window, to update its watches.</summary>
    internal void Register(Geolocation geolocation)
    {
        lock (_lock)
        {
            _geolocations.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _geolocations.Add(new WeakReference<Geolocation>(geolocation));
        }
    }
}

/// <summary>A position of the device returned by a <see cref="GeolocationProvider"/> (https://w3c.github.io/geolocation/#coordinates-interface).</summary>
public sealed class GeolocationReading
{
    /// <summary>Creates a position.</summary>
    /// <param name="latitude">The latitude, in decimal degrees.</param>
    /// <param name="longitude">The longitude, in decimal degrees.</param>
    /// <param name="accuracy">The accuracy of the latitude and the longitude, in meters (a non-negative number).</param>
    public GeolocationReading(double latitude, double longitude, double accuracy)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "The latitude must be between -90 and 90");

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "The longitude must be between -180 and 180");

        if (!double.IsFinite(accuracy) || accuracy < 0)
            throw new ArgumentOutOfRangeException(nameof(accuracy), accuracy, "The accuracy must be a non-negative number");

        Latitude = latitude;
        Longitude = longitude;
        Accuracy = accuracy;
    }

    /// <summary>Gets the latitude, in decimal degrees.</summary>
    public double Latitude { get; }

    /// <summary>Gets the longitude, in decimal degrees.</summary>
    public double Longitude { get; }

    /// <summary>Gets the accuracy of the latitude and the longitude, in meters.</summary>
    public double Accuracy { get; }

    /// <summary>Gets the height above the WGS84 ellipsoid, in meters, or null when it is unknown.</summary>
    public double? Altitude { get; init; }

    /// <summary>Gets the accuracy of <see cref="Altitude"/>, in meters, or null when it is unknown.</summary>
    public double? AltitudeAccuracy { get; init; }

    /// <summary>Gets the direction of travel, in degrees clockwise from the true north, or null when it is unknown (it is ignored when <see cref="Speed"/> is 0).</summary>
    public double? Heading { get; init; }

    /// <summary>Gets the horizontal speed, in meters per second, or null when it is unknown.</summary>
    public double? Speed { get; init; }
}

/// <summary>The options of a request of a position, given to <see cref="GeolocationProvider.GetPositionAsync"/>.</summary>
public sealed class GeolocationRequest
{
    internal GeolocationRequest(bool enableHighAccuracy) => EnableHighAccuracy = enableHighAccuracy;

    /// <summary>Gets whether the document asks for the most accurate position (<c>enableHighAccuracy</c>), which may be slower and consume more power.</summary>
    public bool EnableHighAccuracy { get; }
}
