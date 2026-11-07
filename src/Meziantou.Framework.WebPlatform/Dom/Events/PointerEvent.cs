namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/pointerevents/#pointereventinit-dictionary</summary>
/// <seealso href="https://w3c.github.io/pointerevents/#pointereventinit-dictionary"/>
[IdlInterface("PointerEventInit", "https://w3c.github.io/pointerevents/#pointereventinit-dictionary")]
public sealed class PointerEventInit : MouseEventInit
{
    /// <summary>Gets the unique identifier of the pointer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-pointerid"/>
    [IdlMember("pointerId", "https://w3c.github.io/pointerevents/#dom-pointereventinit-pointerid")]
    public int PointerId { get; init; }

    /// <summary>Gets the width, in CSS pixels, of the contact geometry of the pointer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-width"/>
    [IdlMember("width", "https://w3c.github.io/pointerevents/#dom-pointereventinit-width")]
    public double Width { get; init; } = 1;

    /// <summary>Gets the height, in CSS pixels, of the contact geometry of the pointer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-height"/>
    [IdlMember("height", "https://w3c.github.io/pointerevents/#dom-pointereventinit-height")]
    public double Height { get; init; } = 1;

    /// <summary>Gets the normalized pressure of the pointer input, between 0 and 1.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-pressure"/>
    [IdlMember("pressure", "https://w3c.github.io/pointerevents/#dom-pointereventinit-pressure")]
    public float Pressure { get; init; }

    /// <summary>
    /// Gets the normalized tangential pressure (barrel pressure) of the pointer input, between -1 and 1.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-tangentialpressure"/>
    [IdlMember("tangentialPressure", "https://w3c.github.io/pointerevents/#dom-pointereventinit-tangentialpressure")]
    public float TangentialPressure { get; init; }

    /// <summary>
    /// Gets the plane angle, in degrees, between the Y-Z plane and the plane containing the transducer axis and the Y
    /// axis, or null to compute it from the spherical angles.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-tiltx"/>
    [IdlMember("tiltX", "https://w3c.github.io/pointerevents/#dom-pointereventinit-tiltx")]
    public int? TiltX { get; init; }

    /// <summary>
    /// Gets the plane angle, in degrees, between the X-Z plane and the plane containing the transducer axis and the X
    /// axis, or null to compute it from the spherical angles.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-tilty"/>
    [IdlMember("tiltY", "https://w3c.github.io/pointerevents/#dom-pointereventinit-tilty")]
    public int? TiltY { get; init; }

    /// <summary>Gets the clockwise rotation, in degrees, of the transducer around its own major axis.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-twist"/>
    [IdlMember("twist", "https://w3c.github.io/pointerevents/#dom-pointereventinit-twist")]
    public int Twist { get; init; }

    /// <summary>Gets the altitude, in radians, of the transducer, or null to compute it from the tilt.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-altitudeangle"/>
    [IdlMember("altitudeAngle", "https://w3c.github.io/pointerevents/#dom-pointereventinit-altitudeangle")]
    public double? AltitudeAngle { get; init; }

    /// <summary>Gets the azimuth angle, in radians, of the transducer, or null to compute it from the tilt.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-azimuthangle"/>
    [IdlMember("azimuthAngle", "https://w3c.github.io/pointerevents/#dom-pointereventinit-azimuthangle")]
    public double? AzimuthAngle { get; init; }

    /// <summary>
    /// Gets the type of the device that produced the event, such as <c>mouse</c>, <c>pen</c> or <c>touch</c>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-pointertype"/>
    [IdlMember("pointerType", "https://w3c.github.io/pointerevents/#dom-pointereventinit-pointertype")]
    public string PointerType { get; init; } = "";

    /// <summary>Gets a value indicating whether the pointer is the primary pointer of its type.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-isprimary"/>
    [IdlMember("isPrimary", "https://w3c.github.io/pointerevents/#dom-pointereventinit-isprimary")]
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Gets the identifier of the pointing device that stays the same across the pointers it produces.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-persistentdeviceid"/>
    [IdlMember("persistentDeviceId", "https://w3c.github.io/pointerevents/#dom-pointereventinit-persistentdeviceid")]
    public int PersistentDeviceId { get; init; }

