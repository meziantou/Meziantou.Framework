namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The host hooks of the sensor APIs (<see cref="BrowsingContextOptions.Sensors"/>): the Generic Sensor API
/// (https://w3c.github.io/sensors/, <c>Accelerometer</c>, <c>Gyroscope</c>, <c>AbsoluteOrientationSensor</c>, etc.) and the
/// DeviceOrientation and DeviceMotion events (https://w3c.github.io/deviceorientation/).
/// </summary>
public sealed class DeviceSensorsOptions
{
    /// <summary>
    /// Gets or sets the sensors of the device: a <see cref="DeviceSensorProvider"/> whose sensors are added by the application
    /// (<see cref="DeviceSensorProvider.AddSensor"/>), which pushes their readings with <see cref="DeviceSensor.Update"/>. A
    /// provider can be shared by several browsing contexts. The default is null: the device has no sensor, so the
    /// <c>start()</c> method of the sensor objects fails with a <c>NotReadableError</c>, and the device orientation and
    /// motion events are fired once with null values.
    /// </summary>
    public DeviceSensorProvider? Provider { get; init; }
}

/// <summary>
/// The type of a sensor of the device (https://w3c.github.io/sensors/#virtual-sensor-type), read by the sensor objects of the
/// documents and by the device orientation and motion events.
/// </summary>
public enum DeviceSensorType
{
    /// <summary>The accelerometer (<c>accelerometer</c>): the acceleration of the device including the gravity, in m/s², read by <c>Accelerometer</c> and <c>devicemotion</c> (<c>accelerationIncludingGravity</c>).</summary>
    Accelerometer,

    /// <summary>The linear acceleration sensor (<c>linear-acceleration</c>): the acceleration without the gravity, in m/s², read by <c>LinearAccelerationSensor</c> and <c>devicemotion</c> (<c>acceleration</c>).</summary>
    LinearAcceleration,

    /// <summary>The gravity sensor (<c>gravity</c>): the acceleration due to the gravity, in m/s², read by <c>GravitySensor</c>.</summary>
    Gravity,

    /// <summary>The gyroscope (<c>gyroscope</c>): the angular velocity, in rad/s, read by <c>Gyroscope</c> and <c>devicemotion</c> (<c>rotationRate</c>, in deg/s).</summary>
    Gyroscope,

    /// <summary>The magnetometer (<c>magnetometer</c>): the magnetic field, in µT, read by <c>Magnetometer</c>.</summary>
    Magnetometer,

    /// <summary>The uncalibrated magnetometer (<c>uncalibrated-magnetometer</c>): the magnetic field and the hard iron distortion correction, in µT, read by <c>UncalibratedMagnetometer</c>.</summary>
    UncalibratedMagnetometer,

    /// <summary>The ambient light sensor (<c>ambient-light</c>): the illuminance, in lux, read by <c>AmbientLightSensor</c>.</summary>
    AmbientLight,

    /// <summary>The proximity sensor (<c>proximity</c>): the distance to the closest object, in cm, read by <c>ProximitySensor</c>.</summary>
    Proximity,

    /// <summary>The absolute orientation sensor (<c>absolute-orientation</c>): the orientation relative to the Earth, as Euler angles in degrees, read by <c>AbsoluteOrientationSensor</c> and <c>deviceorientationabsolute</c>.</summary>
    AbsoluteOrientation,

    /// <summary>The relative orientation sensor (<c>relative-orientation</c>): the orientation relative to a stationary frame, as Euler angles in degrees, read by <c>RelativeOrientationSensor</c> and <c>deviceorientation</c>.</summary>
    RelativeOrientation,
}

/// <summary>
/// A reading of a sensor of the device (https://w3c.github.io/sensors/#sensor-reading), given to <see cref="DeviceSensor.Update"/>.
/// Create it with the factory method of the shape of the sensor: <see cref="FromXyz"/> (the motion sensors and the
/// magnetometer), <see cref="FromUncalibratedMagnetometer"/>, <see cref="FromIlluminance"/>, <see cref="FromProximity"/> or
/// <see cref="FromEulerAngles"/> (the orientation sensors). The documents see the values quantized as the specifications
/// require (e.g. the accelerations rounded to 0.1 m/s², the illuminance to 50 lux).
/// </summary>
public sealed class DeviceSensorReading
{
    private DeviceSensorReading()
    {
    }

