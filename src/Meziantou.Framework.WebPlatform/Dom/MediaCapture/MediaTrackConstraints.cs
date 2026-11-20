namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The constraints of a track (https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraints): the basic constraint set
/// (the members inherited from <see cref="MediaTrackConstraintSet"/>) and the advanced constraint sets, which are applied
/// in order when they can be satisfied.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraints"/>
[IdlInterface("MediaTrackConstraints", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraints")]
public sealed class MediaTrackConstraints : MediaTrackConstraintSet
{
    /// <summary>Gets the advanced constraint sets, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraints-advanced"/>
    [IdlMember("advanced", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraints-advanced")]
    public IReadOnlyList<MediaTrackConstraintSet>? Advanced { get; init; }
}

/// <summary>A range of floating-point values (https://w3c.github.io/mediacapture-main/#dom-doublerange).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-doublerange"/>
[IdlInterface("DoubleRange", "https://w3c.github.io/mediacapture-main/#dom-doublerange")]
public class DoubleRange
{
    /// <summary>Gets the maximum value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-doublerange-max"/>
    [IdlMember("max", "https://w3c.github.io/mediacapture-main/#dom-doublerange-max")]
    public double? Max { get; init; }

    /// <summary>Gets the minimum value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-doublerange-min"/>
    [IdlMember("min", "https://w3c.github.io/mediacapture-main/#dom-doublerange-min")]
    public double? Min { get; init; }
}

/// <summary>A constraint on a floating-point property (https://w3c.github.io/mediacapture-main/#dom-constraindoublerange): a range, an exact value and an ideal value.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constraindoublerange"/>
[IdlInterface("ConstrainDoubleRange", "https://w3c.github.io/mediacapture-main/#dom-constraindoublerange")]
public sealed class ConstrainDoubleRange : DoubleRange
{
    /// <summary>Gets the exact value that is required, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constraindoublerange-exact"/>
    [IdlMember("exact", "https://w3c.github.io/mediacapture-main/#dom-constraindoublerange-exact")]
    public double? Exact { get; init; }

    /// <summary>Gets the ideal value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constraindoublerange-ideal"/>
    [IdlMember("ideal", "https://w3c.github.io/mediacapture-main/#dom-constraindoublerange-ideal")]
    public double? Ideal { get; init; }
}

/// <summary>A range of integer values (https://w3c.github.io/mediacapture-main/#dom-ulongrange).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-ulongrange"/>
[IdlInterface("ULongRange", "https://w3c.github.io/mediacapture-main/#dom-ulongrange")]
public class ULongRange
{
    /// <summary>Gets the maximum value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-ulongrange-max"/>
    [IdlMember("max", "https://w3c.github.io/mediacapture-main/#dom-ulongrange-max")]
    public uint? Max { get; init; }

    /// <summary>Gets the minimum value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-ulongrange-min"/>
    [IdlMember("min", "https://w3c.github.io/mediacapture-main/#dom-ulongrange-min")]
    public uint? Min { get; init; }
}

/// <summary>A constraint on an integer property (https://w3c.github.io/mediacapture-main/#dom-constrainulongrange): a range, an exact value and an ideal value.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainulongrange"/>
[IdlInterface("ConstrainULongRange", "https://w3c.github.io/mediacapture-main/#dom-constrainulongrange")]
public sealed class ConstrainULongRange : ULongRange
{
    /// <summary>Gets the exact value that is required, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainulongrange-exact"/>
    [IdlMember("exact", "https://w3c.github.io/mediacapture-main/#dom-constrainulongrange-exact")]
    public uint? Exact { get; init; }

    /// <summary>Gets the ideal value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainulongrange-ideal"/>
    [IdlMember("ideal", "https://w3c.github.io/mediacapture-main/#dom-constrainulongrange-ideal")]
    public uint? Ideal { get; init; }
}

/// <summary>A constraint on a boolean property (https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters): an exact value and an ideal value.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters"/>
[IdlInterface("ConstrainBooleanParameters", "https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters")]
public sealed class ConstrainBooleanParameters
{
    /// <summary>Gets the exact value that is required, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters-exact"/>
    [IdlMember("exact", "https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters-exact")]
    public bool? Exact { get; init; }

    /// <summary>Gets the ideal value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters-ideal"/>
    [IdlMember("ideal", "https://w3c.github.io/mediacapture-main/#dom-constrainbooleanparameters-ideal")]
    public bool? Ideal { get; init; }
}

/// <summary>A constraint on a string property (https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters): the exact values and the ideal values.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters"/>
[IdlInterface("ConstrainDOMStringParameters", "https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters")]
public sealed class ConstrainDomStringParameters
{
    /// <summary>Gets the value, or the list of values, one of which is required, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters-exact"/>
    [IdlMember("exact", "https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters-exact")]
    public StringOrStringList? Exact { get; init; }

    /// <summary>Gets the ideal value, or the list of ideal values, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters-ideal"/>
    [IdlMember("ideal", "https://w3c.github.io/mediacapture-main/#dom-constraindomstringparameters-ideal")]
    public StringOrStringList? Ideal { get; init; }
}

/// <summary>A constraint on the <c>echoCancellation</c> property (https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters): an exact value and an ideal value.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters"/>
[IdlInterface("ConstrainBooleanOrDOMStringParameters", "https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters")]
public sealed class ConstrainBooleanOrDomStringParameters
{
    /// <summary>Gets the exact value that is required, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters-exact"/>
    [IdlMember("exact", "https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters-exact")]
    public BooleanOrString? Exact { get; init; }

    /// <summary>Gets the ideal value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters-ideal"/>
    [IdlMember("ideal", "https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstringparameters-ideal")]
    public BooleanOrString? Ideal { get; init; }
}

/// <summary>A point in normalized coordinates, from (0, 0) at the top left to (1, 1) at the bottom right (https://w3c.github.io/mediacapture-image/#dictdef-point2d).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dictdef-point2d"/>
[IdlInterface("Point2D", "https://w3c.github.io/mediacapture-image/#dictdef-point2d")]
public sealed class Point2D
{
    /// <summary>Gets the horizontal coordinate.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-point2d-x"/>
    [IdlMember("x", "https://w3c.github.io/mediacapture-image/#dom-point2d-x")]
    public double X { get; init; }

    /// <summary>Gets the vertical coordinate.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-point2d-y"/>
    [IdlMember("y", "https://w3c.github.io/mediacapture-image/#dom-point2d-y")]
    public double Y { get; init; }
}

/// <summary>A constraint on the points of interest of a camera (https://w3c.github.io/mediacapture-image/#dictdef-constrainpoint2dparameters): the exact and the ideal points.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dictdef-constrainpoint2dparameters"/>
[IdlInterface("ConstrainPoint2DParameters", "https://w3c.github.io/mediacapture-image/#dictdef-constrainpoint2dparameters")]
public sealed class ConstrainPoint2DParameters
{
    /// <summary>Gets the points that are required, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-constrainpoint2dparameters-exact"/>
    [IdlMember("exact", "https://w3c.github.io/mediacapture-image/#dom-constrainpoint2dparameters-exact")]
    public IReadOnlyList<Point2D>? Exact { get; init; }

    /// <summary>Gets the ideal points, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-constrainpoint2dparameters-ideal"/>
    [IdlMember("ideal", "https://w3c.github.io/mediacapture-image/#dom-constrainpoint2dparameters-ideal")]
    public IReadOnlyList<Point2D>? Ideal { get; init; }
}

/// <summary>The range of a setting of a camera (https://w3c.github.io/mediacapture-image/#mediasettingsrange): its minimum, its maximum and its step.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-image/#dictdef-mediasettingsrange"/>
[IdlInterface("MediaSettingsRange", "https://w3c.github.io/mediacapture-image/#dictdef-mediasettingsrange")]
public sealed class MediaSettingsRange
{
    /// <summary>Gets the maximum value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediasettingsrange-max"/>
    [IdlMember("max", "https://w3c.github.io/mediacapture-image/#dom-mediasettingsrange-max")]
    public double? Max { get; init; }

    /// <summary>Gets the minimum value, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediasettingsrange-min"/>
    [IdlMember("min", "https://w3c.github.io/mediacapture-image/#dom-mediasettingsrange-min")]
    public double? Min { get; init; }

    /// <summary>Gets the step between two values, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediasettingsrange-step"/>
    [IdlMember("step", "https://w3c.github.io/mediacapture-image/#dom-mediasettingsrange-step")]
    public double? Step { get; init; }
}

/// <summary>The kinds of tracks a constrainable property applies to.</summary>
[Flags]
internal enum MediaTrackKinds
{
    None = 0,
    Audio = 1,
    Video = 2,
}

/// <summary>The type of the values of a constrainable property.</summary>
internal enum MediaTrackValueType
{
    /// <summary>An unsigned integer (<c>unsigned long</c>, e.g. <c>width</c>).</summary>
    Integer,

    /// <summary>A floating-point number (e.g. <c>frameRate</c>).</summary>
    Number,

    /// <summary>A string (e.g. <c>facingMode</c>).</summary>
    String,

    /// <summary>A boolean (e.g. <c>autoGainControl</c>).</summary>
    Boolean,

    /// <summary>A boolean or a string (<c>echoCancellation</c>).</summary>
    BooleanOrString,

    /// <summary>A list of points (<c>pointsOfInterest</c>).</summary>
    Points,
}

internal sealed partial class MediaTrackConstrainableProperty
{
    private MediaTrackConstrainableProperty(string name, MediaTrackKinds kinds, MediaTrackValueType valueType, Func<MediaTrackConstraintSet, object?> getConstraint, Func<MediaTrackCapabilities, object?> getCapability, Func<MediaTrackSettings, object?> getSetting)
    {
        Name = name;
        Kinds = kinds;
        ValueType = valueType;
        GetConstraint = getConstraint;
        GetCapability = getCapability;
        GetSetting = getSetting;
    }

    /// <summary>The name of the property (e.g. <c>width</c>).</summary>
    public string Name { get; }

    /// <summary>The kinds of tracks the property applies to.</summary>
    public MediaTrackKinds Kinds { get; }

    /// <summary>The type of the values of the property.</summary>
    public MediaTrackValueType ValueType { get; }

    /// <summary>Returns the constraint of a constraint set on the property (a union of the constraint types), or null.</summary>
    public Func<MediaTrackConstraintSet, object?> GetConstraint { get; }

    /// <summary>Returns the capability of the property (a range or a list of values), or null.</summary>
    public Func<MediaTrackCapabilities, object?> GetCapability { get; }

    /// <summary>Returns the setting of the property, or null.</summary>
    public Func<MediaTrackSettings, object?> GetSetting { get; }

    /// <summary>
    /// Whether the property is one of the properties of the video modes of a camera (https://w3c.github.io/mediacapture-main/#def-constraint-resizeMode):
    /// their values depend on each other.
    /// </summary>
    public bool IsVideoMode => Name is "width" or "height" or "aspectRatio" or "frameRate" or "resizeMode";

    /// <summary>
    /// Whether a required constraint on the property can be used to select a device in <c>getUserMedia()</c>
    /// (https://w3c.github.io/mediacapture-main/#dfn-allowed-required-constraints-for-device-selection).
    /// </summary>
    public bool IsAllowedRequiredForDeviceSelection => Name is "width" or "height" or "aspectRatio" or "frameRate" or "facingMode" or "resizeMode"
        or "sampleRate" or "sampleSize" or "echoCancellation" or "autoGainControl" or "noiseSuppression" or "latency" or "channelCount" or "deviceId" or "groupId";

    public static MediaTrackConstrainableProperty? Get(string name) => Lookup.ByName.GetValueOrDefault(name);

    /// <summary>The values of the settings of a settings dictionary, by name (the members that exist).</summary>
    public static Dictionary<string, object> GetSettingValues(MediaTrackSettings settings)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in All)
        {
            if (property.GetSetting(settings) is { } value)
            {
                result[property.Name] = value switch
                {
                    uint integer => (double)integer,
                    BooleanOrString union => union.Value,
                    IReadOnlyList<Point2D> points => new MediaTrackConstraintProcessing.PointList(points),
                    _ => value,
                };
            }
        }

        return result;
    }

    private static uint? GetInteger(IReadOnlyDictionary<string, object> values, string name) => values.TryGetValue(name, out var value) && value is double number ? (uint)Math.Clamp(Math.Round(number, MidpointRounding.AwayFromZero), 0, uint.MaxValue) : null;

    private static double? GetNumber(IReadOnlyDictionary<string, object> values, string name) => values.TryGetValue(name, out var value) && value is double number ? number : null;

    private static string? GetString(IReadOnlyDictionary<string, object> values, string name) => values.TryGetValue(name, out var value) ? value as string : null;

    private static bool? GetBoolean(IReadOnlyDictionary<string, object> values, string name) => values.TryGetValue(name, out var value) && value is bool boolean ? boolean : null;

    private static BooleanOrString? GetBooleanOrString(IReadOnlyDictionary<string, object> values, string name) => values.TryGetValue(name, out var value)
        ? value switch
        {
            bool boolean => boolean,
            string text => text,
            _ => null,
        }
        : null;

    private static IReadOnlyList<Point2D>? GetPoints(IReadOnlyDictionary<string, object> values, string name) => values.TryGetValue(name, out var value) && value is MediaTrackConstraintProcessing.PointList list ? list.Points : null;

    // The table is created once All is initialized (the initializers of the static fields of the parts of a class run in an unspecified order)
    private static class Lookup
    {
        public static readonly Dictionary<string, MediaTrackConstrainableProperty> ByName = All.ToDictionary(p => p.Name, StringComparer.Ordinal);
    }
}
