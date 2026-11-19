using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The constrainable properties of a source of tracks, as seen by the constraint processing: the capabilities of the
/// properties, their default values, and the native video modes of a camera or a screen.
/// </summary>
internal sealed class MediaTrackSourceModel
{
    /// <summary>The kind of the tracks of the source.</summary>
    public required MediaTrackKinds Kind { get; init; }

    /// <summary>The capabilities of the properties, by name: the members of a <see cref="MediaTrackCapabilities"/> (ranges and lists).</summary>
    public required IReadOnlyDictionary<string, object> Capabilities { get; init; }

    /// <summary>The default settings of the properties, by name (see <see cref="MediaTrackConstrainableProperty.GetSettingValues"/>).</summary>
    public required IReadOnlyDictionary<string, object> Defaults { get; init; }

    /// <summary>The native video modes of the source, or an empty list when the size and the frame rate are not constrainable.</summary>
    public IReadOnlyList<MediaCaptureVideoMode> VideoModes { get; init; } = [];

    /// <summary>Creates the model of a source from its capabilities, its default settings and its video modes (whose sizes and frame rates replace the corresponding capabilities).</summary>
    public static MediaTrackSourceModel Create(MediaTrackKinds kind, MediaTrackCapabilities capabilities, MediaTrackSettings defaults, IReadOnlyList<MediaCaptureVideoMode> videoModes)
    {
        var capabilityValues = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in MediaTrackConstrainableProperty.All)
        {
            if (property.GetCapability(capabilities) is { } capability)
            {
                capabilityValues[property.Name] = capability;
            }
        }

        var defaultValues = MediaTrackConstrainableProperty.GetSettingValues(defaults);
        if (videoModes.Count > 0)
        {
            var maxWidth = videoModes.Max(m => m.Width);
            var maxHeight = videoModes.Max(m => m.Height);
            var maxFrameRate = videoModes.Max(m => m.FrameRate);
            var resizeModes = capabilities.ResizeMode ?? ["none", "crop-and-scale"];
            if (resizeModes.Contains("crop-and-scale", StringComparer.Ordinal))
            {
                capabilityValues["width"] = new ULongRange { Min = 1, Max = (uint)maxWidth };
                capabilityValues["height"] = new ULongRange { Min = 1, Max = (uint)maxHeight };
                capabilityValues["aspectRatio"] = new DoubleRange { Min = 1d / maxHeight, Max = maxWidth };
                capabilityValues["frameRate"] = new DoubleRange { Min = 1, Max = maxFrameRate };
            }
            else
            {
                capabilityValues["width"] = new ULongRange { Min = (uint)videoModes.Min(m => m.Width), Max = (uint)maxWidth };
                capabilityValues["height"] = new ULongRange { Min = (uint)videoModes.Min(m => m.Height), Max = (uint)maxHeight };
                capabilityValues["aspectRatio"] = new DoubleRange { Min = videoModes.Min(m => (double)m.Width / m.Height), Max = videoModes.Max(m => (double)m.Width / m.Height) };
                capabilityValues["frameRate"] = new DoubleRange { Min = videoModes.Min(m => m.FrameRate), Max = maxFrameRate };
            }

            capabilityValues["resizeMode"] = resizeModes;

            // The default video mode: the one of the default settings, else the first mode
            var defaultMode = videoModes[0];
            if (defaultValues.TryGetValue("width", out var w) && defaultValues.TryGetValue("height", out var h))
            {
                defaultMode = videoModes.FirstOrDefault(m => m.Width == (double)w && m.Height == (double)h) ?? defaultMode;
            }

            defaultValues.TryAdd("width", (double)defaultMode.Width);
            defaultValues.TryAdd("height", (double)defaultMode.Height);
            defaultValues.TryAdd("frameRate", defaultMode.FrameRate);
            defaultValues.TryAdd("resizeMode", "none");
            defaultValues["aspectRatio"] = (double)defaultValues["width"] / (double)defaultValues["height"];
        }

        return new MediaTrackSourceModel
        {
            Kind = kind,
            Capabilities = capabilityValues,
            Defaults = defaultValues,
            VideoModes = videoModes,
        };
    }

    /// <summary>The capabilities dictionary of the source (with the given device and group identifiers).</summary>
    public MediaTrackCapabilities CreateCapabilities(string? deviceId, string? groupId)
    {
        var values = new Dictionary<string, object>(Capabilities, StringComparer.Ordinal);
        if (deviceId is not null)
        {
            values["deviceId"] = deviceId;
        }

        if (groupId is not null)
        {
            values["groupId"] = groupId;
        }

        return MediaTrackConstrainableProperty.CreateCapabilities(values);
    }
}

