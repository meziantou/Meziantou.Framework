namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The initialization dictionary of a <see cref="DeviceOrientationEvent"/> (https://w3c.github.io/deviceorientation/#dictdef-deviceorientationeventinit).</summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#dictdef-deviceorientationeventinit"/>
[IdlInterface("DeviceOrientationEventInit", "https://w3c.github.io/deviceorientation/#dictdef-deviceorientationeventinit")]
public sealed class DeviceOrientationEventInit : EventInit
{
    /// <summary>Gets the rotation around the Z axis, in degrees, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-alpha"/>
    [IdlMember("alpha", "https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-alpha")]
    public double? Alpha { get; init; }

    /// <summary>Gets the rotation around the X' axis, in degrees, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-beta"/>
    [IdlMember("beta", "https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-beta")]
    public double? Beta { get; init; }

    /// <summary>Gets the rotation around the Y'' axis, in degrees, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-gamma"/>
    [IdlMember("gamma", "https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-gamma")]
    public double? Gamma { get; init; }

    /// <summary>Gets whether the orientation is relative to the Earth.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-absolute"/>
    [IdlMember("absolute", "https://w3c.github.io/deviceorientation/#dom-deviceorientationeventinit-absolute")]
    public bool Absolute { get; init; }
}

/// <summary>
/// The <c>deviceorientation</c> and <c>deviceorientationabsolute</c> events of a window (https://w3c.github.io/deviceorientation/#deviceorientation):
/// the orientation of the device as intrinsic Tait-Bryan angles Z - X' - Y'' in degrees, rounded to 0.1 degree. They are
/// fired when a listener is added and when the orientation sensor of the <see cref="DeviceSensorProvider"/> of the browsing
/// context gets a new reading, while the document is visible.
/// </summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#deviceorientationevent"/>
[IdlInterface("DeviceOrientationEvent", "https://w3c.github.io/deviceorientation/#deviceorientationevent")]
public sealed class DeviceOrientationEvent : Event
{
    /// <summary>Creates a device orientation event.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-deviceorientationevent"/>
    [IdlMember("constructor", "https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-deviceorientationevent")]
    public DeviceOrientationEvent(string type, DeviceOrientationEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Alpha = eventInitDict?.Alpha;
        Beta = eventInitDict?.Beta;
        Gamma = eventInitDict?.Gamma;
        Absolute = eventInitDict?.Absolute ?? false;
    }

    private DeviceOrientationEvent()
    {
    }

    /// <summary>Gets the rotation around the Z axis, in degrees, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-alpha"/>
    [IdlMember("alpha", "https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-alpha")]
    public double? Alpha { get; }

    /// <summary>Gets the rotation around the X' axis, in degrees, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-beta"/>
    [IdlMember("beta", "https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-beta")]
    public double? Beta { get; }

    /// <summary>Gets the rotation around the Y'' axis, in degrees, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-gamma"/>
    [IdlMember("gamma", "https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-gamma")]
    public double? Gamma { get; }

    /// <summary>Gets whether the orientation is relative to the Earth.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-absolute"/>
    [IdlMember("absolute", "https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-absolute")]
    public bool Absolute { get; }

    /// <summary>
    /// Requests the permissions of the orientation events (https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-requestpermission):
    /// <c>accelerometer</c> and <c>gyroscope</c>, and <c>magnetometer</c> when <paramref name="absolute"/> is true. The task
    /// fails with a <c>NotAllowedError</c> <see cref="DomException"/> when a permission would prompt the user without a
    /// transient activation.
    /// </summary>
    /// <param name="absolute">Whether the absolute orientation is requested.</param>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-requestpermission"/>
    [IdlMember("requestPermission", "https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-requestpermission")]
    public static Task<PermissionState> RequestPermission(bool absolute = false)
        => DeviceOrientationEventSource.RequestPermissions(absolute ? ["accelerometer", "gyroscope", "magnetometer"] : ["accelerometer", "gyroscope"]);

    internal static DeviceOrientationEvent CreateUninitializedDeviceOrientationEvent() => new();
}

/// <summary>The initialization dictionary of a <see cref="DeviceMotionEventAcceleration"/> (https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventaccelerationinit).</summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventaccelerationinit"/>
[IdlInterface("DeviceMotionEventAccelerationInit", "https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventaccelerationinit")]
public sealed class DeviceMotionEventAccelerationInit
{
    /// <summary>Gets the acceleration along the X axis, in m/s², or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventaccelerationinit-x"/>
    [IdlMember("x", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventaccelerationinit-x")]
    public double? X { get; init; }

    /// <summary>Gets the acceleration along the Y axis, in m/s², or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventaccelerationinit-y"/>
    [IdlMember("y", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventaccelerationinit-y")]
    public double? Y { get; init; }

    /// <summary>Gets the acceleration along the Z axis, in m/s², or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventaccelerationinit-z"/>
    [IdlMember("z", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventaccelerationinit-z")]
    public double? Z { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="DeviceMotionEventRotationRate"/> (https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventrotationrateinit).</summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventrotationrateinit"/>
[IdlInterface("DeviceMotionEventRotationRateInit", "https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventrotationrateinit")]
public sealed class DeviceMotionEventRotationRateInit
{
    /// <summary>Gets the rotation rate about the X axis, in deg/s, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrateinit-alpha"/>
    [IdlMember("alpha", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrateinit-alpha")]
    public double? Alpha { get; init; }

    /// <summary>Gets the rotation rate about the Y axis, in deg/s, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrateinit-beta"/>
    [IdlMember("beta", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrateinit-beta")]
    public double? Beta { get; init; }

    /// <summary>Gets the rotation rate about the Z axis, in deg/s, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrateinit-gamma"/>
    [IdlMember("gamma", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrateinit-gamma")]
    public double? Gamma { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="DeviceMotionEvent"/> (https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventinit).</summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventinit"/>
[IdlInterface("DeviceMotionEventInit", "https://w3c.github.io/deviceorientation/#dictdef-devicemotioneventinit")]
public sealed class DeviceMotionEventInit : EventInit
{
    /// <summary>Gets the acceleration without the gravity, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-acceleration"/>
    [IdlMember("acceleration", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-acceleration")]
    public DeviceMotionEventAccelerationInit? Acceleration { get; init; }

    /// <summary>Gets the acceleration including the gravity, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-accelerationincludinggravity"/>
    [IdlMember("accelerationIncludingGravity", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-accelerationincludinggravity")]
    public DeviceMotionEventAccelerationInit? AccelerationIncludingGravity { get; init; }

    /// <summary>Gets the rotation rate, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-rotationrate"/>
    [IdlMember("rotationRate", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-rotationrate")]
    public DeviceMotionEventRotationRateInit? RotationRate { get; init; }

    /// <summary>Gets the interval at which the data is obtained, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-interval"/>
    [IdlMember("interval", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventinit-interval")]
    public double Interval { get; init; }
}

/// <summary>An acceleration of a <see cref="DeviceMotionEvent"/> (https://w3c.github.io/deviceorientation/#devicemotioneventacceleration), in m/s².</summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#devicemotioneventacceleration"/>
[IdlInterface("DeviceMotionEventAcceleration", "https://w3c.github.io/deviceorientation/#devicemotioneventacceleration")]
public sealed class DeviceMotionEventAcceleration
{
    internal DeviceMotionEventAcceleration(double? x, double? y, double? z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the acceleration along the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventacceleration-x"/>
    [IdlMember("x", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventacceleration-x")]
    public double? X { get; }

    /// <summary>Gets the acceleration along the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventacceleration-y"/>
    [IdlMember("y", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventacceleration-y")]
    public double? Y { get; }

    /// <summary>Gets the acceleration along the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventacceleration-z"/>
    [IdlMember("z", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventacceleration-z")]
    public double? Z { get; }
}

/// <summary>A rotation rate of a <see cref="DeviceMotionEvent"/> (https://w3c.github.io/deviceorientation/#devicemotioneventrotationrate), in deg/s.</summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#devicemotioneventrotationrate"/>
[IdlInterface("DeviceMotionEventRotationRate", "https://w3c.github.io/deviceorientation/#devicemotioneventrotationrate")]
public sealed class DeviceMotionEventRotationRate
{
    internal DeviceMotionEventRotationRate(double? alpha, double? beta, double? gamma)
    {
        Alpha = alpha;
        Beta = beta;
        Gamma = gamma;
    }

    /// <summary>Gets the rotation rate about the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrate-alpha"/>
    [IdlMember("alpha", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrate-alpha")]
    public double? Alpha { get; }

    /// <summary>Gets the rotation rate about the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrate-beta"/>
    [IdlMember("beta", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrate-beta")]
    public double? Beta { get; }

    /// <summary>Gets the rotation rate about the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrate-gamma"/>
    [IdlMember("gamma", "https://w3c.github.io/deviceorientation/#dom-devicemotioneventrotationrate-gamma")]
    public double? Gamma { get; }
}

/// <summary>
/// The <c>devicemotion</c> event of a window (https://w3c.github.io/deviceorientation/#devicemotion): the acceleration
/// (linear acceleration sensor) and the acceleration including the gravity (accelerometer) in m/s² rounded to 0.1 m/s², and
/// the rotation rate (gyroscope) in deg/s rounded to 0.1 deg/s, read from the <see cref="DeviceSensorProvider"/> of the
/// browsing context. It is fired when a listener is added and every 16 ms while the sensors get new readings, while the
/// document is visible.
/// </summary>
/// <seealso href="https://w3c.github.io/deviceorientation/#devicemotionevent"/>
[IdlInterface("DeviceMotionEvent", "https://w3c.github.io/deviceorientation/#devicemotionevent")]
public sealed class DeviceMotionEvent : Event
{
    /// <summary>Creates a device motion event.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotionevent-devicemotionevent"/>
    [IdlMember("constructor", "https://w3c.github.io/deviceorientation/#dom-devicemotionevent-devicemotionevent")]
    public DeviceMotionEvent(string type, DeviceMotionEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        if (eventInitDict?.Acceleration is { } acceleration)
        {
            Acceleration = new DeviceMotionEventAcceleration(acceleration.X, acceleration.Y, acceleration.Z);
        }

        if (eventInitDict?.AccelerationIncludingGravity is { } accelerationIncludingGravity)
        {
            AccelerationIncludingGravity = new DeviceMotionEventAcceleration(accelerationIncludingGravity.X, accelerationIncludingGravity.Y, accelerationIncludingGravity.Z);
        }

        if (eventInitDict?.RotationRate is { } rotationRate)
        {
            RotationRate = new DeviceMotionEventRotationRate(rotationRate.Alpha, rotationRate.Beta, rotationRate.Gamma);
        }

        Interval = eventInitDict?.Interval ?? 0;
    }

    private DeviceMotionEvent()
    {
    }

    internal DeviceMotionEvent(string type, DeviceMotionEventAcceleration? acceleration, DeviceMotionEventAcceleration? accelerationIncludingGravity, DeviceMotionEventRotationRate? rotationRate, double interval)
        : base(type)
    {
        Acceleration = acceleration;
        AccelerationIncludingGravity = accelerationIncludingGravity;
        RotationRate = rotationRate;
        Interval = interval;
    }

    /// <summary>Gets the acceleration of the device without the gravity, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotionevent-acceleration"/>
    [IdlMember("acceleration", "https://w3c.github.io/deviceorientation/#dom-devicemotionevent-acceleration")]
    public DeviceMotionEventAcceleration? Acceleration { get; }

    /// <summary>Gets the acceleration of the device including the gravity, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotionevent-accelerationincludinggravity"/>
    [IdlMember("accelerationIncludingGravity", "https://w3c.github.io/deviceorientation/#dom-devicemotionevent-accelerationincludinggravity")]
    public DeviceMotionEventAcceleration? AccelerationIncludingGravity { get; }

    /// <summary>Gets the rotation rate of the device, or null.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotionevent-rotationrate"/>
    [IdlMember("rotationRate", "https://w3c.github.io/deviceorientation/#dom-devicemotionevent-rotationrate")]
    public DeviceMotionEventRotationRate? RotationRate { get; }

    /// <summary>Gets the interval at which the data is obtained, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotionevent-interval"/>
    [IdlMember("interval", "https://w3c.github.io/deviceorientation/#dom-devicemotionevent-interval")]
    public double Interval { get; }

    /// <summary>
    /// Requests the permissions of the motion events (https://w3c.github.io/deviceorientation/#dom-devicemotionevent-requestpermission):
    /// <c>accelerometer</c> and <c>gyroscope</c>. The task fails with a <c>NotAllowedError</c> <see cref="DomException"/>
    /// when a permission would prompt the user without a transient activation.
    /// </summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-devicemotionevent-requestpermission"/>
    [IdlMember("requestPermission", "https://w3c.github.io/deviceorientation/#dom-devicemotionevent-requestpermission")]
    public static Task<PermissionState> RequestPermission() => DeviceOrientationEventSource.RequestPermissions(["accelerometer", "gyroscope"]);

    internal static DeviceMotionEvent CreateUninitializedDeviceMotionEvent() => new();
}

/// <summary>
/// The device orientation and motion events of a window (https://w3c.github.io/deviceorientation/): the sensors of the device
/// are read while the window has listeners of the events. As in Chromium, an event is fired when a listener is added (with
/// null values when the device has no working sensor, or when the permissions are not granted), then when the sensors get
/// new readings.
/// </summary>
internal sealed class DeviceOrientationEventSource
{
    /// <summary>The interval of the <c>devicemotion</c> events, in milliseconds (60 Hz, as Chromium).</summary>
    private const double MotionInterval = 16;

    private readonly WindowSensors _owner;
    private readonly EventState _orientation = new("deviceorientation");
    private readonly EventState _absoluteOrientation = new("deviceorientationabsolute");
    private readonly EventState _motion = new("devicemotion");
    private bool _motionTimerPending;

    public DeviceOrientationEventSource(WindowSensors owner) => _owner = owner;

    private Window Window => _owner.Window;

    /// <summary>https://w3c.github.io/deviceorientation/#dom-deviceorientationevent-requestpermission</summary>
    public static Task<PermissionState> RequestPermissions(string[] permissions)
    {
        var global = GlobalScope.Current ?? throw new InvalidOperationException("There is no current global object");
        var hasTransientActivation = global is Window { HasTransientActivation: true };
        var promise = new TaskCompletionSource<PermissionState>();
        global.QueueGlobalTask(() =>
        {
            foreach (var name in permissions)
            {
                if (Permissions.GetPermissionState(global, name) == PermissionState.Prompt && !hasTransientActivation)
                {
                    promise.TrySetException(DomException.NotAllowed("Requesting the permission requires a user activation"));
                    return;
                }
            }

            var permissionState = PermissionState.Granted;
            foreach (var name in permissions)
            {
                if (Permissions.RequestPermissionToUse(global, name) != PermissionState.Granted)
                {
                    permissionState = PermissionState.Denied;
                    break;
                }
            }

            promise.TrySetResult(permissionState);
        });
        return promise.Task;
    }

    /// <summary>A listener of an event of the window was added or removed.</summary>
    public void OnListenersChanged(string type)
    {
        if (GetState(type) is not { } state)
            return;

        var hasListeners = Window.HasEventListener(type);
        if (hasListeners == state.Connected)
            return;

        state.Connected = hasListeners;
        state.LastValues = null;
        if (hasListeners)
        {
            // The sensors start, and the first event is fired from a task (never from addEventListener())
            QueueFire(state, initial: true);
        }
    }

    /// <summary>A sensor of the device was added or has a new reading (from a task of the event loop).</summary>
    public void OnDeviceSensorChanged(DeviceSensor device)
    {
        switch (device.Type)
        {
            case DeviceSensorType.RelativeOrientation or DeviceSensorType.AbsoluteOrientation:
                if (_orientation.Connected)
                {
                    QueueFire(_orientation, initial: false);
                }

                if (_absoluteOrientation.Connected && device.Type == DeviceSensorType.AbsoluteOrientation)
                {
                    QueueFire(_absoluteOrientation, initial: false);
                }

                break;

            case DeviceSensorType.Accelerometer or DeviceSensorType.LinearAcceleration or DeviceSensorType.Gyroscope when _motion.Connected:
                QueueFire(_motion, initial: false);
                break;
        }
    }

    /// <summary>The document became visible: the events are fired with the current readings.</summary>
    public void OnDocumentVisible()
    {
        foreach (var state in (ReadOnlySpan<EventState>)[_orientation, _absoluteOrientation, _motion])
        {
            if (state.Connected)
            {
                QueueFire(state, initial: true);
            }
        }
    }

    /// <summary>The document is no longer fully active: the sensors are no longer read.</summary>
    public void Disconnect()
    {
        _orientation.Connected = false;
        _absoluteOrientation.Connected = false;
        _motion.Connected = false;
    }

    private EventState? GetState(string type) => type switch
    {
        "deviceorientation" => _orientation,
        "deviceorientationabsolute" => _absoluteOrientation,
        "devicemotion" => _motion,
        _ => null,
    };

    private void QueueFire(EventState state, bool initial)
    {
        if (!Window.IsSecureContext || !Window.Document.IsFullyActive)
            return;

        state.PendingInitial |= initial;
        if (ReferenceEquals(state, _motion))
        {
            // The motion events are fired at an interval, with the latest readings of the three sensors
            if (_motionTimerPending)
                return;

            _motionTimerPending = true;
            Window.EventLoop.QueueTimer(TimeSpan.FromMilliseconds(MotionInterval), () => Window.QueueGlobalTask(() =>
            {
                _motionTimerPending = false;
                Fire(state);
            }));
            return;
        }

        Window.QueueGlobalTask(() => Fire(state));
    }

    private void Fire(EventState state)
    {
        var initial = state.PendingInitial;
        state.PendingInitial = false;
        var document = Window.Document;
        if (!state.Connected || !document.IsFullyActive || document.Hidden)
            return;

        if (ReferenceEquals(state, _motion))
        {
            FireMotionEvent(initial);
        }
        else
        {
            FireOrientationEvent(state, initial);
        }
    }

    /// <summary>
    /// https://w3c.github.io/deviceorientation/#fire-an-orientation-event: the <c>deviceorientation</c> event reads the relative
    /// orientation sensor, or the absolute one when the device has no working relative orientation sensor.
    /// </summary>
    private void FireOrientationEvent(EventState state, bool initial)
    {
        var absolute = ReferenceEquals(state, _absoluteOrientation);
        var sensor = absolute ? GetWorkingSensor(DeviceSensorType.AbsoluteOrientation) : GetWorkingSensor(DeviceSensorType.RelativeOrientation);
        if (sensor is null && !absolute && GetWorkingSensor(DeviceSensorType.AbsoluteOrientation) is { } fallback)
        {
            sensor = fallback;
            absolute = true;
        }

        if (!IsAllowedToUseFeatures(absolute))
            return;

        string[] permissions = absolute ? ["accelerometer", "gyroscope", "magnetometer"] : ["accelerometer", "gyroscope"];
        if (sensor is not null && !HasPermissions(permissions))
        {
            // As in Chromium, the event is fired once with null values when the permissions are not granted
            sensor = null;
        }

        double?[] values;
        if (sensor is null)
        {
            // An implementation that can never provide the orientation fires the event once with null values
            if (!initial)
                return;

            values = [null, null, null];
            absolute = ReferenceEquals(state, _absoluteOrientation);
        }
        else
        {
            if (sensor.Reading is not { Alpha: { } alpha, Beta: { } beta, Gamma: { } gamma })
                return;

            values = [SensorKind.RoundToTenth(alpha), SensorKind.RoundToTenth(beta), SensorKind.RoundToTenth(gamma)];
        }

        // A significant change in the orientation fires the event; the first event is always fired
        if (!initial && state.LastValues is { } last && last.AsSpan().SequenceEqual(values))
            return;

        state.LastValues = values;
        Window.FireEvent(new DeviceOrientationEvent(state.Type, new DeviceOrientationEventInit { Alpha = values[0], Beta = values[1], Gamma = values[2], Absolute = absolute }));
    }

    /// <summary>
    /// https://w3c.github.io/deviceorientation/#devicemotion: the readings of the linear acceleration sensor, the
    /// accelerometer and the gyroscope. As in Chromium, the objects of the event are not null: their values are null when
    /// the device has no working sensor, or when the permissions are not granted.
    /// </summary>
    private void FireMotionEvent(bool initial)
    {
        if (!IsAllowedToUseFeatures(absolute: false))
            return;

        var linearAcceleration = GetWorkingSensor(DeviceSensorType.LinearAcceleration);
        var accelerometer = GetWorkingSensor(DeviceSensorType.Accelerometer);
        var gyroscope = GetWorkingSensor(DeviceSensorType.Gyroscope);
        if (!HasPermissions(["accelerometer", "gyroscope"]))
        {
            if (!initial)
                return;

            linearAcceleration = null;
            accelerometer = null;
            gyroscope = null;
        }

        // The sensors that work but have no reading yet: the event is fired once they have one
        if ((linearAcceleration ?? accelerometer ?? gyroscope) is not null && linearAcceleration?.Reading is null && accelerometer?.Reading is null && gyroscope?.Reading is null)
            return;

        var acceleration = ToAcceleration(linearAcceleration?.Reading);
        var accelerationIncludingGravity = ToAcceleration(accelerometer?.Reading);
        var rotationRate = gyroscope?.Reading is { X: { } x, Y: { } y, Z: { } z }
            ? new DeviceMotionEventRotationRate(ToDegreesPerSecond(x), ToDegreesPerSecond(y), ToDegreesPerSecond(z))
            : new DeviceMotionEventRotationRate(null, null, null);
        Window.FireEvent(new DeviceMotionEvent("devicemotion", acceleration, accelerationIncludingGravity, rotationRate, MotionInterval));

        static DeviceMotionEventAcceleration ToAcceleration(DeviceSensorReading? reading) => reading is { X: { } x, Y: { } y, Z: { } z }
            ? new DeviceMotionEventAcceleration(SensorKind.RoundToTenth(x), SensorKind.RoundToTenth(y), SensorKind.RoundToTenth(z))
            : new DeviceMotionEventAcceleration(null, null, null);

        static double ToDegreesPerSecond(double radiansPerSecond) => SensorKind.RoundToTenth(radiansPerSecond * 180 / Math.PI);
    }

    private DeviceSensor? GetWorkingSensor(DeviceSensorType type) => _owner.Provider?.GetSensor(type) is { CanProvideReadings: true } sensor ? sensor : null;

    /// <summary>The document is allowed to use the <c>accelerometer</c> and <c>gyroscope</c> features (and <c>magnetometer</c> for the absolute orientation).</summary>
    private bool IsAllowedToUseFeatures(bool absolute)
    {
        var document = Window.Document;
        return PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Accelerometer)
            && PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Gyroscope)
            && (!absolute || PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Magnetometer));
    }

    private bool HasPermissions(string[] permissions)
    {
        foreach (var permission in permissions)
        {
            if (Permissions.GetPermissionState(Window, permission) != PermissionState.Granted)
                return false;
        }

        return true;
    }

    private sealed class EventState(string type)
    {
        public string Type { get; } = type;

        /// <summary>Whether the window has listeners of the event: the sensors are read.</summary>
        public bool Connected { get; set; }

        /// <summary>Whether the next event is the first one since the sensors started (it is fired even if the values did not change).</summary>
        public bool PendingInitial { get; set; }

        /// <summary>The values of the latest orientation event.</summary>
        public double?[]? LastValues { get; set; }
    }
}

// The device orientation and motion events of the window (https://w3c.github.io/deviceorientation/)
public sealed partial class Window
{
    /// <summary>Gets or sets the event handler of the <c>deviceorientation</c> event.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-window-ondeviceorientation"/>
    [IdlMember("ondeviceorientation", "https://w3c.github.io/deviceorientation/#dom-window-ondeviceorientation")]
    public Action<Event>? OnDeviceOrientation
    {
        get => GetEventHandler("deviceorientation");
        set => SetEventHandler("deviceorientation", value);
    }

    /// <summary>Gets or sets the event handler of the <c>deviceorientationabsolute</c> event.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-window-ondeviceorientationabsolute"/>
    [IdlMember("ondeviceorientationabsolute", "https://w3c.github.io/deviceorientation/#dom-window-ondeviceorientationabsolute")]
    public Action<Event>? OnDeviceOrientationAbsolute
    {
        get => GetEventHandler("deviceorientationabsolute");
        set => SetEventHandler("deviceorientationabsolute", value);
    }

    /// <summary>Gets or sets the event handler of the <c>devicemotion</c> event.</summary>
    /// <seealso href="https://w3c.github.io/deviceorientation/#dom-window-ondevicemotion"/>
    [IdlMember("ondevicemotion", "https://w3c.github.io/deviceorientation/#dom-window-ondevicemotion")]
    public Action<Event>? OnDeviceMotion
    {
        get => GetEventHandler("devicemotion");
        set => SetEventHandler("devicemotion", value);
    }

    /// <summary>A listener was added: the sensors of the device orientation and motion events start.</summary>
    private void OnDeviceOrientationListenerAdded(EventListenerEntry listener)
    {
        if (listener.Type is "deviceorientation" or "deviceorientationabsolute" or "devicemotion")
        {
            Sensors.OrientationEvents.OnListenersChanged(listener.Type);
        }
    }

    private protected override void OnEventListenerRemoved(EventListenerEntry listener)
    {
        base.OnEventListenerRemoved(listener);
        if (listener.Type is "deviceorientation" or "deviceorientationabsolute" or "devicemotion")
        {
            SensorsOrNull?.OrientationEvents.OnListenersChanged(listener.Type);
        }
    }
}