    /// <summary>Gets the value along the X axis (the acceleration, the angular velocity or the magnetic field), or null.</summary>
    public double? X { get; private init; }

    /// <summary>Gets the value along the Y axis, or null.</summary>
    public double? Y { get; private init; }

    /// <summary>Gets the value along the Z axis, or null.</summary>
    public double? Z { get; private init; }

    /// <summary>Gets the hard iron distortion correction along the X axis (uncalibrated magnetometer), or null.</summary>
    public double? XBias { get; private init; }

    /// <summary>Gets the hard iron distortion correction along the Y axis (uncalibrated magnetometer), or null.</summary>
    public double? YBias { get; private init; }

    /// <summary>Gets the hard iron distortion correction along the Z axis (uncalibrated magnetometer), or null.</summary>
    public double? ZBias { get; private init; }

    /// <summary>Gets the illuminance in lux (ambient light sensor), or null.</summary>
    public double? Illuminance { get; private init; }

    /// <summary>Gets the distance to the closest object in cm (proximity sensor), or null.</summary>
    public double? Distance { get; private init; }

    /// <summary>Gets the maximum sensing range in cm (proximity sensor), or null.</summary>
    public double? Max { get; private init; }

    /// <summary>Gets whether an object is near (proximity sensor), or null.</summary>
    public bool? Near { get; private init; }

    /// <summary>Gets the rotation around the Z axis in degrees, in [0, 360) (orientation sensors), or null.</summary>
    public double? Alpha { get; private init; }

    /// <summary>Gets the rotation around the X axis in degrees, in [-180, 180) (orientation sensors), or null.</summary>
    public double? Beta { get; private init; }

    /// <summary>Gets the rotation around the Y axis in degrees, in [-90, 90) (orientation sensors), or null.</summary>
    public double? Gamma { get; private init; }

    /// <summary>The unit quaternion (x, y, z, w) of the orientation (https://w3c.github.io/orientation-sensor/#create-a-quaternion-from-euler-angles).</summary>
    internal double[]? Quaternion { get; private init; }

    /// <summary>
    /// Creates a reading of three values along the axes (https://w3c.github.io/sensors/#parse-xyz-reading): the acceleration
    /// in m/s² (accelerometer, linear acceleration, gravity), the angular velocity in rad/s (gyroscope), or the magnetic
    /// field in µT (magnetometer).
    /// </summary>
    public static DeviceSensorReading FromXyz(double x, double y, double z) => new()
    {
        X = Finite(x, nameof(x)),
        Y = Finite(y, nameof(y)),
        Z = Finite(z, nameof(z)),
    };

    /// <summary>Creates a reading of the uncalibrated magnetometer (https://w3c.github.io/magnetometer/#uncalibrated-magnetometer-reading-parsing-algorithm), in µT.</summary>
    public static DeviceSensorReading FromUncalibratedMagnetometer(double x, double y, double z, double xBias, double yBias, double zBias) => new()
    {
        X = Finite(x, nameof(x)),
        Y = Finite(y, nameof(y)),
        Z = Finite(z, nameof(z)),
        XBias = Finite(xBias, nameof(xBias)),
        YBias = Finite(yBias, nameof(yBias)),
        ZBias = Finite(zBias, nameof(zBias)),
    };

    /// <summary>Creates a reading of the ambient light sensor (https://w3c.github.io/ambient-light/#ambient-light-reading-parsing-algorithm), in lux.</summary>
    public static DeviceSensorReading FromIlluminance(double illuminance) => new() { Illuminance = Finite(illuminance, nameof(illuminance)) };

