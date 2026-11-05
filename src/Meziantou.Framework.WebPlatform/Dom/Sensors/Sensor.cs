namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of a sensor object (https://w3c.github.io/sensors/#dictdef-sensoroptions).</summary>
/// <seealso href="https://w3c.github.io/sensors/#dictdef-sensoroptions"/>
[IdlInterface("SensorOptions", "https://w3c.github.io/sensors/#dictdef-sensoroptions")]
public class SensorOptions
{
    /// <summary>
    /// Gets the requested sampling frequency, in Hz. It is clamped to the bounds of the sensor type and of the sensor of the
    /// device when the sensor starts; the default is 5 Hz.
    /// </summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensoroptions-frequency"/>
    [IdlMember("frequency", "https://w3c.github.io/sensors/#dom-sensoroptions-frequency")]
    public double? Frequency { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="SensorErrorEvent"/> (https://w3c.github.io/sensors/#dictdef-sensorerroreventinit).</summary>
/// <seealso href="https://w3c.github.io/sensors/#dictdef-sensorerroreventinit"/>
[IdlInterface("SensorErrorEventInit", "https://w3c.github.io/sensors/#dictdef-sensorerroreventinit")]
public sealed class SensorErrorEventInit : EventInit
{
    /// <summary>Gets the error.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensorerroreventinit-error"/>
    [IdlMember("error", "https://w3c.github.io/sensors/#dom-sensorerroreventinit-error")]
    public required DomException Error { get; init; }
}

/// <summary>The <c>error</c> event of a <see cref="Sensor"/> (https://w3c.github.io/sensors/#the-sensor-error-event-interface).</summary>
/// <seealso href="https://w3c.github.io/sensors/#sensorerrorevent"/>
[IdlInterface("SensorErrorEvent", "https://w3c.github.io/sensors/#sensorerrorevent")]
public sealed class SensorErrorEvent : Event
{
    /// <summary>Creates an error event.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensorerrorevent-sensorerrorevent"/>
    [IdlMember("constructor", "https://w3c.github.io/sensors/#dom-sensorerrorevent-sensorerrorevent")]
    public SensorErrorEvent(string type, SensorErrorEventInit errorEventInitDict)
        : base(type, errorEventInitDict)
    {
        ArgumentNullException.ThrowIfNull(errorEventInitDict);
        Error = errorEventInitDict.Error;
    }

    /// <summary>Gets the error: a <c>NotAllowedError</c> when the permission is denied, a <c>NotReadableError</c> when the device has no working sensor of the type.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensorerrorevent-error"/>
    [IdlMember("error", "https://w3c.github.io/sensors/#dom-sensorerrorevent-error")]
    public DomException Error { get; }
}

/// <summary>
/// A sensor object of a document (https://w3c.github.io/sensors/#the-sensor-interface): the base of <see cref="Accelerometer"/>,
/// <see cref="Gyroscope"/>, <see cref="Magnetometer"/>, <see cref="AmbientLightSensor"/>, <see cref="OrientationSensor"/>,
/// etc. <see cref="Start"/> connects it to the sensor of its type of the <see cref="DeviceSensorProvider"/> of the browsing
/// context (<see cref="DeviceSensorsOptions.Provider"/>) after checking the permissions; the <c>reading</c> event is fired
/// when a new reading is available, at most at the requested frequency, while the document is visible and focused.
/// </summary>
/// <seealso href="https://w3c.github.io/sensors/#sensor"/>
[IdlInterface("Sensor", "https://w3c.github.io/sensors/#sensor")]
public abstract class Sensor : EventTarget
{
    /// <summary>The frequency of a sensor object that does not ask for one, in Hz (as Chromium).</summary>
    private const double DefaultFrequency = 5;

    private readonly Window _window;
    private SensorState _state;
    private double? _frequency;

    /// <summary>[[lastEventFiredAt]]: the timestamp of the latest reading sent to the observers, or null.</summary>
    private long? _lastEventFiredAt;

    /// <summary>[[pendingReadingNotification]]</summary>
    private bool _pendingReadingNotification;

    /// <summary>Incremented when the sensor object is started or deactivated: the tasks of a previous activation are ignored.</summary>
    private int _generation;

    private protected Sensor(SensorKind kind, SensorOptions? options, bool screenCoordinateSystem)
    {
        _window = GlobalScope.Current as Window ?? throw new InvalidOperationException("The sensor objects can only be created in a window");
        Kind = kind;
        UsesScreenCoordinateSystem = screenCoordinateSystem;

        // https://w3c.github.io/sensors/#check-sensor-policy-controlled-features
        foreach (var feature in kind.Features)
        {
            if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_window.Document, feature))
                throw new DomException($"The document is not allowed to use the {feature.Name} feature", "SecurityError");
        }

        // https://w3c.github.io/sensors/#initialize-a-sensor-object
        if (options?.Frequency is { } frequency)
        {
            _frequency = frequency;
        }
    }

    internal override GlobalScope? RelevantGlobal => _window;

    internal SensorKind Kind { get; }

    /// <summary>Whether the readings are remapped to the screen coordinate system (<c>referenceFrame: "screen"</c>).</summary>
    private bool UsesScreenCoordinateSystem { get; }

    /// <summary>The [[frequency]] of the sensor object, set when it connects to a sensor.</summary>
    internal double Frequency => _frequency ?? DefaultFrequency;

    /// <summary>Gets whether the sensor is activated: it is started and connected to a sensor of the device.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-activated"/>
    [IdlMember("activated", "https://w3c.github.io/sensors/#dom-sensor-activated")]
    public bool Activated => _state == SensorState.Activated;

    /// <summary>Gets whether the sensor has a reading.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-hasreading"/>
    [IdlMember("hasReading", "https://w3c.github.io/sensors/#dom-sensor-hasreading")]
    public bool HasReading => GetLatestReading() is not null;

    /// <summary>Gets the time of the latest reading, in milliseconds relative to the time origin of the window, or null.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/sensors/#dom-sensor-timestamp")]
    public double? Timestamp => GetLatestReading() is not null && GetPlatformSensor().LatestTimestamp is { } timestamp ? _window.ToRelativeHighResolutionTime(timestamp) : null;

    /// <summary>Gets or sets the event handler of the <c>reading</c> event.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-onreading"/>
    [IdlMember("onreading", "https://w3c.github.io/sensors/#dom-sensor-onreading")]
    public Action<Event>? OnReading
    {
        get => GetEventHandler("reading");
        set => SetEventHandler("reading", value);
    }

    /// <summary>Gets or sets the event handler of the <c>activate</c> event.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-onactivate"/>
    [IdlMember("onactivate", "https://w3c.github.io/sensors/#dom-sensor-onactivate")]
    public Action<Event>? OnActivate
    {
        get => GetEventHandler("activate");
        set => SetEventHandler("activate", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-onerror"/>
    [IdlMember("onerror", "https://w3c.github.io/sensors/#dom-sensor-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>
    /// Starts the sensor (https://w3c.github.io/sensors/#sensor-start): the permissions of the sensor type are requested, then
    /// the sensor connects to the sensor of its type of the device, and the <c>activate</c> event is fired. The <c>error</c>
    /// event is fired with a <c>NotAllowedError</c> when a permission is denied, and with a <c>NotReadableError</c> when the
    /// device has no working sensor of the type.
    /// </summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-start"/>
    [IdlMember("start", "https://w3c.github.io/sensors/#dom-sensor-start")]
    public void Start()
    {
        if (_state != SensorState.Idle)
            return;

        _state = SensorState.Activating;
        var generation = ++_generation;

        // https://wicg.github.io/nav-speculation/prerendering.html#patch-generic-sensor: a prerendering document connects to
        // the sensors once it is activated
        if (_window.Document.Prerendering)
        {
            _window.Document.RunAfterPrerendering(() => StartCore(generation));
            return;
        }

        StartCore(generation);
    }

    private void StartCore(int generation)
    {
        QueueSensorTask(generation, () =>
        {
            // https://w3c.github.io/sensors/#request-sensor-access
            foreach (var permission in Kind.PermissionNames)
            {
                if (Permissions.RequestPermissionToUse(_window, permission) == PermissionState.Denied)
                {
                    NotifyError(DomException.NotAllowed($"The {permission} permission is denied"));
                    return;
                }
            }

            var platformSensor = GetPlatformSensor();
            if (platformSensor.Connect() is not { } device)
            {
                NotifyError(new DomException("The device has no working sensor of this type", "NotReadableError"));
                return;
            }

            // https://w3c.github.io/sensors/#connect-to-sensor: the frequency is clamped to the bounds of the sensor
            var minimum = Math.Max(Kind.MinimumSamplingFrequency, device.MinimumSamplingFrequency);
            var maximum = Math.Min(Kind.MaximumSamplingFrequency, device.MaximumSamplingFrequency);
            _frequency = Math.Clamp(_frequency ?? DefaultFrequency, minimum, Math.Max(minimum, maximum));

            // https://w3c.github.io/sensors/#activate-a-sensor-object
            platformSensor.Activate(this);
            QueueSensorTask(generation, NotifyActivatedState);
        });
    }

    /// <summary>Stops the sensor (https://w3c.github.io/sensors/#sensor-stop): it no longer has a reading.</summary>
    /// <seealso href="https://w3c.github.io/sensors/#dom-sensor-stop"/>
    [IdlMember("stop", "https://w3c.github.io/sensors/#dom-sensor-stop")]
    public void Stop()
    {
        if (_state == SensorState.Idle)
            return;

        _state = SensorState.Idle;
        Deactivate();
    }

    /// <summary>https://w3c.github.io/sensors/#report-latest-reading-updated</summary>
    internal void ReportLatestReadingUpdated()
    {
        if (_pendingReadingNotification)
            return;

        _pendingReadingNotification = true;
        var generation = _generation;
        if (_lastEventFiredAt is not { } lastReported || GetPlatformSensor().LatestTimestamp is not { } latest)
        {
            QueueSensorTask(generation, NotifyNewReading);
            return;
        }

        // The events are fired at most at the frequency of the sensor object
        var reportingInterval = TimeSpan.FromSeconds(1 / Frequency);
        var timestampDelta = TimeSpan.FromSeconds((double)(latest - lastReported) / _window.EventLoop.TimeProvider.TimestampFrequency);
        if (timestampDelta >= reportingInterval)
        {
            QueueSensorTask(generation, NotifyNewReading);
            return;
        }

        _window.EventLoop.QueueTimer(reportingInterval - timestampDelta, () =>
        {
            if (_pendingReadingNotification)
            {
                QueueSensorTask(generation, NotifyNewReading);
            }
        });
    }

    /// <summary>The document is no longer fully active: the sensor object is disconnected from the sensor of the device.</summary>
    internal void OnDocumentUnloaded() => Deactivate();

    /// <summary>
    /// https://w3c.github.io/sensors/#get-value-from-latest-reading: the latest reading of the platform sensor, quantized
    /// (https://w3c.github.io/sensors/#reading-quantization-algorithm), or null when the sensor object is not activated.
    /// </summary>
    private protected DeviceSensorReading? GetLatestReading()
    {
        if (_state != SensorState.Activated)
            return null;

        return GetPlatformSensor().LatestReading;
    }

    /// <summary>Returns a value along an axis of the latest reading, quantized and remapped to the local coordinate system of the sensor object, or null.</summary>
    private protected double? GetAxisValue(int axis)
    {
        if (GetLatestReading() is not { X: { } x, Y: { } y, Z: { } z })
            return null;

        (x, y) = RemapToScreen(x, y);
        var value = axis switch
        {
            0 => x,
            1 => y,
            _ => z,
        };
        return Kind.Quantize(value);
    }

    /// <summary>
    /// Remaps the X and Y values to the screen coordinate system (https://w3c.github.io/accelerometer/#screen-coordinate-system):
    /// a rotation around the Z axis by the angle of the orientation of the screen.
    /// </summary>
    private protected (double X, double Y) RemapToScreen(double x, double y)
    {
        if (!UsesScreenCoordinateSystem || GetScreenOrientationAngle() is not (> 0 and var angle))
            return (x, y);

        var radians = angle * Math.PI / 180;
        var (sin, cos) = Math.SinCos(radians);
        return ((x * cos) - (y * sin), (x * sin) + (y * cos));
    }

    /// <summary>Remaps a quaternion to the screen coordinate system: it is rotated around the Z axis by the opposite of the angle of the orientation of the screen.</summary>
    private protected double[] RemapToScreen(double[] quaternion)
    {
        if (!UsesScreenCoordinateSystem || GetScreenOrientationAngle() is not (> 0 and var angle))
            return quaternion;

        var (sin, cos) = Math.SinCos(-angle * Math.PI / 360);
        var (x, y, z, w) = (quaternion[0], quaternion[1], quaternion[2], quaternion[3]);
        return
        [
            (x * cos) + (y * sin),
            (y * cos) - (x * sin),
            (w * sin) + (z * cos),
            (w * cos) - (z * sin),
        ];
    }

    /// <summary>The angle of the orientation of the screen, in degrees: the screen of the library is in its natural orientation.</summary>
    private static double GetScreenOrientationAngle() => 0;

    private PlatformSensor GetPlatformSensor() => _window.Sensors.GetPlatformSensor(Kind);

    private void Deactivate()
    {
        // https://w3c.github.io/sensors/#deactivate-a-sensor-object: the tasks of the sensor object are removed
        _generation++;
        GetPlatformSensor().Deactivate(this);
        _pendingReadingNotification = false;
        _lastEventFiredAt = null;
    }

    private void QueueSensorTask(int generation, Action action) => _window.QueueGlobalTask(() =>
    {
        if (generation == _generation)
        {
            action();
        }
    });

    /// <summary>https://w3c.github.io/sensors/#notify-new-reading</summary>
    private void NotifyNewReading()
    {
        _pendingReadingNotification = false;
        _lastEventFiredAt = GetPlatformSensor().LatestTimestamp;
        FireEvent(new Event("reading"));
    }

    /// <summary>https://w3c.github.io/sensors/#notify-activated-state</summary>
    private void NotifyActivatedState()
    {
        _state = SensorState.Activated;
        FireEvent(new Event("activate"));
        if (_state == SensorState.Activated && GetPlatformSensor().LatestTimestamp is not null)
        {
            QueueSensorTask(_generation, NotifyNewReading);
        }
    }

    /// <summary>https://w3c.github.io/sensors/#notify-error</summary>
    private void NotifyError(DomException error)
    {
        _state = SensorState.Idle;
        FireEvent(new SensorErrorEvent("error", new SensorErrorEventInit { Error = error }));
    }

    private enum SensorState
    {
        Idle,
        Activating,
        Activated,
    }
}

/// <summary>A sensor type (https://w3c.github.io/sensors/#sensor-type): its permissions, features, bounds and algorithms.</summary>
internal sealed class SensorKind
{
    private SensorKind(DeviceSensorType deviceType, string[] permissionNames, PolicyControlledFeature[] features)
    {
        DeviceType = deviceType;
        PermissionNames = permissionNames;
        Features = features;
    }

    public static SensorKind Accelerometer { get; } = new(DeviceSensorType.Accelerometer, ["accelerometer"], [PolicyControlledFeature.Accelerometer]) { Quantize = RoundToTenth };

    public static SensorKind LinearAcceleration { get; } = new(DeviceSensorType.LinearAcceleration, ["accelerometer"], [PolicyControlledFeature.Accelerometer]) { Quantize = RoundToTenth };

    public static SensorKind Gravity { get; } = new(DeviceSensorType.Gravity, ["accelerometer"], [PolicyControlledFeature.Accelerometer]) { Quantize = RoundToTenth };

    /// <summary>As Chromium, the angular velocity is rounded to 0.1 deg/s, to mitigate the fingerprinting of the calibration of the sensor.</summary>
    public static SensorKind Gyroscope { get; } = new(DeviceSensorType.Gyroscope, ["gyroscope"], [PolicyControlledFeature.Gyroscope]) { Quantize = RoundRadiansToTenthOfDegree };

    public static SensorKind Magnetometer { get; } = new(DeviceSensorType.Magnetometer, ["magnetometer"], [PolicyControlledFeature.Magnetometer]);

    public static SensorKind UncalibratedMagnetometer { get; } = new(DeviceSensorType.UncalibratedMagnetometer, ["magnetometer"], [PolicyControlledFeature.Magnetometer]);

    /// <summary>https://w3c.github.io/ambient-light/#model: the illuminance is rounded to 50 lux, and changes of less than 25 lux are discarded.</summary>
    public static SensorKind AmbientLight { get; } = new(DeviceSensorType.AmbientLight, ["ambient-light-sensor"], [PolicyControlledFeature.AmbientLightSensor])
    {
        Quantize = static value => Math.Round(value / IlluminanceRoundingMultiple, MidpointRounding.AwayFromZero) * IlluminanceRoundingMultiple,
        ThresholdCheck = static (newReading, latestReading) =>
        {
            // https://w3c.github.io/ambient-light/#ambient-light-threshold-check-algorithm
            if (newReading.Illuminance is not { } newIlluminance || latestReading.Illuminance is not { } latestIlluminance)
                return true;

            if (Math.Abs(latestIlluminance - newIlluminance) < IlluminanceThresholdValue)
                return false;

            return AmbientLight!.Quantize(newIlluminance) != AmbientLight.Quantize(latestIlluminance);
        },
    };

    public static SensorKind Proximity { get; } = new(DeviceSensorType.Proximity, ["proximity"], [PolicyControlledFeature.ProximitySensor]);

    public static SensorKind AbsoluteOrientation { get; } = new(DeviceSensorType.AbsoluteOrientation, ["accelerometer", "gyroscope", "magnetometer"], [PolicyControlledFeature.Accelerometer, PolicyControlledFeature.Gyroscope, PolicyControlledFeature.Magnetometer]);

    public static SensorKind RelativeOrientation { get; } = new(DeviceSensorType.RelativeOrientation, ["accelerometer", "gyroscope"], [PolicyControlledFeature.Accelerometer, PolicyControlledFeature.Gyroscope]);

    private const double IlluminanceRoundingMultiple = 50;
    private const double IlluminanceThresholdValue = 25;

    public DeviceSensorType DeviceType { get; }

    /// <summary>The sensor permission names (https://w3c.github.io/sensors/#sensor-permission-names).</summary>
    public string[] PermissionNames { get; }

    /// <summary>The sensor feature names (https://w3c.github.io/sensors/#sensor-feature-names).</summary>
    public PolicyControlledFeature[] Features { get; }

    /// <summary>The minimum sampling frequency of the sensor type, in Hz.</summary>
    public double MinimumSamplingFrequency => DeviceSensorProvider.DefaultMinimumSamplingFrequency;

    /// <summary>The maximum sampling frequency of the sensor type, in Hz (60 Hz, as Chromium).</summary>
    public double MaximumSamplingFrequency => DeviceSensorProvider.DefaultMaximumSamplingFrequency;

    /// <summary>The reading quantization algorithm (https://w3c.github.io/sensors/#reading-quantization-algorithm) of the values of the readings.</summary>
    public Func<double, double> Quantize { get; private init; } = static value => value;

    /// <summary>The threshold check algorithm (https://w3c.github.io/sensors/#threshold-check-algorithm), or null.</summary>
    public Func<DeviceSensorReading, DeviceSensorReading, bool>? ThresholdCheck { get; private init; }

    /// <summary>Rounds a value to the nearest 0.1 (e.g. https://w3c.github.io/accelerometer/#accelerometer-reading-quantization-algorithm).</summary>
    internal static double RoundToTenth(double value) => Math.Round(value * 10, MidpointRounding.AwayFromZero) / 10;

    private static double RoundRadiansToTenthOfDegree(double value) => RoundToTenth(value * 180 / Math.PI) * Math.PI / 180;
}

/// <summary>
/// A platform sensor of a window (https://w3c.github.io/sensors/#concept-platform-sensor): the sensor objects of a type that
/// are activated, connected to the sensor of the device, and the latest reading they share.
/// </summary>
internal sealed class PlatformSensor
{
    private readonly WindowSensors _owner;
    private readonly List<Sensor> _activatedSensors = [];
    private DeviceSensor? _device;
    private bool _isConnectedToDevice;

    /// <summary>A reading received while the readings cannot be exposed to the document (hidden or not focused), delivered later.</summary>
    private (DeviceSensorReading Reading, long Timestamp)? _stashedReading;

    public PlatformSensor(WindowSensors owner, SensorKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    public SensorKind Kind { get; }

    /// <summary>The sampling frequency, in Hz, or null when no sensor object is activated (read from any thread).</summary>
    public double? SamplingFrequency { get; private set; }

    /// <summary>The latest reading (https://w3c.github.io/sensors/#latest-reading), or null.</summary>
    public DeviceSensorReading? LatestReading { get; private set; }

    /// <summary>The timestamp of <see cref="LatestReading"/> (a timestamp of the time provider of the event loop), or null.</summary>
    public long? LatestTimestamp { get; private set; }

    /// <summary>https://w3c.github.io/sensors/#connect-to-sensor: returns the sensor of the device, or null when it has no working sensor of the type.</summary>
    public DeviceSensor? Connect()
    {
        if (_device is { IsRemoved: false } current)
            return current;

        if (_owner.Provider?.GetSensor(Kind.DeviceType) is not { CanProvideReadings: true } device)
            return null;

        _device = device;
        _isConnectedToDevice = false;
        return device;
    }

    public void Activate(Sensor sensor)
    {
        if (!_activatedSensors.Contains(sensor))
        {
            _activatedSensors.Add(sensor);
        }

        SetSensorSettings();
    }

    public void Deactivate(Sensor sensor)
    {
        if (_activatedSensors.Remove(sensor))
        {
            SetSensorSettings();
        }
    }

    /// <summary>The document is no longer fully active: the sensor objects are disconnected.</summary>
    public void DeactivateAll()
    {
        foreach (var sensor in _activatedSensors.ToArray())
        {
            sensor.OnDocumentUnloaded();
        }
    }

    /// <summary>The sensor of the device has a new reading (from a task of the event loop).</summary>
    public void OnDeviceReading(DeviceSensor device, DeviceSensorReading reading, long timestamp)
    {
        if (!ReferenceEquals(device, _device) || !_isConnectedToDevice || _activatedSensors.Count == 0)
            return;

        Receive(reading, timestamp, report: true);
    }

    /// <summary>The document became visible or focused: the reading received while it was not is delivered.</summary>
    public void OnExposureChanged()
    {
        if (_stashedReading is not { } stashed || !_owner.CanExposeSensorReadings())
            return;

        _stashedReading = null;
        UpdateLatestReading(stashed.Reading, stashed.Timestamp, report: true);
    }

    /// <summary>https://w3c.github.io/sensors/#set-sensor-settings</summary>
    private void SetSensorSettings()
    {
        if (_activatedSensors.Count == 0)
        {
            SamplingFrequency = null;
            LatestReading = null;
            LatestTimestamp = null;
            _stashedReading = null;
            _device?.Disconnect(this);
            _device = null;
            _isConnectedToDevice = false;
            return;
        }

        var frequency = 0d;
        foreach (var sensor in _activatedSensors)
        {
            frequency = Math.Max(frequency, sensor.Frequency);
        }

        SamplingFrequency = frequency;
        if (_device is { } device && !_isConnectedToDevice)
        {
            // The sensor starts to provide readings: its current reading is the first one (it is reported by the notify
            // activated state steps of the sensor object)
            _isConnectedToDevice = true;
            device.Connect(this);
            if (device.Reading is { } reading)
            {
                Receive(reading, _owner.Window.EventLoop.TimeProvider.GetTimestamp(), report: false);
            }
        }
    }

    private void Receive(DeviceSensorReading reading, long timestamp, bool report)
    {
        // https://w3c.github.io/sensors/#can-expose-sensor-readings: the reading is kept until the document can see it
        if (!_owner.CanExposeSensorReadings())
        {
            _stashedReading = (reading, timestamp);
            return;
        }

        _stashedReading = null;
        UpdateLatestReading(reading, timestamp, report);
    }

    /// <summary>https://w3c.github.io/sensors/#update-latest-reading</summary>
    private void UpdateLatestReading(DeviceSensorReading reading, long timestamp, bool report)
    {
        if (Kind.ThresholdCheck is { } thresholdCheck && LatestReading is { } latest && !thresholdCheck(reading, latest))
            return;

        LatestReading = reading;
        LatestTimestamp = timestamp;
        if (!report)
            return;

        foreach (var sensor in _activatedSensors.ToArray())
        {
            sensor.ReportLatestReadingUpdated();
        }
    }
}

/// <summary>The sensors of a window: its platform sensors and its device orientation and motion events.</summary>
internal sealed class WindowSensors
{
    private readonly Dictionary<SensorKind, PlatformSensor> _platformSensors = [];
    private DeviceOrientationEventSource? _orientationEvents;

    public WindowSensors(Window window)
    {
        Window = window;
        Provider = window.Options.Sensors?.Provider;
        Provider?.Register(this);
    }

    public Window Window { get; }

    public DeviceSensorProvider? Provider { get; }

    public DeviceOrientationEventSource OrientationEvents => _orientationEvents ??= new DeviceOrientationEventSource(this);

    public PlatformSensor GetPlatformSensor(SensorKind kind)
    {
        if (!_platformSensors.TryGetValue(kind, out var platformSensor))
        {
            platformSensor = new PlatformSensor(this, kind);
            _platformSensors.Add(kind, platformSensor);
        }

        return platformSensor;
    }

    /// <summary>
    /// https://w3c.github.io/sensors/#can-expose-sensor-readings: the document is in a secure context, visible, and same
    /// origin-domain with the document of the focused area of its top-level browsing context
    /// (https://w3c.github.io/sensors/#focus-and-origin-check).
    /// </summary>
    public bool CanExposeSensorReadings()
    {
        var document = Window.Document;
        if (!Window.IsSecureContext || document.Hidden || !document.IsFullyActive)
            return false;

        if (FocusModel.GetCurrentlyFocusedAreaOf(document) is not { } focusedArea)
            return true;

        var focusedDocument = focusedArea as Document ?? focusedArea.NodeDocument;
        return Document.IsSameOriginDomain(document, focusedDocument);
    }

    /// <summary>A sensor of the device was added or has a new reading (called from any thread).</summary>
    public void OnDeviceSensorChanged(DeviceSensor device)
    {
        var timestamp = Window.EventLoop.TimeProvider.GetTimestamp();
        var reading = device.Reading;
        Window.QueueGlobalTask(() =>
        {
            if (reading is not null)
            {
                foreach (var platformSensor in _platformSensors.Values)
                {
                    if (platformSensor.Kind.DeviceType == device.Type)
                    {
                        platformSensor.OnDeviceReading(device, reading, timestamp);
                    }
                }
            }

            _orientationEvents?.OnDeviceSensorChanged(device);
        });
    }

    /// <summary>The document became visible.</summary>
    public void OnDocumentVisible()
    {
        foreach (var platformSensor in _platformSensors.Values)
        {
            platformSensor.OnExposureChanged();
        }

        _orientationEvents?.OnDocumentVisible();
    }

    /// <summary>The document is no longer fully active.</summary>
    public void OnDocumentUnloaded()
    {
        foreach (var platformSensor in _platformSensors.Values)
        {
            platformSensor.DeactivateAll();
        }

        _orientationEvents?.Disconnect();
    }
}

// The sensors of the window (https://w3c.github.io/sensors/)
public sealed partial class Window
{
    private WindowSensors? _sensors;

    internal WindowSensors Sensors => _sensors ??= new WindowSensors(this);

    internal WindowSensors? SensorsOrNull => _sensors;
}