/// <summary>
/// The constraint processing of Media Capture and Streams: the fitness distance
/// (https://w3c.github.io/mediacapture-main/#dfn-fitness-distance) and the SelectSettings algorithm
/// (https://w3c.github.io/mediacapture-main/#dfn-selectsettings).
/// </summary>
/// <remarks>
/// The settings dictionaries of a source are finite: the candidate values of a property are its default value, the bounds
/// of its range, and the values of the constraints clamped to the range (or the values of a list). The properties are
/// independent, except the properties of the video modes (<c>width</c>, <c>height</c>, <c>aspectRatio</c>,
/// <c>frameRate</c> and <c>resizeMode</c>), whose candidates are tuples: the native modes, and the modes cropped and
/// scaled down from them. As the fitness distance is a sum over the properties, and the constraint sets are conjunctions
/// of constraints on the properties, the algorithm runs on each group of dependent properties separately, which gives the
/// result of the algorithm on the Cartesian product of the groups.
/// </remarks>
internal static class MediaTrackConstraintProcessing
{
    private const double Epsilon = 1e-9;

    /// <summary>The maximum length of the value of a <c>deviceId</c> or <c>groupId</c> constraint (as in the browsers).</summary>
    private const int MaxIdentifierLength = 500;

    /// <summary>The result of <see cref="SelectSettings"/>: the settings, or the name of a constraint that cannot be satisfied.</summary>
    internal readonly record struct SelectionResult(Dictionary<string, object>? Settings, double FitnessDistance, string FailedConstraint)
    {
        public bool Succeeded => Settings is not null;
    }

    /// <summary>
    /// https://w3c.github.io/mediacapture-main/#dfn-selectsettings: the settings dictionary of the source that satisfies the
    /// required constraints of the basic constraint set and as many advanced constraint sets as possible, with the smallest
    /// fitness distance; when several have the same distance, the one closest to the default settings.
    /// </summary>
    /// <param name="model">The source.</param>
    /// <param name="constraints">The constraints (the basic constraint set and the advanced constraint sets).</param>
    /// <param name="fixedValues">The values of the properties that the constraints cannot change (e.g. the <c>deviceId</c> and the <c>groupId</c>).</param>
    public static SelectionResult SelectSettings(MediaTrackSourceModel model, MediaTrackConstraints constraints, IReadOnlyDictionary<string, object>? fixedValues = null)
    {
        var basic = Normalize(constraints, bareValuesAreExact: false);
        var advancedSets = constraints.Advanced?.Select(set => Normalize(set, bareValuesAreExact: true)).ToList() ?? [];

        // As in the browsers, an identifier longer than 500 characters cannot be satisfied
        foreach (var (name, constraint) in basic)
        {
            if (name is "deviceId" or "groupId" && (constraint.ExactValues ?? []).Concat(constraint.IdealValues ?? []).Any(value => value is string { Length: > MaxIdentifierLength }))
                return new SelectionResult(null, double.PositiveInfinity, name);
        }

        var groups = CreateGroups(model, fixedValues, [basic, .. advancedSets]);

        // The constraints on the properties that the source does not have: a required constraint cannot be satisfied
        foreach (var (name, constraint) in basic)
        {
            if (constraint.IsRequired && !groups.Exists(g => g.Properties.Contains(name)))
                return new SelectionResult(null, double.PositiveInfinity, name);
        }

        // Step 3: the settings dictionaries that satisfy the required constraints of the basic set
        foreach (var group in groups)
        {
            var remaining = group.Candidates.FindAll(candidate => Satisfies(candidate, basic, group.Properties));
            if (remaining.Count == 0)
                return new SelectionResult(null, double.PositiveInfinity, FindFailedConstraint(group, basic));

            group.Candidates = remaining;
        }

        // Step 4: the advanced constraint sets that can be satisfied, in order
        foreach (var set in advancedSets)
        {
            if (set.Any(pair => !groups.Exists(g => g.Properties.Contains(pair.Key)) && pair.Value.IsRequired))
                continue;

            var filtered = new List<List<Dictionary<string, object>>>(groups.Count);
            foreach (var group in groups)
            {
                var remaining = group.Candidates.FindAll(candidate => Satisfies(candidate, set, group.Properties));
                if (remaining.Count == 0)
                    break;

                filtered.Add(remaining);
            }

            if (filtered.Count != groups.Count)
                continue;

            for (var i = 0; i < groups.Count; i++)
            {
                groups[i].Candidates = filtered[i];
            }
        }

        // Step 5: the candidate with the smallest fitness distance, then the closest to the default settings
        var settings = new Dictionary<string, object>(StringComparer.Ordinal);
        var fitness = 0d;
        foreach (var group in groups)
        {
            Dictionary<string, object>? best = null;
            var bestFitness = double.PositiveInfinity;
            var bestPreference = double.PositiveInfinity;
            foreach (var candidate in group.Candidates)
            {
                var candidateFitness = GetIdealFitness(model.Kind, candidate, basic, group.Properties);
                var preference = group.GetPreference(candidate);
                if (candidateFitness < bestFitness - Epsilon || (Math.Abs(candidateFitness - bestFitness) <= Epsilon && preference < bestPreference))
                {
                    best = candidate;
                    bestFitness = candidateFitness;
                    bestPreference = preference;
                }
            }

            fitness += bestFitness;
            foreach (var (name, value) in best!)
            {
                settings[name] = value;
            }
        }

        // The ideal values of the constraints on the properties that the source does not have
        foreach (var (name, constraint) in basic)
        {
            if (!groups.Exists(g => g.Properties.Contains(name)))
            {
                fitness += GetIdealFitness(model.Kind, name, constraint, value: null);
            }
        }

        return new SelectionResult(settings, fitness, "");
    }