    /// <summary>Creates a reading of the proximity sensor (https://w3c.github.io/proximity/#proximity-reading-parsing-algorithm), in cm.</summary>
    public static DeviceSensorReading FromProximity(double distance, double max, bool near) => new()
    {
        Distance = Finite(distance, nameof(distance)),
        Max = Finite(max, nameof(max)),
        Near = near,
    };

    /// <summary>
    /// Creates a reading of an orientation sensor (https://w3c.github.io/deviceorientation/#parse-orientation-data-reading),
    /// as intrinsic Tait-Bryan angles Z - X' - Y'' in degrees.
    /// </summary>
    /// <param name="alpha">The rotation around the Z axis, in [0, 360).</param>
    /// <param name="beta">The rotation around the X' axis, in [-180, 180).</param>
    /// <param name="gamma">The rotation around the Y'' axis, in [-90, 90).</param>
    public static DeviceSensorReading FromEulerAngles(double alpha, double beta, double gamma)
    {
        if (!double.IsFinite(alpha) || alpha is < 0 or >= 360)
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "The alpha angle must be in [0, 360)");

        if (!double.IsFinite(beta) || beta is < -180 or >= 180)
            throw new ArgumentOutOfRangeException(nameof(beta), beta, "The beta angle must be in [-180, 180)");

        if (!double.IsFinite(gamma) || gamma is < -90 or >= 90)
            throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "The gamma angle must be in [-90, 90)");

        return new DeviceSensorReading { Alpha = alpha, Beta = beta, Gamma = gamma, Quaternion = CreateQuaternion(alpha, beta, gamma) };
    }

    /// <summary>Whether the reading has the shape of the readings of a sensor type.</summary>
    internal bool IsValidFor(DeviceSensorType type) => type switch
    {
        DeviceSensorType.UncalibratedMagnetometer => X is not null && XBias is not null,
        DeviceSensorType.AmbientLight => Illuminance is not null,
        DeviceSensorType.Proximity => Distance is not null,
        DeviceSensorType.AbsoluteOrientation or DeviceSensorType.RelativeOrientation => Quaternion is not null,
        _ => X is not null,
    };

    /// <summary>https://w3c.github.io/orientation-sensor/#create-a-quaternion-from-euler-angles</summary>
    private static double[] CreateQuaternion(double alpha, double beta, double gamma)
    {
        var alphaInRadians = alpha * Math.PI / 180;
        var betaInRadians = beta * Math.PI / 180;
        var gammaInRadians = gamma * Math.PI / 180;
        var cosZ = Math.Cos(0.5 * alphaInRadians);
        var sinZ = Math.Sin(0.5 * alphaInRadians);
        var cosX = Math.Cos(0.5 * betaInRadians);
        var sinX = Math.Sin(0.5 * betaInRadians);
        var cosY = Math.Cos(0.5 * gammaInRadians);
        var sinY = Math.Sin(0.5 * gammaInRadians);
        return
        [
            (sinX * cosY * cosZ) - (cosX * sinY * sinZ),
            (cosX * sinY * cosZ) + (sinX * cosY * sinZ),
            (cosX * cosY * sinZ) + (sinX * sinY * cosZ),
            (cosX * cosY * cosZ) - (sinX * sinY * sinZ),
        ];
    }

    private static double Finite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "The value must be a finite number");

        return value;
    }
}