    /// <summary>Gets the events that were coalesced into the event.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-coalescedevents"/>
    [IdlMember("coalescedEvents", "https://w3c.github.io/pointerevents/#dom-pointereventinit-coalescedevents")]
    public IReadOnlyList<PointerEvent> CoalescedEvents { get; init; } = [];

    /// <summary>Gets the events that are predicted to follow the event.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointereventinit-predictedevents"/>
    [IdlMember("predictedEvents", "https://w3c.github.io/pointerevents/#dom-pointereventinit-predictedevents")]
    public IReadOnlyList<PointerEvent> PredictedEvents { get; init; } = [];
}

/// <summary>https://w3c.github.io/pointerevents/#pointerevent-interface</summary>
/// <seealso href="https://w3c.github.io/pointerevents/#pointerevent-interface"/>
[IdlInterface("PointerEvent", "https://w3c.github.io/pointerevents/#pointerevent-interface")]
public sealed class PointerEvent : MouseEvent
{
    private PointerEvent[] _coalescedEvents;
    private readonly PointerEvent[] _predictedEvents;

    /// <summary>Creates a pointer event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/pointerevents/#dom-pointerevent-constructor")]
    public PointerEvent(string type, PointerEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        eventInitDict ??= new PointerEventInit();
        PointerId = eventInitDict.PointerId;
        Width = eventInitDict.Width;
        Height = eventInitDict.Height;
        Pressure = eventInitDict.Pressure;
        TangentialPressure = eventInitDict.TangentialPressure;
        Twist = eventInitDict.Twist;
        PointerType = eventInitDict.PointerType;
        IsPrimary = eventInitDict.IsPrimary;
        PersistentDeviceId = eventInitDict.PersistentDeviceId;

        // https://w3c.github.io/pointerevents/#converting-between-tiltx-tilty-and-altitudeangle-azimuthangle: the angles
        // that are not given are computed from the other ones (or their defaults: 0 tilt, π/2 altitude, 0 azimuth)
        var hasTilt = eventInitDict.TiltX is not null || eventInitDict.TiltY is not null;
        var hasAngles = eventInitDict.AltitudeAngle is not null || eventInitDict.AzimuthAngle is not null;
        if (hasTilt && hasAngles)
        {
            // Both sets are given: the values stay as initialized, and the missing value of a set is its default
            TiltX = eventInitDict.TiltX ?? 0;
            TiltY = eventInitDict.TiltY ?? 0;
            AltitudeAngle = eventInitDict.AltitudeAngle ?? Math.PI / 2;
            AzimuthAngle = eventInitDict.AzimuthAngle ?? 0;
        }
        else if (!hasAngles)
        {
            TiltX = eventInitDict.TiltX ?? 0;
            TiltY = eventInitDict.TiltY ?? 0;
            (AltitudeAngle, AzimuthAngle) = TiltToSpherical(TiltX, TiltY);
        }
        else
        {
            AltitudeAngle = eventInitDict.AltitudeAngle ?? Math.PI / 2;
            AzimuthAngle = eventInitDict.AzimuthAngle ?? 0;
            (TiltX, TiltY) = SphericalToTilt(AltitudeAngle, AzimuthAngle);
        }

        _coalescedEvents = [.. eventInitDict.CoalescedEvents];
        _predictedEvents = [.. eventInitDict.PredictedEvents];
    }

    /// <summary>Gets the unique identifier of the pointer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-pointerid"/>
    [IdlMember("pointerId", "https://w3c.github.io/pointerevents/#dom-pointerevent-pointerid")]
    public int PointerId { get; }

    /// <summary>Gets the width, in CSS pixels, of the contact geometry of the pointer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-width"/>
    [IdlMember("width", "https://w3c.github.io/pointerevents/#dom-pointerevent-width")]
    public double Width { get; }

    /// <summary>Gets the height, in CSS pixels, of the contact geometry of the pointer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-height"/>
    [IdlMember("height", "https://w3c.github.io/pointerevents/#dom-pointerevent-height")]
    public double Height { get; }