    /// <summary>
    /// The name of a required constraint of the basic constraint set that no settings dictionary of the source satisfies
    /// (https://w3c.github.io/mediacapture-main/#dfn-selectsettings), or the empty string when the constraints only conflict
    /// with each other.
    /// </summary>
    public static string FindFailedConstraint(MediaTrackSourceModel model, MediaTrackConstraints constraints, IReadOnlyDictionary<string, object>? fixedValues = null)
    {
        var basic = Normalize(constraints, bareValuesAreExact: false);
        var groups = CreateGroups(model, fixedValues, [basic]);
        foreach (var (name, constraint) in basic)
        {
            if (!constraint.IsRequired)
                continue;

            if (groups.Find(g => g.Properties.Contains(name)) is not { } group || !group.Candidates.Exists(candidate => Satisfies(candidate, name, constraint)))
                return name;
        }

        foreach (var group in groups)
        {
            if (!group.Candidates.Exists(candidate => Satisfies(candidate, basic, group.Properties)))
                return FindFailedConstraint(group, basic);
        }

        return "";
    }

    /// <summary>Whether a required constraint of a constraint set is not in the list of the constraints that are allowed to select a device (https://w3c.github.io/mediacapture-main/#dfn-allowed-required-constraints-for-device-selection).</summary>
    public static string? FindDisallowedRequiredConstraint(MediaTrackConstraints constraints)
    {
        foreach (var (name, constraint) in Normalize(constraints, bareValuesAreExact: false))
        {
            if (constraint.IsRequired && MediaTrackConstrainableProperty.Get(name) is { IsAllowedRequiredForDeviceSelection: false })
                return name;
        }

        return null;
    }

    /// <summary>Removes the constraints on the properties that do not apply to a kind of track (e.g. the audio constraints of the <c>video</c> member of <c>getUserMedia()</c>).</summary>
    public static MediaTrackConstraints RemoveConstraintsOfOtherKinds(MediaTrackConstraints constraints, MediaTrackKinds kind)
    {
        // The constraints are kept as they are when all of them apply
        if (!HasConstraintsOfOtherKinds(constraints, kind) && (constraints.Advanced is null || !constraints.Advanced.Any(set => HasConstraintsOfOtherKinds(set, kind))))
            return constraints;

        return new MediaTrackConstraints
        {
            Width = Keep("width", constraints.Width),
            Height = Keep("height", constraints.Height),
            AspectRatio = Keep("aspectRatio", constraints.AspectRatio),
            FrameRate = Keep("frameRate", constraints.FrameRate),
            FacingMode = Keep("facingMode", constraints.FacingMode),
            ResizeMode = Keep("resizeMode", constraints.ResizeMode),
            SampleRate = Keep("sampleRate", constraints.SampleRate),
            SampleSize = Keep("sampleSize", constraints.SampleSize),
            EchoCancellation = Keep("echoCancellation", constraints.EchoCancellation),
            AutoGainControl = Keep("autoGainControl", constraints.AutoGainControl),
            NoiseSuppression = Keep("noiseSuppression", constraints.NoiseSuppression),
            Latency = Keep("latency", constraints.Latency),
            ChannelCount = Keep("channelCount", constraints.ChannelCount),
            DeviceId = constraints.DeviceId,
            GroupId = constraints.GroupId,
            BackgroundBlur = Keep("backgroundBlur", constraints.BackgroundBlur),
            DisplaySurface = Keep("displaySurface", constraints.DisplaySurface),
            LogicalSurface = Keep("logicalSurface", constraints.LogicalSurface),
            Cursor = Keep("cursor", constraints.Cursor),
            RestrictOwnAudio = Keep("restrictOwnAudio", constraints.RestrictOwnAudio),
            SuppressLocalAudioPlayback = Keep("suppressLocalAudioPlayback", constraints.SuppressLocalAudioPlayback),
            WhiteBalanceMode = Keep("whiteBalanceMode", constraints.WhiteBalanceMode),
            ExposureMode = Keep("exposureMode", constraints.ExposureMode),
            FocusMode = Keep("focusMode", constraints.FocusMode),
            PointsOfInterest = Keep("pointsOfInterest", constraints.PointsOfInterest),
            ExposureCompensation = Keep("exposureCompensation", constraints.ExposureCompensation),
            ExposureTime = Keep("exposureTime", constraints.ExposureTime),
            ColorTemperature = Keep("colorTemperature", constraints.ColorTemperature),
            Iso = Keep("iso", constraints.Iso),
            Brightness = Keep("brightness", constraints.Brightness),
            Contrast = Keep("contrast", constraints.Contrast),
            Saturation = Keep("saturation", constraints.Saturation),
            Sharpness = Keep("sharpness", constraints.Sharpness),
            FocusDistance = Keep("focusDistance", constraints.FocusDistance),
            Pan = Keep("pan", constraints.Pan),
            Tilt = Keep("tilt", constraints.Tilt),
            Zoom = Keep("zoom", constraints.Zoom),
            Torch = Keep("torch", constraints.Torch),
            GestureReactions = Keep("gestureReactions", constraints.GestureReactions),
            FaceFraming = Keep("faceFraming", constraints.FaceFraming),
            EyeGazeCorrection = Keep("eyeGazeCorrection", constraints.EyeGazeCorrection),
            VoiceIsolation = Keep("voiceIsolation", constraints.VoiceIsolation),
            HumanFaceDetectionMode = Keep("humanFaceDetectionMode", constraints.HumanFaceDetectionMode),
            BackgroundSegmentationMask = Keep("backgroundSegmentationMask", constraints.BackgroundSegmentationMask),
            Advanced = constraints.Advanced?.Select(set => (MediaTrackConstraintSet)RemoveConstraintsOfOtherKinds(ToConstraints(set), kind)).ToList(),
        };

        T? Keep<T>(string name, T? value)
            where T : struct
            => MediaTrackConstrainableProperty.Get(name) is { } property && (property.Kinds & kind) != MediaTrackKinds.None ? value : null;
    }