/// <summary>
/// The sensors of the device (https://w3c.github.io/sensors/#virtual-sensors) of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="DeviceSensorsOptions.Provider"/>): the application adds a sensor of a
/// type with <see cref="AddSensor"/>, then pushes its readings with <see cref="DeviceSensor.Update"/>. The sensor objects
/// of the documents (e.g. <c>new Accelerometer()</c>) connect to the sensor of their type when they start, and the device
/// orientation and motion events read the accelerometer, linear acceleration, gyroscope and orientation sensors.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public sealed class DeviceSensorProvider
{
    /// <summary>The default minimum sampling frequency of a sensor, in Hz.</summary>
    public const double DefaultMinimumSamplingFrequency = 1;

    /// <summary>The default maximum sampling frequency of a sensor, in Hz (the browsers do not report the readings more often, to mitigate the attacks).</summary>
    public const double DefaultMaximumSamplingFrequency = 60;

    private readonly Lock _lock = new();
    private readonly Dictionary<DeviceSensorType, DeviceSensor> _sensors = [];
    private readonly List<WeakReference<WindowSensors>> _listeners = [];

    /// <summary>Gets the sensors of the device.</summary>
    public IReadOnlyList<DeviceSensor> Sensors
    {
        get
        {
            lock (_lock)
            {
                return [.. _sensors.Values];
            }
        }
    }

    /// <summary>
    /// Adds a sensor to the device (https://w3c.github.io/sensors/#create-virtual-sensor-command). The sensor objects of its
    /// type that start from now on connect to it. A device has at most one sensor of each type.
    /// </summary>
    /// <param name="type">The type of the sensor.</param>
    /// <param name="canProvideReadings">Whether the sensor works: when false, the sensor objects that start fail with a <c>NotReadableError</c>, and the events report null values.</param>
    /// <param name="minimumSamplingFrequency">The minimum sampling frequency of the sensor, in Hz (<see cref="DefaultMinimumSamplingFrequency"/> by default).</param>
    /// <param name="maximumSamplingFrequency">The maximum sampling frequency of the sensor, in Hz (<see cref="DefaultMaximumSamplingFrequency"/> by default).</param>
    /// <exception cref="InvalidOperationException">The device already has a sensor of this type.</exception>
    public DeviceSensor AddSensor(DeviceSensorType type, bool canProvideReadings = true, double? minimumSamplingFrequency = null, double? maximumSamplingFrequency = null)
    {
        var minimum = minimumSamplingFrequency ?? DefaultMinimumSamplingFrequency;
        var maximum = maximumSamplingFrequency ?? DefaultMaximumSamplingFrequency;
        if (!double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(minimumSamplingFrequency), minimum, "The minimum sampling frequency must be a finite number");

        if (!double.IsFinite(maximum))
            throw new ArgumentOutOfRangeException(nameof(maximumSamplingFrequency), maximum, "The maximum sampling frequency must be a finite number");

        if (minimum > maximum)
            throw new ArgumentException("The minimum sampling frequency is greater than the maximum sampling frequency", nameof(minimumSamplingFrequency));

        var sensor = new DeviceSensor(this, type, canProvideReadings, minimum, maximum);
        lock (_lock)
        {
            if (!_sensors.TryAdd(type, sensor))
                throw new InvalidOperationException($"The device already has a sensor of type {type}");
        }

        NotifyChanged(sensor);
        return sensor;
    }

    /// <summary>
    /// Removes the sensor of a type from the device (https://w3c.github.io/sensors/#delete-virtual-sensor-command): the
    /// sensor objects connected to it no longer receive readings. Returns false when the device has no sensor of this type.
    /// </summary>
    public bool RemoveSensor(DeviceSensorType type)
    {
        DeviceSensor? sensor;
        lock (_lock)
        {
            if (!_sensors.Remove(type, out sensor))
                return false;
        }

        sensor.MarkRemoved();
        return true;
    }

    /// <summary>Returns the sensor of a type, or null when the device has none.</summary>
    public DeviceSensor? GetSensor(DeviceSensorType type)
    {
        lock (_lock)
        {
            return _sensors.GetValueOrDefault(type);
        }
    }

    /// <summary>Registers the sensors of a window, to give them the readings.</summary>
    internal void Register(WindowSensors listener)
    {
        lock (_lock)
        {
            _listeners.RemoveAll(static reference => !reference.TryGetTarget(out _));
            foreach (var reference in _listeners)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, listener))
                    return;
            }

            _listeners.Add(new WeakReference<WindowSensors>(listener));
        }
    }

    /// <summary>A sensor was added or got a new reading: the windows are notified (from any thread).</summary>
    internal void NotifyChanged(DeviceSensor sensor)
    {
        List<WindowSensors> listeners = [];
        lock (_lock)
        {
            foreach (var reference in _listeners)
            {
                if (reference.TryGetTarget(out var listener))
                {
                    listeners.Add(listener);
                }
            }
        }

        foreach (var listener in listeners)
        {
            listener.OnDeviceSensorChanged(sensor);
        }
    }
}