    /// <summary>Gets the normalized pressure of the pointer input, between 0 and 1.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-pressure"/>
    [IdlMember("pressure", "https://w3c.github.io/pointerevents/#dom-pointerevent-pressure")]
    public float Pressure { get; }

    /// <summary>
    /// Gets the normalized tangential pressure (barrel pressure) of the pointer input, between -1 and 1.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-tangentialpressure"/>
    [IdlMember("tangentialPressure", "https://w3c.github.io/pointerevents/#dom-pointerevent-tangentialpressure")]
    public float TangentialPressure { get; }

    /// <summary>
    /// Gets the plane angle, in degrees, between the Y-Z plane and the plane containing the transducer axis and the Y
    /// axis.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-tiltx"/>
    [IdlMember("tiltX", "https://w3c.github.io/pointerevents/#dom-pointerevent-tiltx")]
    public int TiltX { get; }

    /// <summary>
    /// Gets the plane angle, in degrees, between the X-Z plane and the plane containing the transducer axis and the X
    /// axis.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-tilty"/>
    [IdlMember("tiltY", "https://w3c.github.io/pointerevents/#dom-pointerevent-tilty")]
    public int TiltY { get; }

    /// <summary>Gets the clockwise rotation, in degrees, of the transducer around its own major axis.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-twist"/>
    [IdlMember("twist", "https://w3c.github.io/pointerevents/#dom-pointerevent-twist")]
    public int Twist { get; }

    /// <summary>Gets the altitude, in radians, of the transducer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-altitudeangle"/>
    [IdlMember("altitudeAngle", "https://w3c.github.io/pointerevents/#dom-pointerevent-altitudeangle")]
    public double AltitudeAngle { get; }

    /// <summary>Gets the azimuth angle, in radians, of the transducer.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-azimuthangle"/>
    [IdlMember("azimuthAngle", "https://w3c.github.io/pointerevents/#dom-pointerevent-azimuthangle")]
    public double AzimuthAngle { get; }

    /// <summary>
    /// Gets the type of the device that produced the event, such as <c>mouse</c>, <c>pen</c> or <c>touch</c>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-pointertype"/>
    [IdlMember("pointerType", "https://w3c.github.io/pointerevents/#dom-pointerevent-pointertype")]
    public string PointerType { get; }

    /// <summary>Gets a value indicating whether the pointer is the primary pointer of its type.</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-isprimary"/>
    [IdlMember("isPrimary", "https://w3c.github.io/pointerevents/#dom-pointerevent-isprimary")]
    public bool IsPrimary { get; }

    /// <summary>
    /// Gets the identifier of the pointing device that stays the same across the pointers it produces.
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-persistentdeviceid"/>
    [IdlMember("persistentDeviceId", "https://w3c.github.io/pointerevents/#dom-pointerevent-persistentdeviceid")]
    public int PersistentDeviceId { get; }

    /// <summary>https://w3c.github.io/pointerevents/#dom-pointerevent-getcoalescedevents</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-getcoalescedevents"/>
    [IdlMember("getCoalescedEvents", "https://w3c.github.io/pointerevents/#dom-pointerevent-getcoalescedevents")]
    public IReadOnlyList<PointerEvent> GetCoalescedEvents()
    {
        UpdateTargets(_coalescedEvents);
        return [.. _coalescedEvents];
    }

    /// <summary>https://w3c.github.io/pointerevents/#dom-pointerevent-getpredictedevents</summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-pointerevent-getpredictedevents"/>
    [IdlMember("getPredictedEvents", "https://w3c.github.io/pointerevents/#dom-pointerevent-getpredictedevents")]
    public IReadOnlyList<PointerEvent> GetPredictedEvents()
    {
        UpdateTargets(_predictedEvents);
        return [.. _predictedEvents];
    }

    /// <summary>Sets the coalesced events list of a trusted <c>pointermove</c> or <c>pointerrawupdate</c> event (https://w3c.github.io/pointerevents/#coalesced-events).</summary>
    internal void SetCoalescedEvents(PointerEvent[] events) => _coalescedEvents = events;

