using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The local coordinate system of an <see cref="Accelerometer"/> (https://w3c.github.io/accelerometer/#enumdef-accelerometerlocalcoordinatesystem).</summary>
/// <seealso href="https://w3c.github.io/accelerometer/#enumdef-accelerometerlocalcoordinatesystem"/>
[IdlInterface("AccelerometerLocalCoordinateSystem", "https://w3c.github.io/accelerometer/#enumdef-accelerometerlocalcoordinatesystem")]
public enum AccelerometerLocalCoordinateSystem
{
    /// <summary>The coordinate system of the device.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometerlocalcoordinatesystem-device"/>
    [IdlMember("device", "https://w3c.github.io/accelerometer/#dom-accelerometerlocalcoordinatesystem-device")]
    Device,

    /// <summary>The coordinate system of the screen, which follows its orientation.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometerlocalcoordinatesystem-screen"/>
    [IdlMember("screen", "https://w3c.github.io/accelerometer/#dom-accelerometerlocalcoordinatesystem-screen")]
    Screen,
}

/// <summary>The options of an <see cref="Accelerometer"/> (https://w3c.github.io/accelerometer/#dictdef-accelerometersensoroptions).</summary>
/// <seealso href="https://w3c.github.io/accelerometer/#dictdef-accelerometersensoroptions"/>
[IdlInterface("AccelerometerSensorOptions", "https://w3c.github.io/accelerometer/#dictdef-accelerometersensoroptions")]
public sealed class AccelerometerSensorOptions : SensorOptions
{
    /// <summary>Gets the local coordinate system of the readings.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometersensoroptions-referenceframe"/>
    [IdlMember("referenceFrame", "https://w3c.github.io/accelerometer/#dom-accelerometersensoroptions-referenceframe")]
    public AccelerometerLocalCoordinateSystem ReferenceFrame { get; init; }
}

/// <summary>
/// The accelerometer (https://w3c.github.io/accelerometer/#accelerometer-interface): the acceleration of the device along its
/// axes, including the gravity, in m/s² rounded to 0.1 m/s². It reads the <see cref="DeviceSensorType.Accelerometer"/> sensor
/// of the device, and needs the <c>accelerometer</c> permission and permissions policy feature.
/// </summary>
/// <seealso href="https://w3c.github.io/accelerometer/#accelerometer"/>
[IdlInterface("Accelerometer", "https://w3c.github.io/accelerometer/#accelerometer")]
public class Accelerometer : Sensor
{
    /// <summary>Creates an accelerometer. Throws a <c>SecurityError</c> <see cref="DomException"/> when the document is not allowed to use the <c>accelerometer</c> feature.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometer-accelerometer"/>
    [IdlMember("constructor", "https://w3c.github.io/accelerometer/#dom-accelerometer-accelerometer")]
    public Accelerometer(AccelerometerSensorOptions? options = null)
        : this(SensorKind.Accelerometer, options)
    {
    }

    private protected Accelerometer(SensorKind kind, AccelerometerSensorOptions? options)
        : base(kind, options, options?.ReferenceFrame == AccelerometerLocalCoordinateSystem.Screen)
    {
    }

    /// <summary>Gets the acceleration along the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometer-x"/>
    [IdlMember("x", "https://w3c.github.io/accelerometer/#dom-accelerometer-x")]
    public double? X => GetAxisValue(0);

    /// <summary>Gets the acceleration along the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometer-y"/>
    [IdlMember("y", "https://w3c.github.io/accelerometer/#dom-accelerometer-y")]
    public double? Y => GetAxisValue(1);

    /// <summary>Gets the acceleration along the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-accelerometer-z"/>
    [IdlMember("z", "https://w3c.github.io/accelerometer/#dom-accelerometer-z")]
    public double? Z => GetAxisValue(2);
}

/// <summary>
/// The linear acceleration sensor (https://w3c.github.io/accelerometer/#linearaccelerationsensor-interface): the acceleration
/// of the device without the gravity. It reads the <see cref="DeviceSensorType.LinearAcceleration"/> sensor of the device.
/// </summary>
/// <seealso href="https://w3c.github.io/accelerometer/#linearaccelerationsensor"/>
[IdlInterface("LinearAccelerationSensor", "https://w3c.github.io/accelerometer/#linearaccelerationsensor")]
public sealed class LinearAccelerationSensor : Accelerometer
{
    /// <summary>Creates a linear acceleration sensor.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-linearaccelerationsensor-linearaccelerationsensor"/>
    [IdlMember("constructor", "https://w3c.github.io/accelerometer/#dom-linearaccelerationsensor-linearaccelerationsensor")]
    public LinearAccelerationSensor(AccelerometerSensorOptions? options = null)
        : base(SensorKind.LinearAcceleration, options)
    {
    }
}

/// <summary>
/// The gravity sensor (https://w3c.github.io/accelerometer/#gravitysensor-interface): the acceleration of the device due to
/// the gravity. It reads the <see cref="DeviceSensorType.Gravity"/> sensor of the device.
/// </summary>
/// <seealso href="https://w3c.github.io/accelerometer/#gravitysensor"/>
[IdlInterface("GravitySensor", "https://w3c.github.io/accelerometer/#gravitysensor")]
public sealed class GravitySensor : Accelerometer
{
    /// <summary>Creates a gravity sensor.</summary>
    /// <seealso href="https://w3c.github.io/accelerometer/#dom-gravitysensor-gravitysensor"/>
    [IdlMember("constructor", "https://w3c.github.io/accelerometer/#dom-gravitysensor-gravitysensor")]
    public GravitySensor(AccelerometerSensorOptions? options = null)
        : base(SensorKind.Gravity, options)
    {
    }
}

/// <summary>The local coordinate system of a <see cref="Gyroscope"/> (https://w3c.github.io/gyroscope/#enumdef-gyroscopelocalcoordinatesystem).</summary>
/// <seealso href="https://w3c.github.io/gyroscope/#enumdef-gyroscopelocalcoordinatesystem"/>
[IdlInterface("GyroscopeLocalCoordinateSystem", "https://w3c.github.io/gyroscope/#enumdef-gyroscopelocalcoordinatesystem")]
public enum GyroscopeLocalCoordinateSystem
{
    /// <summary>The coordinate system of the device.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscopelocalcoordinatesystem-device"/>
    [IdlMember("device", "https://w3c.github.io/gyroscope/#dom-gyroscopelocalcoordinatesystem-device")]
    Device,

    /// <summary>The coordinate system of the screen, which follows its orientation.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscopelocalcoordinatesystem-screen"/>
    [IdlMember("screen", "https://w3c.github.io/gyroscope/#dom-gyroscopelocalcoordinatesystem-screen")]
    Screen,
}

/// <summary>The options of a <see cref="Gyroscope"/> (https://w3c.github.io/gyroscope/#dictdef-gyroscopesensoroptions).</summary>
/// <seealso href="https://w3c.github.io/gyroscope/#dictdef-gyroscopesensoroptions"/>
[IdlInterface("GyroscopeSensorOptions", "https://w3c.github.io/gyroscope/#dictdef-gyroscopesensoroptions")]
public sealed class GyroscopeSensorOptions : SensorOptions
{
    /// <summary>Gets the local coordinate system of the readings.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscopesensoroptions-referenceframe"/>
    [IdlMember("referenceFrame", "https://w3c.github.io/gyroscope/#dom-gyroscopesensoroptions-referenceframe")]
    public GyroscopeLocalCoordinateSystem ReferenceFrame { get; init; }
}

/// <summary>
/// The gyroscope (https://w3c.github.io/gyroscope/#gyroscope-interface): the angular velocity of the device around its axes,
/// in rad/s (rounded to 0.1 deg/s, as Chromium). It reads the <see cref="DeviceSensorType.Gyroscope"/> sensor of the device,
/// and needs the <c>gyroscope</c> permission and permissions policy feature.
/// </summary>
/// <seealso href="https://w3c.github.io/gyroscope/#gyroscope"/>
[IdlInterface("Gyroscope", "https://w3c.github.io/gyroscope/#gyroscope")]
public sealed class Gyroscope : Sensor
{
    /// <summary>Creates a gyroscope. Throws a <c>SecurityError</c> <see cref="DomException"/> when the document is not allowed to use the <c>gyroscope</c> feature.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscope-gyroscope"/>
    [IdlMember("constructor", "https://w3c.github.io/gyroscope/#dom-gyroscope-gyroscope")]
    public Gyroscope(GyroscopeSensorOptions? sensorOptions = null)
        : base(SensorKind.Gyroscope, sensorOptions, sensorOptions?.ReferenceFrame == GyroscopeLocalCoordinateSystem.Screen)
    {
    }

    /// <summary>Gets the angular velocity around the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscope-x"/>
    [IdlMember("x", "https://w3c.github.io/gyroscope/#dom-gyroscope-x")]
    public double? X => GetAxisValue(0);

    /// <summary>Gets the angular velocity around the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscope-y"/>
    [IdlMember("y", "https://w3c.github.io/gyroscope/#dom-gyroscope-y")]
    public double? Y => GetAxisValue(1);

    /// <summary>Gets the angular velocity around the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/gyroscope/#dom-gyroscope-z"/>
    [IdlMember("z", "https://w3c.github.io/gyroscope/#dom-gyroscope-z")]
    public double? Z => GetAxisValue(2);
}

/// <summary>The local coordinate system of a <see cref="Magnetometer"/> (https://w3c.github.io/magnetometer/#enumdef-magnetometerlocalcoordinatesystem).</summary>
/// <seealso href="https://w3c.github.io/magnetometer/#enumdef-magnetometerlocalcoordinatesystem"/>
[IdlInterface("MagnetometerLocalCoordinateSystem", "https://w3c.github.io/magnetometer/#enumdef-magnetometerlocalcoordinatesystem")]
public enum MagnetometerLocalCoordinateSystem
{
    /// <summary>The coordinate system of the device.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometerlocalcoordinatesystem-device"/>
    [IdlMember("device", "https://w3c.github.io/magnetometer/#dom-magnetometerlocalcoordinatesystem-device")]
    Device,

    /// <summary>The coordinate system of the screen, which follows its orientation.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometerlocalcoordinatesystem-screen"/>
    [IdlMember("screen", "https://w3c.github.io/magnetometer/#dom-magnetometerlocalcoordinatesystem-screen")]
    Screen,
}

/// <summary>The options of a <see cref="Magnetometer"/> (https://w3c.github.io/magnetometer/#dictdef-magnetometersensoroptions).</summary>
/// <seealso href="https://w3c.github.io/magnetometer/#dictdef-magnetometersensoroptions"/>
[IdlInterface("MagnetometerSensorOptions", "https://w3c.github.io/magnetometer/#dictdef-magnetometersensoroptions")]
public sealed class MagnetometerSensorOptions : SensorOptions
{
    /// <summary>Gets the local coordinate system of the readings.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometersensoroptions-referenceframe"/>
    [IdlMember("referenceFrame", "https://w3c.github.io/magnetometer/#dom-magnetometersensoroptions-referenceframe")]
    public MagnetometerLocalCoordinateSystem ReferenceFrame { get; init; }
}

/// <summary>
/// The magnetometer (https://w3c.github.io/magnetometer/#magnetometer-interface): the magnetic field around the device, in µT.
/// It reads the <see cref="DeviceSensorType.Magnetometer"/> sensor of the device, and needs the <c>magnetometer</c>
/// permission and permissions policy feature.
/// </summary>
/// <seealso href="https://w3c.github.io/magnetometer/#magnetometer"/>
[IdlInterface("Magnetometer", "https://w3c.github.io/magnetometer/#magnetometer")]
public sealed class Magnetometer : Sensor
{
    /// <summary>Creates a magnetometer. Throws a <c>SecurityError</c> <see cref="DomException"/> when the document is not allowed to use the <c>magnetometer</c> feature.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometer-magnetometer"/>
    [IdlMember("constructor", "https://w3c.github.io/magnetometer/#dom-magnetometer-magnetometer")]
    public Magnetometer(MagnetometerSensorOptions? sensorOptions = null)
        : base(SensorKind.Magnetometer, sensorOptions, sensorOptions?.ReferenceFrame == MagnetometerLocalCoordinateSystem.Screen)
    {
    }

    /// <summary>Gets the magnetic field along the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometer-x"/>
    [IdlMember("x", "https://w3c.github.io/magnetometer/#dom-magnetometer-x")]
    public double? X => GetAxisValue(0);

    /// <summary>Gets the magnetic field along the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometer-y"/>
    [IdlMember("y", "https://w3c.github.io/magnetometer/#dom-magnetometer-y")]
    public double? Y => GetAxisValue(1);

    /// <summary>Gets the magnetic field along the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-magnetometer-z"/>
    [IdlMember("z", "https://w3c.github.io/magnetometer/#dom-magnetometer-z")]
    public double? Z => GetAxisValue(2);
}

/// <summary>
/// The uncalibrated magnetometer (https://w3c.github.io/magnetometer/#uncalibrated-magnetometer-interface): the magnetic field
/// without the hard iron distortion correction, and the correction, in µT. It reads the
/// <see cref="DeviceSensorType.UncalibratedMagnetometer"/> sensor of the device.
/// </summary>
/// <seealso href="https://w3c.github.io/magnetometer/#uncalibratedmagnetometer"/>
[IdlInterface("UncalibratedMagnetometer", "https://w3c.github.io/magnetometer/#uncalibratedmagnetometer")]
public sealed class UncalibratedMagnetometer : Sensor
{
    /// <summary>Creates an uncalibrated magnetometer.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-uncalibratedmagnetometer"/>
    [IdlMember("constructor", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-uncalibratedmagnetometer")]
    public UncalibratedMagnetometer(MagnetometerSensorOptions? sensorOptions = null)
        : base(SensorKind.UncalibratedMagnetometer, sensorOptions, sensorOptions?.ReferenceFrame == MagnetometerLocalCoordinateSystem.Screen)
    {
    }

    /// <summary>Gets the uncalibrated magnetic field along the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-x"/>
    [IdlMember("x", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-x")]
    public double? X => GetAxisValue(0);

    /// <summary>Gets the uncalibrated magnetic field along the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-y"/>
    [IdlMember("y", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-y")]
    public double? Y => GetAxisValue(1);

    /// <summary>Gets the uncalibrated magnetic field along the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-z"/>
    [IdlMember("z", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-z")]
    public double? Z => GetAxisValue(2);

    /// <summary>Gets the hard iron distortion correction along the X axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-xbias"/>
    [IdlMember("xBias", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-xbias")]
    public double? XBias => GetBias(0);

    /// <summary>Gets the hard iron distortion correction along the Y axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-ybias"/>
    [IdlMember("yBias", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-ybias")]
    public double? YBias => GetBias(1);

    /// <summary>Gets the hard iron distortion correction along the Z axis, or null.</summary>
    /// <seealso href="https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-zbias"/>
    [IdlMember("zBias", "https://w3c.github.io/magnetometer/#dom-uncalibratedmagnetometer-zbias")]
    public double? ZBias => GetBias(2);

    private double? GetBias(int axis)
    {
        if (GetLatestReading() is not { XBias: { } x, YBias: { } y, ZBias: { } z })
            return null;

        (x, y) = RemapToScreen(x, y);
        return axis switch
        {
            0 => x,
            1 => y,
            _ => z,
        };
    }
}

/// <summary>
/// The ambient light sensor (https://w3c.github.io/ambient-light/#ambient-light-sensor-interface): the illuminance around the
/// device, in lux, rounded to 50 lux (the changes of less than 25 lux are discarded). It reads the
/// <see cref="DeviceSensorType.AmbientLight"/> sensor of the device, and needs the <c>ambient-light-sensor</c> permission and
/// permissions policy feature.
/// </summary>
/// <seealso href="https://w3c.github.io/ambient-light/#ambientlightsensor"/>
[IdlInterface("AmbientLightSensor", "https://w3c.github.io/ambient-light/#ambientlightsensor")]
public sealed class AmbientLightSensor : Sensor
{
    /// <summary>Creates an ambient light sensor. Throws a <c>SecurityError</c> <see cref="DomException"/> when the document is not allowed to use the <c>ambient-light-sensor</c> feature.</summary>
    /// <seealso href="https://w3c.github.io/ambient-light/#dom-ambientlightsensor-ambientlightsensor"/>
    [IdlMember("constructor", "https://w3c.github.io/ambient-light/#dom-ambientlightsensor-ambientlightsensor")]
    public AmbientLightSensor(SensorOptions? sensorOptions = null)
        : base(SensorKind.AmbientLight, sensorOptions, screenCoordinateSystem: false)
    {
    }

    /// <summary>Gets the illuminance, in lux, or null.</summary>
    /// <seealso href="https://w3c.github.io/ambient-light/#dom-ambientlightsensor-illuminance"/>
    [IdlMember("illuminance", "https://w3c.github.io/ambient-light/#dom-ambientlightsensor-illuminance")]
    public double? Illuminance => GetLatestReading()?.Illuminance is { } illuminance ? Kind.Quantize(illuminance) : null;
}

/// <summary>
/// The proximity sensor (https://w3c.github.io/proximity/#proximity-sensor-interface): the distance to the closest object, in
/// cm. It reads the <see cref="DeviceSensorType.Proximity"/> sensor of the device, and needs the <c>proximity</c> permission
/// and the <c>proximity-sensor</c> permissions policy feature.
/// </summary>
/// <seealso href="https://w3c.github.io/proximity/#proximitysensor"/>
[IdlInterface("ProximitySensor", "https://w3c.github.io/proximity/#proximitysensor")]
public sealed class ProximitySensor : Sensor
{
    /// <summary>Creates a proximity sensor. Throws a <c>SecurityError</c> <see cref="DomException"/> when the document is not allowed to use the <c>proximity-sensor</c> feature.</summary>
    /// <seealso href="https://w3c.github.io/proximity/#dom-proximitysensor-proximitysensor"/>
    [IdlMember("constructor", "https://w3c.github.io/proximity/#dom-proximitysensor-proximitysensor")]
    public ProximitySensor(SensorOptions? sensorOptions = null)
        : base(SensorKind.Proximity, sensorOptions, screenCoordinateSystem: false)
    {
    }

    /// <summary>Gets the distance to the closest object, in cm, or null.</summary>
    /// <seealso href="https://w3c.github.io/proximity/#dom-proximitysensor-distance"/>
    [IdlMember("distance", "https://w3c.github.io/proximity/#dom-proximitysensor-distance")]
    public double? Distance => GetLatestReading()?.Distance;

    /// <summary>Gets the maximum sensing range, in cm, or null.</summary>
    /// <seealso href="https://w3c.github.io/proximity/#dom-proximitysensor-max"/>
    [IdlMember("max", "https://w3c.github.io/proximity/#dom-proximitysensor-max")]
    public double? Max => GetLatestReading()?.Max;

    /// <summary>Gets whether an object is near, or null.</summary>
    /// <seealso href="https://w3c.github.io/proximity/#dom-proximitysensor-near"/>
    [IdlMember("near", "https://w3c.github.io/proximity/#dom-proximitysensor-near")]
    public bool? Near => GetLatestReading()?.Near;
}

/// <summary>The local coordinate system of an <see cref="OrientationSensor"/> (https://w3c.github.io/orientation-sensor/#enumdef-orientationsensorlocalcoordinatesystem).</summary>
/// <seealso href="https://w3c.github.io/orientation-sensor/#enumdef-orientationsensorlocalcoordinatesystem"/>
[IdlInterface("OrientationSensorLocalCoordinateSystem", "https://w3c.github.io/orientation-sensor/#enumdef-orientationsensorlocalcoordinatesystem")]
public enum OrientationSensorLocalCoordinateSystem
{
    /// <summary>The coordinate system of the device.</summary>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-orientationsensorlocalcoordinatesystem-device"/>
    [IdlMember("device", "https://w3c.github.io/orientation-sensor/#dom-orientationsensorlocalcoordinatesystem-device")]
    Device,

    /// <summary>The coordinate system of the screen, which follows its orientation.</summary>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-orientationsensorlocalcoordinatesystem-screen"/>
    [IdlMember("screen", "https://w3c.github.io/orientation-sensor/#dom-orientationsensorlocalcoordinatesystem-screen")]
    Screen,
}

/// <summary>The options of an <see cref="OrientationSensor"/> (https://w3c.github.io/orientation-sensor/#dictdef-orientationsensoroptions).</summary>
/// <seealso href="https://w3c.github.io/orientation-sensor/#dictdef-orientationsensoroptions"/>
[IdlInterface("OrientationSensorOptions", "https://w3c.github.io/orientation-sensor/#dictdef-orientationsensoroptions")]
public sealed class OrientationSensorOptions : SensorOptions
{
    /// <summary>Gets the local coordinate system of the readings.</summary>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-orientationsensoroptions-referenceframe"/>
    [IdlMember("referenceFrame", "https://w3c.github.io/orientation-sensor/#dom-orientationsensoroptions-referenceframe")]
    public OrientationSensorLocalCoordinateSystem ReferenceFrame { get; init; }
}

/// <summary>
/// An orientation sensor (https://w3c.github.io/orientation-sensor/#orientationsensor-interface): the orientation of the
/// device, as a unit quaternion. The base of <see cref="AbsoluteOrientationSensor"/> and <see cref="RelativeOrientationSensor"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/orientation-sensor/#orientationsensor"/>
[IdlInterface("OrientationSensor", "https://w3c.github.io/orientation-sensor/#orientationsensor")]
public abstract class OrientationSensor : Sensor
{
    private DeviceSensorReading? _quaternionReading;
    private IReadOnlyList<double>? _quaternion;

    private protected OrientationSensor(SensorKind kind, OrientationSensorOptions? options)
        : base(kind, options, options?.ReferenceFrame == OrientationSensorLocalCoordinateSystem.Screen)
    {
    }

    /// <summary>
    /// Gets the components (x, y, z, w) of the unit quaternion of the orientation of the device, or null. It is the same list
    /// while the reading does not change.
    /// </summary>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-orientationsensor-quaternion"/>
    [IdlMember("quaternion", "https://w3c.github.io/orientation-sensor/#dom-orientationsensor-quaternion")]
    public IReadOnlyList<double>? Quaternion
    {
        get
        {
            if (GetLatestReading() is not { Quaternion: { } quaternion } reading)
                return null;

            // A frozen array is the same object while the reading is the same
            if (!ReferenceEquals(reading, _quaternionReading))
            {
                _quaternionReading = reading;
                _quaternion = Array.AsReadOnly(RemapToScreen(quaternion));
            }

            return _quaternion;
        }
    }

    /// <summary>
    /// Writes the rotation matrix of the orientation of the device to a matrix (https://w3c.github.io/orientation-sensor/#dom-orientationsensor-populatematrix):
    /// a <c>Float32Array</c> or a <c>Float64Array</c> of at least 16 elements (a <see cref="float"/> or <see cref="double"/>
    /// array from .NET), or a <see cref="DomMatrix"/>. Throws a <see cref="TypeErrorException"/> when the array is too short,
    /// and a <c>NotReadableError</c> <see cref="DomException"/> when the sensor has no reading.
    /// </summary>
    /// <param name="targetMatrix">The matrix to fill, in column-major order.</param>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-orientationsensor-populatematrix"/>
    [IdlMember("populateMatrix", "https://w3c.github.io/orientation-sensor/#dom-orientationsensor-populatematrix")]
    public void PopulateMatrix(object? targetMatrix)
    {
        var length = targetMatrix switch
        {
            JsBufferSource { Kind: JsBufferKind.Float32Array } buffer => buffer.Bytes.Length / sizeof(float),
            JsBufferSource { Kind: JsBufferKind.Float64Array } buffer => buffer.Bytes.Length / sizeof(double),
            float[] array => array.Length,
            double[] array => array.Length,
            DomMatrix => 16,
            _ => throw new TypeErrorException("The target matrix must be a Float32Array, a Float64Array or a DOMMatrix"),
        };

        if (length < 16)
            throw new TypeErrorException("The target matrix must have at least 16 elements");

        if (Quaternion is not { } quaternion)
            throw new DomException("The sensor has no reading", "NotReadableError");

        var matrix = ToRotationMatrix(quaternion[0], quaternion[1], quaternion[2], quaternion[3]);
        switch (targetMatrix)
        {
            case JsBufferSource { Kind: JsBufferKind.Float32Array } buffer:
                for (var i = 0; i < matrix.Length; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(buffer.Bytes.Span[(i * sizeof(float))..], (float)matrix[i]);
                }

                break;

            case JsBufferSource buffer:
                for (var i = 0; i < matrix.Length; i++)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(buffer.Bytes.Span[(i * sizeof(double))..], matrix[i]);
                }

                break;

            case float[] array:
                for (var i = 0; i < matrix.Length; i++)
                {
                    array[i] = (float)matrix[i];
                }

                break;

            case double[] array:
                matrix.CopyTo(array, 0);
                break;

            case DomMatrix domMatrix:
                domMatrix.M11 = matrix[0];
                domMatrix.M12 = matrix[1];
                domMatrix.M13 = matrix[2];
                domMatrix.M14 = matrix[3];
                domMatrix.M21 = matrix[4];
                domMatrix.M22 = matrix[5];
                domMatrix.M23 = matrix[6];
                domMatrix.M24 = matrix[7];
                domMatrix.M31 = matrix[8];
                domMatrix.M32 = matrix[9];
                domMatrix.M33 = matrix[10];
                domMatrix.M34 = matrix[11];
                domMatrix.M41 = matrix[12];
                domMatrix.M42 = matrix[13];
                domMatrix.M43 = matrix[14];
                domMatrix.M44 = matrix[15];
                break;
        }
    }

    /// <summary>https://w3c.github.io/orientation-sensor/#convert-quaternion-to-rotation-matrix</summary>
    private static double[] ToRotationMatrix(double x, double y, double z, double w) =>
    [
        1 - (2 * y * y) - (2 * z * z), (2 * x * y) - (2 * z * w), (2 * x * z) + (2 * y * w), 0,
        (2 * x * y) + (2 * z * w), 1 - (2 * x * x) - (2 * z * z), (2 * y * z) - (2 * x * w), 0,
        (2 * x * z) - (2 * y * w), (2 * y * z) + (2 * x * w), 1 - (2 * x * x) - (2 * y * y), 0,
        0, 0, 0, 1,
    ];
}

/// <summary>
/// The absolute orientation sensor (https://w3c.github.io/orientation-sensor/#absoluteorientationsensor-interface): the
/// orientation of the device relative to the Earth. It reads the <see cref="DeviceSensorType.AbsoluteOrientation"/> sensor of
/// the device, and needs the <c>accelerometer</c>, <c>gyroscope</c> and <c>magnetometer</c> permissions and features.
/// </summary>
/// <seealso href="https://w3c.github.io/orientation-sensor/#absoluteorientationsensor"/>
[IdlInterface("AbsoluteOrientationSensor", "https://w3c.github.io/orientation-sensor/#absoluteorientationsensor")]
public sealed class AbsoluteOrientationSensor : OrientationSensor
{
    /// <summary>Creates an absolute orientation sensor.</summary>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-absoluteorientationsensor-absoluteorientationsensor"/>
    [IdlMember("constructor", "https://w3c.github.io/orientation-sensor/#dom-absoluteorientationsensor-absoluteorientationsensor")]
    public AbsoluteOrientationSensor(OrientationSensorOptions? sensorOptions = null)
        : base(SensorKind.AbsoluteOrientation, sensorOptions)
    {
    }
}

/// <summary>
/// The relative orientation sensor (https://w3c.github.io/orientation-sensor/#relativeorientationsensor-interface): the
/// orientation of the device relative to a stationary frame. It reads the <see cref="DeviceSensorType.RelativeOrientation"/>
/// sensor of the device, and needs the <c>accelerometer</c> and <c>gyroscope</c> permissions and features.
/// </summary>
/// <seealso href="https://w3c.github.io/orientation-sensor/#relativeorientationsensor"/>
[IdlInterface("RelativeOrientationSensor", "https://w3c.github.io/orientation-sensor/#relativeorientationsensor")]
public sealed class RelativeOrientationSensor : OrientationSensor
{
    /// <summary>Creates a relative orientation sensor.</summary>
    /// <seealso href="https://w3c.github.io/orientation-sensor/#dom-relativeorientationsensor-relativeorientationsensor"/>
    [IdlMember("constructor", "https://w3c.github.io/orientation-sensor/#dom-relativeorientationsensor-relativeorientationsensor")]
    public RelativeOrientationSensor(OrientationSensorOptions? sensorOptions = null)
        : base(SensorKind.RelativeOrientation, sensorOptions)
    {
    }
}
