using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The constraint processing of Media Capture and Streams: the constraints of a page (the JSON of a MediaTrackConstraints
// dictionary, converted as the WebIDL bindings do) are applied to a camera and to a microphone by the SelectSettings
// algorithm
public static partial class WebPlatformFuzzTargets
{
    private const int MediaConstraintsMaxLength = 4096;

    private static readonly MediaCaptureDevice FuzzCamera = new()
    {
        Id = "camera",
        Kind = MediaDeviceKind.VideoInput,
        Capabilities = new MediaTrackCapabilities { FacingMode = ["user", "environment"], Zoom = new MediaSettingsRange { Min = 1, Max = 10, Step = 0.5 }, Torch = [false, true] },
        DefaultSettings = new MediaTrackSettings { Width = 640, Height = 480, FrameRate = 30, FacingMode = "user", Zoom = 1, Torch = false },
        VideoModes = [new MediaCaptureVideoMode(640, 480, 30), new MediaCaptureVideoMode(1280, 720, 60), new MediaCaptureVideoMode(320, 240, 15)],
    };

    private static readonly MediaCaptureDevice FuzzMicrophone = new()
    {
        Id = "microphone",
        Kind = MediaDeviceKind.AudioInput,
        Capabilities = new MediaTrackCapabilities
        {
            SampleRate = new ULongRange { Min = 8000, Max = 48000 },
            ChannelCount = new ULongRange { Min = 1, Max = 2 },
            Latency = new DoubleRange { Min = 0.01, Max = 0.1 },
            EchoCancellation = [true, false, "all", "remote-only"],
            AutoGainControl = [true, false],
        },
        DefaultSettings = new MediaTrackSettings { SampleRate = 48000, ChannelCount = 1, Latency = 0.01, EchoCancellation = true, AutoGainControl = true },
    };

    /// <summary>The tokens of the media-constraints target: the names of the constrainable properties and of the members of the constraints.</summary>
    private static string[] MediaConstraintsTokens =>
    [
        "\"width\"", "\"height\"", "\"aspectRatio\"", "\"frameRate\"", "\"facingMode\"", "\"resizeMode\"", "\"sampleRate\"", "\"channelCount\"",
        "\"latency\"", "\"echoCancellation\"", "\"autoGainControl\"", "\"deviceId\"", "\"groupId\"", "\"zoom\"", "\"torch\"", "\"pan\"",
        "\"min\"", "\"max\"", "\"exact\"", "\"ideal\"", "\"advanced\"", "\"none\"", "\"crop-and-scale\"", "\"user\"", "\"environment\"",
        "\"all\"", "\"remote-only\"", "true", "false", "{", "}", "[", "]", ":", ",",
    ];

    /// <summary>
    /// Applies the constraints of the input (a JSON object) to a camera and to a microphone: the selected settings satisfy the
    /// required constraints of the basic constraint set and are in the capabilities of the device, the fitness distance is a
    /// finite non-negative number, a failure names a required constraint of the basic set (or none), and the result does not
    /// depend on the run.
    /// </summary>
    public static void MediaConstraints(ReadOnlySpan<byte> data)
    {
        MediaTrackConstraints constraints;
        try
        {
            using var document = JsonDocument.Parse(data.Length > MediaConstraintsMaxLength ? data[..MediaConstraintsMaxLength].ToArray() : data.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;

            constraints = ToConstraints(document.RootElement, allowAdvanced: true);
        }
        catch (JsonException)
        {
            return;
        }

        foreach (var device in (ReadOnlySpan<MediaCaptureDevice>)[FuzzCamera, FuzzMicrophone])
        {
            var kind = device.Kind == MediaDeviceKind.AudioInput ? MediaTrackKinds.Audio : MediaTrackKinds.Video;
            var model = MediaTrackSourceModel.Create(kind, device.Capabilities, device.DefaultSettings, device.VideoModes);
            var fixedValues = new Dictionary<string, object>(StringComparer.Ordinal) { ["deviceId"] = device.Id, ["groupId"] = "group" };
            var result = MediaTrackConstraintProcessing.SelectSettings(model, constraints, fixedValues);
            var again = MediaTrackConstraintProcessing.SelectSettings(model, constraints, fixedValues);
            FuzzAssert.Equal(Describe(result.Settings), Describe(again.Settings), "SelectSettings is not deterministic");

            var basic = MediaTrackConstraintProcessing.Normalize(constraints, bareValuesAreExact: false);
            if (result.Settings is not { } settings)
            {
                FuzzAssert.True(result.FailedConstraint.Length == 0 || basic.Exists(pair => pair.Key == result.FailedConstraint && pair.Value.IsRequired), $"The failed constraint '{result.FailedConstraint}' is not a required constraint of the basic set");
                continue;
            }

            FuzzAssert.True(double.IsFinite(result.FitnessDistance) && result.FitnessDistance >= 0, $"The fitness distance {result.FitnessDistance.ToString(CultureInfo.InvariantCulture)} is not a finite non-negative number");
            foreach (var (name, constraint) in basic)
            {
                FuzzAssert.True(MediaTrackConstraintProcessing.Satisfies(settings, name, constraint), $"The selected settings do not satisfy the required constraint on '{name}'");
            }

            // The selected values are in the capabilities of the device
            var capabilities = model.CreateCapabilities(device.Id, "group");
            foreach (var property in MediaTrackConstrainableProperty.All)
            {
                if (!settings.TryGetValue(property.Name, out var value))
                    continue;

                switch (property.GetCapability(capabilities))
                {
                    case ULongRange range when value is double number:
                        FuzzAssert.True(number >= (range.Min ?? 0) && number <= (range.Max ?? uint.MaxValue) && number == Math.Floor(number), $"The setting {property.Name}={number.ToString(CultureInfo.InvariantCulture)} is not an integer of its range");
                        break;
                    case DoubleRange range when value is double number:
                        FuzzAssert.True(number >= (range.Min ?? 0) - 1e-9 && number <= (range.Max ?? double.MaxValue) + 1e-9, $"The setting {property.Name}={number.ToString(CultureInfo.InvariantCulture)} is out of its range");
                        break;
                    case MediaSettingsRange range when value is double number:
                        FuzzAssert.True(number >= (range.Min ?? 0) && number <= (range.Max ?? double.MaxValue), $"The setting {property.Name}={number.ToString(CultureInfo.InvariantCulture)} is out of its range");
                        break;
                    case IReadOnlyList<string> list:
                        FuzzAssert.True(value is string text && list.Contains(text, StringComparer.Ordinal), $"The setting {property.Name} is not one of its capabilities");
                        break;
                }
            }

            // The settings of a video mode are consistent: the aspect ratio is the ratio of the size
            if (settings.TryGetValue("width", out var width) && settings.TryGetValue("height", out var height) && settings.TryGetValue("aspectRatio", out var aspectRatio))
            {
                FuzzAssert.True(Math.Abs(((double)width / (double)height) - (double)aspectRatio) < 1e-9, "The aspect ratio is not the ratio of the width and the height");
            }
        }
    }

    private static string Describe(Dictionary<string, object>? settings) => settings is null
        ? "null"
        : string.Join(";", settings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + Convert.ToString(pair.Value, CultureInfo.InvariantCulture)));

    /// <summary>Converts a JSON object to the constraints, as the WebIDL bindings convert a JavaScript object (the unknown members and the values of the wrong type are ignored).</summary>
    private static MediaTrackConstraints ToConstraints(JsonElement element, bool allowAdvanced)
    {
        ConstrainULong? ReadULong(string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.Number => (ConstrainULong?)ClampULong(value.GetDouble()),
            JsonValueKind.Object => new ConstrainULongRange { Min = OptionalULong(value, "min"), Max = OptionalULong(value, "max"), Exact = OptionalULong(value, "exact"), Ideal = OptionalULong(value, "ideal") },
            _ => null,
        } : null;

        ConstrainDouble? ReadDouble(string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.Number => (ConstrainDouble?)value.GetDouble(),
            JsonValueKind.Object => new ConstrainDoubleRange { Min = OptionalDouble(value, "min"), Max = OptionalDouble(value, "max"), Exact = OptionalDouble(value, "exact"), Ideal = OptionalDouble(value, "ideal") },
            _ => null,
        } : null;

        ConstrainBoolean? ReadBoolean(string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => (ConstrainBoolean?)value.GetBoolean(),
            JsonValueKind.Object => new ConstrainBooleanParameters { Exact = OptionalBoolean(value, "exact"), Ideal = OptionalBoolean(value, "ideal") },
            _ => null,
        } : null;

        ConstrainDomString? ReadString(string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => (ConstrainDomString?)value.GetString()!,
            JsonValueKind.Array => (ConstrainDomString?)value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray(),
            JsonValueKind.Object => new ConstrainDomStringParameters { Exact = Strings(value, "exact"), Ideal = Strings(value, "ideal") },
            _ => null,
        } : null;

        ConstrainBooleanOrDomString? ReadBooleanOrString(string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => (ConstrainBooleanOrDomString?)value.GetBoolean(),
            JsonValueKind.String => (ConstrainBooleanOrDomString?)value.GetString()!,
            JsonValueKind.Object => new ConstrainBooleanOrDomStringParameters { Exact = OptionalBooleanOrString(value, "exact"), Ideal = OptionalBooleanOrString(value, "ideal") },
            _ => null,
        } : null;

        BooleanOrConstrainDouble? ReadBooleanOrDouble(string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => (BooleanOrConstrainDouble?)value.GetBoolean(),
            JsonValueKind.Number => (BooleanOrConstrainDouble)value.GetDouble(),
            JsonValueKind.Object => new ConstrainDoubleRange { Min = OptionalDouble(value, "min"), Max = OptionalDouble(value, "max"), Exact = OptionalDouble(value, "exact"), Ideal = OptionalDouble(value, "ideal") },
            _ => null,
        } : null;

        return new MediaTrackConstraints
        {
            Width = ReadULong("width"),
            Height = ReadULong("height"),
            AspectRatio = ReadDouble("aspectRatio"),
            FrameRate = ReadDouble("frameRate"),
            FacingMode = ReadString("facingMode"),
            ResizeMode = ReadString("resizeMode"),
            SampleRate = ReadULong("sampleRate"),
            ChannelCount = ReadULong("channelCount"),
            Latency = ReadDouble("latency"),
            EchoCancellation = ReadBooleanOrString("echoCancellation"),
            AutoGainControl = ReadBoolean("autoGainControl"),
            DeviceId = ReadString("deviceId"),
            GroupId = ReadString("groupId"),
            Zoom = ReadBooleanOrDouble("zoom"),
            Pan = ReadBooleanOrDouble("pan"),
            Torch = ReadBoolean("torch"),
            Advanced = allowAdvanced && element.TryGetProperty("advanced", out var advanced) && advanced.ValueKind == JsonValueKind.Array
                ? [.. advanced.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => (MediaTrackConstraintSet)ToConstraints(e, allowAdvanced: false))]
                : null,
        };

        static uint ClampULong(double value) => double.IsNaN(value) ? 0 : (uint)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), 0, uint.MaxValue);

        static uint? OptionalULong(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? ClampULong(value.GetDouble()) : null;

        static double? OptionalDouble(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

        static bool? OptionalBoolean(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

        static BooleanOrString? OptionalBooleanOrString(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => (BooleanOrString?)value.GetBoolean(),
            JsonValueKind.String => (BooleanOrString)value.GetString()!,
            _ => null,
        } : null;

        static StringOrStringList? Strings(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => (StringOrStringList?)value.GetString()!,
            JsonValueKind.Array => (StringOrStringList?)value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray(),
            _ => null,
        } : null;
    }

    /// <summary>The seeds of the media-constraints target: the constraints of the unit tests and of the web-platform-tests.</summary>
    private static IEnumerable<FuzzSeed> GetMediaConstraintsSeeds()
    {
        string[] seeds =
        [
            "{}",
            """{"width":{"min":1000}}""",
            """{"width":320}""",
            """{"width":{"min":4000}}""",
            """{"width":{"exact":639},"resizeMode":{"exact":"none"}}""",
            """{"width":{"min":100,"max":10}}""",
            """{"frameRate":{"max":0}}""",
            """{"aspectRatio":{"exact":1.7777777777777777}}""",
            """{"facingMode":{"exact":"invalid"}}""",
            """{"facingMode":["environment","user"]}""",
            """{"height":{"ideal":240},"advanced":[{"width":1280},{"frameRate":60}]}""",
            """{"echoCancellation":{"exact":"all"}}""",
            """{"echoCancellation":false,"autoGainControl":{"exact":true}}""",
            """{"sampleRate":{"ideal":16000},"channelCount":2,"latency":{"max":0.05}}""",
            """{"deviceId":{"exact":"camera"},"groupId":"group"}""",
            """{"zoom":true,"advanced":[{"pan":true},{"torch":true},{"zoom":{"exact":2.5}}]}""",
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("media-constraints-" + i.ToString(CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(seeds[i]));
        }
    }
}