    /// <summary>
    /// "When a trusted PointerEvent's target is changed, user agent SHOULD, for each event in the coalesced events list and
    /// predicted events list: set the event's target to the trusted PointerEvent's target" (the targets are set when the lists
    /// are read).
    /// </summary>
    private void UpdateTargets(PointerEvent[] events)
    {
        if (!IsTrusted)
            return;

        foreach (var @event in events)
        {
            if (@event.IsTrusted)
            {
                @event.TargetInternal = TargetInternal;
            }
        }
    }

    private static (double Altitude, double Azimuth) TiltToSpherical(int tiltX, int tiltY)
    {
        if (tiltX == 0 && tiltY == 0)
            return (Math.PI / 2, 0);

        var tiltXRadians = tiltX * Math.PI / 180;
        var tiltYRadians = tiltY * Math.PI / 180;
        double azimuth;
        if (tiltX == 0)
        {
            azimuth = tiltY > 0 ? Math.PI / 2 : 3 * Math.PI / 2;
        }
        else if (tiltY == 0)
        {
            azimuth = tiltX < 0 ? Math.PI : 0;
        }
        else if (Math.Abs(tiltX) == 90 || Math.Abs(tiltY) == 90)
        {
            azimuth = 0;
        }
        else
        {
            azimuth = Math.Atan2(Math.Tan(tiltYRadians), Math.Tan(tiltXRadians));
            if (azimuth < 0)
            {
                azimuth += 2 * Math.PI;
            }
        }

        double altitude;
        if (Math.Abs(tiltX) == 90 || Math.Abs(tiltY) == 90)
        {
            altitude = 0;
        }
        else if (tiltX == 0)
        {
            altitude = (Math.PI / 2) - Math.Abs(tiltYRadians);
        }
        else if (tiltY == 0)
        {
            altitude = (Math.PI / 2) - Math.Abs(tiltXRadians);
        }
        else
        {
            altitude = Math.Atan(1.0 / Math.Sqrt(Math.Pow(Math.Tan(tiltXRadians), 2) + Math.Pow(Math.Tan(tiltYRadians), 2)));
        }

        return (altitude, azimuth);
    }

    private static (int TiltX, int TiltY) SphericalToTilt(double altitude, double azimuth)
    {
        const double RadiansToDegrees = 180 / Math.PI;
        double tiltXRadians = 0;
        double tiltYRadians = 0;
        if (altitude == 0)
        {
            if (azimuth is 0 or 2 * Math.PI)
            {
                tiltXRadians = Math.PI / 2;
            }
            else if (azimuth == Math.PI / 2)
            {
                tiltYRadians = Math.PI / 2;
            }
            else if (azimuth == Math.PI)
            {
                tiltXRadians = -Math.PI / 2;
            }
            else if (azimuth == 3 * Math.PI / 2)
            {
                tiltYRadians = -Math.PI / 2;
            }
            else if (azimuth > 0 && azimuth < Math.PI / 2)
            {
                tiltXRadians = Math.PI / 2;
                tiltYRadians = Math.PI / 2;
            }
            else if (azimuth > Math.PI / 2 && azimuth < Math.PI)
            {
                tiltXRadians = -Math.PI / 2;
                tiltYRadians = Math.PI / 2;
            }
            else if (azimuth > Math.PI && azimuth < 3 * Math.PI / 2)
            {
                tiltXRadians = -Math.PI / 2;
                tiltYRadians = -Math.PI / 2;
            }
            else if (azimuth > 3 * Math.PI / 2 && azimuth < 2 * Math.PI)
            {
                tiltXRadians = Math.PI / 2;
                tiltYRadians = -Math.PI / 2;
            }
        }
        else
        {
            var tanAltitude = Math.Tan(altitude);
            tiltXRadians = Math.Atan(Math.Cos(azimuth) / tanAltitude);
            tiltYRadians = Math.Atan(Math.Sin(azimuth) / tanAltitude);
        }

        return ((int)Math.Round(tiltXRadians * RadiansToDegrees, MidpointRounding.AwayFromZero), (int)Math.Round(tiltYRadians * RadiansToDegrees, MidpointRounding.AwayFromZero));
    }
}