/// <summary>
/// A sensor of the device (https://w3c.github.io/sensors/#virtual-sensors), added by <see cref="DeviceSensorProvider.AddSensor"/>:
/// <see cref="Update"/> makes a new reading available to the sensor objects of the documents.
/// </summary>
public sealed class DeviceSensor
{
    private readonly DeviceSensorProvider _provider;
    private readonly Lock _lock = new();
    private readonly List<PlatformSensor> _platformSensors = [];
    private DeviceSensorReading? _reading;

    internal DeviceSensor(DeviceSensorProvider provider, DeviceSensorType type, bool canProvideReadings, double minimumSamplingFrequency, double maximumSamplingFrequency)
    {
        _provider = provider;
        Type = type;
        CanProvideReadings = canProvideReadings;
        MinimumSamplingFrequency = minimumSamplingFrequency;
        MaximumSamplingFrequency = maximumSamplingFrequency;
    }

    /// <summary>Gets the type of the sensor.</summary>
    public DeviceSensorType Type { get; }

    /// <summary>Gets whether the sensor works (the can provide readings flag): when false, the sensor objects cannot connect to it.</summary>
    public bool CanProvideReadings { get; }

    /// <summary>Gets the minimum sampling frequency of the sensor, in Hz.</summary>
    public double MinimumSamplingFrequency { get; }

    /// <summary>Gets the maximum sampling frequency of the sensor, in Hz.</summary>
    public double MaximumSamplingFrequency { get; }

    /// <summary>Gets whether the sensor was removed from the device (<see cref="DeviceSensorProvider.RemoveSensor"/>).</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>
    /// Gets the sampling frequency requested by the sensor objects that use the sensor, in Hz
    /// (https://w3c.github.io/sensors/#virtual-sensor-requested-sampling-frequency): the highest frequency they asked for,
    /// within the bounds of the sensor, or 0 when no sensor object uses it.
    /// </summary>
    public double RequestedSamplingFrequency
    {
        get
        {
            lock (_lock)
            {
                var result = 0d;
                foreach (var platformSensor in _platformSensors)
                {
                    if (platformSensor.SamplingFrequency is { } frequency && frequency > result)
                    {
                        result = frequency;
                    }
                }

                return result;
            }
        }
    }

    /// <summary>Gets the latest reading of the sensor, or null when it has none.</summary>
    public DeviceSensorReading? Reading
    {
        get
        {
            lock (_lock)
            {
                return _reading;
            }
        }
    }

    /// <summary>
    /// Makes a new reading available to the sensor objects that use the sensor (https://w3c.github.io/sensors/#update-virtual-sensor-reading-command):
    /// they process it from a task (it may be discarded by the threshold check of the sensor type, and it is delivered once
    /// the document is visible and focused). It can be called from any thread.
    /// </summary>
    /// <exception cref="ArgumentException">The reading does not have the shape of the readings of <see cref="Type"/>.</exception>
    public void Update(DeviceSensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (!reading.IsValidFor(Type))
            throw new ArgumentException($"The reading is not a reading of a sensor of type {Type}", nameof(reading));

        lock (_lock)
        {
            _reading = reading;
        }

        _provider.NotifyChanged(this);
    }

    internal void Connect(PlatformSensor platformSensor)
    {
        lock (_lock)
        {
            if (!_platformSensors.Contains(platformSensor))
            {
                _platformSensors.Add(platformSensor);
            }
        }
    }

    internal void Disconnect(PlatformSensor platformSensor)
    {
        lock (_lock)
        {
            _platformSensors.Remove(platformSensor);
        }
    }

    internal void MarkRemoved()
    {
        lock (_lock)
        {
            IsRemoved = true;
            _platformSensors.Clear();
        }
    }
}