    private static bool HasConstraintsOfOtherKinds(MediaTrackConstraintSet set, MediaTrackKinds kind)
    {
        foreach (var property in MediaTrackConstrainableProperty.All)
        {
            if ((property.Kinds & kind) == MediaTrackKinds.None && property.GetConstraint(set) is not null)
                return true;
        }

        return false;
    }

    private static MediaTrackConstraints ToConstraints(MediaTrackConstraintSet set) => set as MediaTrackConstraints ?? new MediaTrackConstraints
    {
        Width = set.Width,
        Height = set.Height,
        AspectRatio = set.AspectRatio,
        FrameRate = set.FrameRate,
        FacingMode = set.FacingMode,
        ResizeMode = set.ResizeMode,
        SampleRate = set.SampleRate,
        SampleSize = set.SampleSize,
        EchoCancellation = set.EchoCancellation,
        AutoGainControl = set.AutoGainControl,
        NoiseSuppression = set.NoiseSuppression,
        Latency = set.Latency,
        ChannelCount = set.ChannelCount,
        DeviceId = set.DeviceId,
        GroupId = set.GroupId,
        BackgroundBlur = set.BackgroundBlur,
        DisplaySurface = set.DisplaySurface,
        LogicalSurface = set.LogicalSurface,
        Cursor = set.Cursor,
        RestrictOwnAudio = set.RestrictOwnAudio,
        SuppressLocalAudioPlayback = set.SuppressLocalAudioPlayback,
        WhiteBalanceMode = set.WhiteBalanceMode,
        ExposureMode = set.ExposureMode,
        FocusMode = set.FocusMode,
        PointsOfInterest = set.PointsOfInterest,
        ExposureCompensation = set.ExposureCompensation,
        ExposureTime = set.ExposureTime,
        ColorTemperature = set.ColorTemperature,
        Iso = set.Iso,
        Brightness = set.Brightness,
        Contrast = set.Contrast,
        Saturation = set.Saturation,
        Sharpness = set.Sharpness,
        FocusDistance = set.FocusDistance,
        Pan = set.Pan,
        Tilt = set.Tilt,
        Zoom = set.Zoom,
        Torch = set.Torch,
        GestureReactions = set.GestureReactions,
        FaceFraming = set.FaceFraming,
        EyeGazeCorrection = set.EyeGazeCorrection,
        VoiceIsolation = set.VoiceIsolation,
        HumanFaceDetectionMode = set.HumanFaceDetectionMode,
        BackgroundSegmentationMask = set.BackgroundSegmentationMask,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Normalized constraints
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A constraint on a property, normalized: a range and an exact value for the numbers, the accepted values for the
    /// strings and the booleans, and the ideal value(s).
    /// </summary>
    internal sealed class Constraint
    {
        public double? Min { get; init; }

        public double? Max { get; init; }

        public double? Exact { get; init; }

        public double? Ideal { get; init; }

        /// <summary>The values one of which is required (strings, booleans or lists of points), or null.</summary>
        public IReadOnlyList<object>? ExactValues { get; init; }

        /// <summary>The ideal values (strings, booleans or lists of points), or null.</summary>
        public IReadOnlyList<object>? IdealValues { get; init; }

        /// <summary>
        /// A boolean given for a property that is not a boolean (e.g. <c>pan: true</c>): whether the property is requested. It
        /// is required in an advanced constraint set (a bare value).
        /// </summary>
        public bool? Presence { get; init; }

        public bool PresenceIsRequired { get; init; }

        public bool IsRequired => Min is not null || Max is not null || Exact is not null || ExactValues is not null || PresenceIsRequired;
    }

    /// <summary>The constraints of a constraint set, by name, in the order of the IDL. An empty list of values is no constraint.</summary>
    internal static List<KeyValuePair<string, Constraint>> Normalize(MediaTrackConstraintSet set, bool bareValuesAreExact)
    {
        var result = new List<KeyValuePair<string, Constraint>>();
        foreach (var property in MediaTrackConstrainableProperty.All)
        {
            if (property.GetConstraint(set) is not { } value)
                continue;

            if (Normalize(value, bareValuesAreExact) is { } constraint)
            {
                result.Add(new(property.Name, constraint));
            }
        }

        return result;
    }

    private static Constraint? Normalize(object value, bool bareValuesAreExact)
    {
        switch (value)
        {
            case ConstrainULong { Bare: { } bare }:
                return Bare(bare);
            case ConstrainULong { Range: { } range }:
                return new Constraint { Min = range.Min, Max = range.Max, Exact = range.Exact, Ideal = range.Ideal };
            case ConstrainDouble { Bare: { } bare }:
                return Bare(bare);
            case ConstrainDouble { Range: { } range }:
                return new Constraint { Min = range.Min, Max = range.Max, Exact = range.Exact, Ideal = range.Ideal };
            case BooleanOrConstrainDouble { Flag: { } flag }:
                return new Constraint { Presence = flag, PresenceIsRequired = bareValuesAreExact };
            case BooleanOrConstrainDouble { Bare: { } bare }:
                return Bare(bare);
            case BooleanOrConstrainDouble { Range: { } range }:
                return new Constraint { Min = range.Min, Max = range.Max, Exact = range.Exact, Ideal = range.Ideal };
            case ConstrainBoolean { Bare: { } bare }:
                return Values([bare]);
            case ConstrainBoolean { Parameters: { } parameters }:
                return new Constraint { ExactValues = parameters.Exact is { } exactBoolean ? [exactBoolean] : null, IdealValues = parameters.Ideal is { } idealBoolean ? [idealBoolean] : null };
            case ConstrainDomString { Bare: { } bare }:
                return Values([bare]);
            case ConstrainDomString { BareList: { } list }:
                return list.Count == 0 ? null : Values([.. list]);
            case ConstrainDomString { Parameters: { } parameters }:
                return new Constraint { ExactValues = ToList(parameters.Exact), IdealValues = ToList(parameters.Ideal) };
            case ConstrainBooleanOrDomString { BareFlag: { } flag }:
                return Values([flag]);
            case ConstrainBooleanOrDomString { BareText: { } text }:
                return Values([text]);
            case ConstrainBooleanOrDomString { Parameters: { } parameters }:
                return new Constraint { ExactValues = parameters.Exact is { } exactValue ? [exactValue.Value] : null, IdealValues = parameters.Ideal is { } idealValue ? [idealValue.Value] : null };
            case ConstrainPoint2D { Bare: { } points }:
                return Values([new PointList(points)]);
            case ConstrainPoint2D { Parameters: { } parameters }:
                return new Constraint { ExactValues = parameters.Exact is { } exactPoints ? [new PointList(exactPoints)] : null, IdealValues = parameters.Ideal is { } idealPoints ? [new PointList(idealPoints)] : null };
            default:
                return null;
        }

        Constraint Bare(double bare) => bareValuesAreExact ? new Constraint { Exact = bare } : new Constraint { Ideal = bare };

        Constraint Values(IReadOnlyList<object> values) => bareValuesAreExact ? new Constraint { ExactValues = values } : new Constraint { IdealValues = values };

        static IReadOnlyList<object>? ToList(StringOrStringList? value) => value switch
        {
            { One: { } one } => [one],
            { Many: { Count: > 0 } many } => [.. many],
            _ => null,
        };
    }

    /// <summary>A list of points, compared by value.</summary>
    internal sealed class PointList(IReadOnlyList<Point2D> points) : IEquatable<PointList>
    {
        public IReadOnlyList<Point2D> Points { get; } = points;

        public bool Equals([NotNullWhen(true)] PointList? other)
        {
            if (other is null || other.Points.Count != Points.Count)
                return false;

            for (var i = 0; i < Points.Count; i++)
            {
                if (Points[i].X != other.Points[i].X || Points[i].Y != other.Points[i].Y)
                    return false;
            }

            return true;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PointList);

        public override int GetHashCode() => Points.Count;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Fitness distance (https://w3c.github.io/mediacapture-main/#dfn-fitness-distance)
    // ----------------------------------------------------------------------------------------------------------------

    private static bool Satisfies(Dictionary<string, object> candidate, List<KeyValuePair<string, Constraint>> set, HashSet<string> properties)
    {
        foreach (var (name, constraint) in set)
        {
            if (properties.Contains(name) && !Satisfies(candidate, name, constraint))
                return false;
        }

        return true;
    }

    /// <summary>Whether the value of a property of a settings dictionary satisfies the required part of a constraint (a missing value satisfies no required constraint).</summary>
    internal static bool Satisfies(IReadOnlyDictionary<string, object> candidate, string name, Constraint constraint)
    {
        if (!constraint.IsRequired)
            return true;

        var exists = candidate.TryGetValue(name, out var value);
        if (constraint.Presence is { } presence)
            return presence == exists;

        if (!exists)
            return false;

        if (value is double number)
        {
            if (constraint.Exact is { } exact && !NearlyEquals(number, exact))
                return false;

            if (constraint.Min is { } min && number < min - (Epsilon * Math.Max(1, Math.Abs(min))))
                return false;

            if (constraint.Max is { } max && number > max + (Epsilon * Math.Max(1, Math.Abs(max))))
                return false;

            return constraint.ExactValues is null;
        }

        if (constraint.Exact is not null || constraint.Min is not null || constraint.Max is not null)
            return false;

        return constraint.ExactValues is null || constraint.ExactValues.Contains(value);
    }

    private static double GetIdealFitness(MediaTrackKinds kind, Dictionary<string, object> candidate, List<KeyValuePair<string, Constraint>> set, HashSet<string> properties)
    {
        var fitness = 0d;
        foreach (var (name, constraint) in set)
        {
            if (properties.Contains(name))
            {
                fitness += GetIdealFitness(kind, name, constraint, candidate.GetValueOrDefault(name));
            }
        }

        return fitness;
    }

    /// <summary>The fitness distance of the ideal part of a constraint whose required part is satisfied.</summary>
    internal static double GetIdealFitness(MediaTrackKinds kind, string name, Constraint constraint, object? value)
    {
        // A constraint that does not apply to this kind of track
        if (MediaTrackConstrainableProperty.Get(name) is { } property && (property.Kinds & kind) == MediaTrackKinds.None)
            return 0;

        if (constraint.Presence is { } presence)
            return presence == (value is not null) ? 0 : 1;

        if (value is null)
            return 1;

        if (constraint.Ideal is { } ideal)
        {
            if (value is not double actual)
                return 1;

            return NearlyEquals(actual, ideal) ? 0 : Math.Abs(actual - ideal) / Math.Max(Math.Abs(actual), Math.Abs(ideal));
        }

        if (constraint.IdealValues is { } idealValues)
            return idealValues.Contains(value) ? 0 : 1;

        return 0;
    }

    private static bool NearlyEquals(double a, double b) => a == b || Math.Abs(a - b) <= Epsilon * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    // ----------------------------------------------------------------------------------------------------------------
    // Candidate settings dictionaries
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A group of dependent properties and their candidate values.</summary>
    private sealed class PropertyGroup(HashSet<string> properties, List<Dictionary<string, object>> candidates, Func<Dictionary<string, object>, double> getPreference)
    {
        public HashSet<string> Properties { get; } = properties;

        public List<Dictionary<string, object>> Candidates { get; set; } = candidates;

        /// <summary>How far a candidate is from the default settings: the smallest value is preferred when the fitness distances are equal.</summary>
        public double GetPreference(Dictionary<string, object> candidate) => getPreference(candidate);
    }

    /// <summary>
    /// The required constraint of a group that cannot be satisfied: one that no candidate satisfies, else the first one without
    /// which the other ones can be satisfied (e.g. <c>width</c> for an exact width that only a scaled mode has, with
    /// <c>resizeMode: none</c>), else the empty string.
    /// </summary>
    private static string FindFailedConstraint(PropertyGroup group, List<KeyValuePair<string, Constraint>> basic)
    {
        var required = basic.FindAll(pair => group.Properties.Contains(pair.Key) && pair.Value.IsRequired);
        foreach (var (name, constraint) in required)
        {
            if (!group.Candidates.Exists(candidate => Satisfies(candidate, name, constraint)))
                return name;
        }

        foreach (var (name, _) in required)
        {
            var others = required.FindAll(pair => pair.Key != name);
            if (group.Candidates.Exists(candidate => others.TrueForAll(pair => Satisfies(candidate, pair.Key, pair.Value))))
                return name;
        }

        return "";
    }

    private static List<PropertyGroup> CreateGroups(MediaTrackSourceModel model, IReadOnlyDictionary<string, object>? fixedValues, List<List<KeyValuePair<string, Constraint>>> sets)
    {
        var groups = new List<PropertyGroup>();
        if (model.VideoModes.Count > 0)
        {
            groups.Add(CreateVideoModeGroup(model, sets));
        }

        var names = new List<string>();
        foreach (var property in MediaTrackConstrainableProperty.All)
        {
            if (model.VideoModes.Count > 0 && property.IsVideoMode)
                continue;

            if (model.Capabilities.ContainsKey(property.Name) || model.Defaults.ContainsKey(property.Name) || (fixedValues?.ContainsKey(property.Name) ?? false))
            {
                names.Add(property.Name);
            }
        }

        foreach (var name in names)
        {
            var property = MediaTrackConstrainableProperty.Get(name)!;
            var values = new List<object>();
            if (fixedValues is not null && fixedValues.TryGetValue(name, out var fixedValue))
            {
                values.Add(fixedValue);
            }
            else
            {
                AddCandidateValues(property, model.Capabilities.GetValueOrDefault(name), model.Defaults.GetValueOrDefault(name), sets, values);
            }

            var defaultValue = model.Defaults.GetValueOrDefault(name) ?? (values.Count > 0 ? values[0] : null);
            var candidates = values.Distinct().Select(value => new Dictionary<string, object>(StringComparer.Ordinal) { [name] = value }).ToList();
            groups.Add(new PropertyGroup([name], candidates, candidate => Distance(candidate[name], defaultValue)));
        }

        return groups;
    }

    private static double Distance(object value, object? reference) => (value, reference) switch
    {
        (double a, double b) => NearlyEquals(a, b) ? 0 : Math.Abs(a - b) / Math.Max(Math.Abs(a), Math.Abs(b)),
        _ => Equals(value, reference) ? 0 : 1,
    };

    private static void AddCandidateValues(MediaTrackConstrainableProperty property, object? capability, object? defaultValue, List<List<KeyValuePair<string, Constraint>>> sets, List<object> values)
    {
        if (defaultValue is not null)
        {
            values.Add(defaultValue);
        }

        switch (capability)
        {
            case ULongRange range:
                AddNumbers(range.Min ?? 0, range.Max ?? uint.MaxValue, isInteger: true);
                break;
            case DoubleRange range:
                AddNumbers(range.Min ?? 0, range.Max ?? double.MaxValue, isInteger: property.ValueType == MediaTrackValueType.Integer);
                break;
            case MediaSettingsRange range:
                AddNumbers(range.Min ?? 0, range.Max ?? double.MaxValue, isInteger: false, range.Step);
                break;
            case IReadOnlyList<string> list:
                values.AddRange(list);
                break;
            case IReadOnlyList<bool> list:
                values.AddRange(list.Cast<object>());
                break;
            case IReadOnlyList<BooleanOrString> list:
                values.AddRange(list.Select(v => v.Value));
                break;
            case string text:
                values.Add(text);
                break;
            case bool boolean:
                values.Add(boolean);
                break;
        }

        // A boolean property without capability (e.g. suppressLocalAudioPlayback) can take both values
        if (capability is null && defaultValue is bool defaultBoolean && property.Name is "restrictOwnAudio" or "suppressLocalAudioPlayback")
        {
            values.Add(!defaultBoolean);
        }

        // A list of points can be any list: the candidates are the points of the constraints
        if (property.ValueType == MediaTrackValueType.Points)
        {
            foreach (var set in sets)
            {
                foreach (var (name, constraint) in set)
                {
                    if (name == property.Name)
                    {
                        values.AddRange(constraint.ExactValues ?? []);
                        values.AddRange(constraint.IdealValues ?? []);
                    }
                }
            }
        }

        void AddNumbers(double min, double max, bool isInteger, double? step = null)
        {
            if (min > max)
                return;

            values.Add(min);
            values.Add(max);
            foreach (var set in sets)
            {
                foreach (var (name, constraint) in set)
                {
                    if (name != property.Name)
                        continue;

                    foreach (var value in (ReadOnlySpan<double?>)[constraint.Exact, constraint.Min, constraint.Max, constraint.Ideal])
                    {
                        if (value is { } number)
                        {
                            AddNumber(number);
                        }
                    }
                }
            }

            void AddNumber(double number)
            {
                if (step is > 0)
                {
                    number = min + (Math.Round((number - min) / step.Value, MidpointRounding.AwayFromZero) * step.Value);
                }

                number = Math.Clamp(number, min, max);
                if (isInteger)
                {
                    values.Add(Math.Clamp(Math.Floor(number), min, max));
                    values.Add(Math.Clamp(Math.Ceiling(number), min, max));
                }
                else
                {
                    values.Add(number);
                }
            }
        }
    }

    /// <summary>
    /// The candidates of the properties of the video modes: the native modes (resize mode <c>none</c>), and when the source
    /// can crop and scale its frames, the sizes and frame rates of the constraints that fit in a native mode (resize mode
    /// <c>crop-and-scale</c>, https://w3c.github.io/mediacapture-main/#def-constraint-resizeMode).
    /// </summary>
    private static PropertyGroup CreateVideoModeGroup(MediaTrackSourceModel model, List<List<KeyValuePair<string, Constraint>>> sets)
    {
        var canCropAndScale = model.Capabilities.GetValueOrDefault("resizeMode") is IReadOnlyList<string> resizeModes && resizeModes.Contains("crop-and-scale", StringComparer.Ordinal);
        var defaultWidth = (double)model.Defaults["width"];
        var defaultHeight = (double)model.Defaults["height"];
        var defaultFrameRate = (double)model.Defaults["frameRate"];

        var widths = new List<double>();
        var heights = new List<double>();
        var aspectRatios = new List<double>();
        var frameRates = new List<double>();
        foreach (var set in sets)
        {
            foreach (var (name, constraint) in set)
            {
                var target = name switch
                {
                    "width" => widths,
                    "height" => heights,
                    "aspectRatio" => aspectRatios,
                    "frameRate" => frameRates,
                    _ => null,
                };
                if (target is null)
                    continue;

                foreach (var value in (ReadOnlySpan<double?>)[constraint.Exact, constraint.Min, constraint.Max, constraint.Ideal])
                {
                    if (value is { } number && double.IsFinite(number) && number > 0)
                    {
                        target.Add(number);
                    }
                }
            }
        }

        var candidates = new List<Dictionary<string, object>>();
        var seen = new HashSet<(double, double, double, bool)>();
        foreach (var mode in model.VideoModes)
        {
            Add(mode.Width, mode.Height, mode.FrameRate, cropAndScale: false);
            if (!canCropAndScale)
                continue;

            var nativeAspectRatio = (double)mode.Width / mode.Height;
            var modeWidths = new HashSet<double> { mode.Width, Math.Min(mode.Width, defaultWidth) };
            var modeHeights = new HashSet<double> { mode.Height, Math.Min(mode.Height, defaultHeight) };
            foreach (var width in widths)
            {
                AddSize(modeWidths, width, mode.Width);
                AddSize(modeHeights, width / nativeAspectRatio, mode.Height);
                foreach (var aspectRatio in aspectRatios)
                {
                    AddSize(modeHeights, width / aspectRatio, mode.Height);
                }
            }

            foreach (var height in heights)
            {
                AddSize(modeHeights, height, mode.Height);
                AddSize(modeWidths, height * nativeAspectRatio, mode.Width);
                foreach (var aspectRatio in aspectRatios)
                {
                    AddSize(modeWidths, height * aspectRatio, mode.Width);
                }
            }

            foreach (var aspectRatio in aspectRatios)
            {
                // The largest size of the aspect ratio in the native mode
                AddSize(modeWidths, Math.Min(mode.Width, mode.Height * aspectRatio), mode.Width);
                AddSize(modeHeights, Math.Min(mode.Height, mode.Width / aspectRatio), mode.Height);
            }

            var modeFrameRates = new HashSet<double> { mode.FrameRate };
            foreach (var frameRate in frameRates)
            {
                modeFrameRates.Add(Math.Clamp(frameRate, 1, mode.FrameRate));
            }

            if (defaultFrameRate < mode.FrameRate)
            {
                modeFrameRates.Add(defaultFrameRate);
            }

            foreach (var width in modeWidths)
            {
                foreach (var height in modeHeights)
                {
                    foreach (var frameRate in modeFrameRates)
                    {
                        Add(width, height, frameRate, cropAndScale: true);
                    }
                }
            }
        }

        var defaultAspectRatio = defaultWidth / defaultHeight;
        return new PropertyGroup(["width", "height", "aspectRatio", "frameRate", "resizeMode"], candidates, candidate =>
        {
            var width = (double)candidate["width"];
            var height = (double)candidate["height"];
            var cropAndScale = (string)candidate["resizeMode"] == "crop-and-scale";

            // The native modes, then the sizes that keep the native aspect ratio, then the closest to the default size and frame rate
            return (4 * Distance(width / height, cropAndScale ? FindNativeAspectRatio(width, height) : defaultAspectRatio))
                + Distance(width, defaultWidth) + Distance(height, defaultHeight) + Distance(candidate["frameRate"], defaultFrameRate) + (cropAndScale ? 10 : 0);
        });

        double FindNativeAspectRatio(double width, double height)
        {
            // The aspect ratio of the smallest native mode that contains the size
            foreach (var mode in model.VideoModes.OrderBy(m => m.Width * m.Height))
            {
                if (mode.Width >= width && mode.Height >= height)
                    return (double)mode.Width / mode.Height;
            }

            return defaultAspectRatio;
        }

        static void AddSize(HashSet<double> sizes, double size, double max)
        {
            foreach (var rounded in (ReadOnlySpan<double>)[Math.Floor(size), Math.Ceiling(size)])
            {
                if (rounded >= 1)
                {
                    sizes.Add(Math.Min(rounded, max));
                }
            }
        }

        void Add(double width, double height, double frameRate, bool cropAndScale)
        {
            if (!seen.Add((width, height, frameRate, cropAndScale)))
                return;

            candidates.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["width"] = width,
                ["height"] = height,
                ["aspectRatio"] = width / height,
                ["frameRate"] = frameRate,
                ["resizeMode"] = cropAndScale ? "crop-and-scale" : "none",
            });
        }
    }
}
