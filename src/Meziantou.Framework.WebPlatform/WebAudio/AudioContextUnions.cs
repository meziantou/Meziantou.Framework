using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The <c>(AudioContextLatencyCategory or double)</c> union of the latency hint of an <see cref="AudioContext"/>
/// (https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-latencyhint): a category, or a latency in seconds.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-latencyhint"/>
[IdlInterface("(AudioContextLatencyCategory or double)", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-latencyhint")]
public readonly struct AudioContextLatencyHint : IUnionValue, IEquatable<AudioContextLatencyHint>
{
    // Null for the default value of the struct: the interactive category
    private readonly object? _value;

    private AudioContextLatencyHint(object value) => _value = value;

    /// <summary>Gets the value: an <see cref="AudioContextLatencyCategory"/> or a <see cref="double"/>.</summary>
    [NotInIdl]
    public object Value => _value ?? AudioContextLatencyCategory.Interactive;

    /// <summary>Gets the category when the value is a category, otherwise null.</summary>
    [NotInIdl]
    public AudioContextLatencyCategory? Category => Value as AudioContextLatencyCategory?;

    /// <summary>Gets the latency in seconds when the value is a number, otherwise null.</summary>
    [NotInIdl]
    public double? Seconds => Value as double?;

    /// <summary>Creates a union value from a category.</summary>
    [NotInIdl]
    public static implicit operator AudioContextLatencyHint(AudioContextLatencyCategory value) => new(value);

    /// <summary>Creates a union value from a latency in seconds.</summary>
    [NotInIdl]
    public static implicit operator AudioContextLatencyHint(double value) => new(value);

    /// <summary>Creates a union value from a category.</summary>
    [NotInIdl]
    public static AudioContextLatencyHint FromAudioContextLatencyCategory(AudioContextLatencyCategory value) => value;

    /// <summary>Creates a union value from a latency in seconds.</summary>
    [NotInIdl]
    public static AudioContextLatencyHint FromDouble(double value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(AudioContextLatencyHint other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is AudioContextLatencyHint other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Returns whether the two values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(AudioContextLatencyHint left, AudioContextLatencyHint right) => left.Equals(right);

    /// <summary>Returns whether the two values are different.</summary>
    [NotInIdl]
    public static bool operator !=(AudioContextLatencyHint left, AudioContextLatencyHint right) => !left.Equals(right);
}

/// <summary>
/// The <c>(DOMString or AudioSinkOptions)</c> union of the audio output device requested for an <see cref="AudioContext"/>
/// (https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-sinkid): the identifier of a device (the empty string
/// for the default device), or the options of a sink without device.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-sinkid"/>
[IdlInterface("(DOMString or AudioSinkOptions)", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-sinkid")]
public readonly struct StringOrAudioSinkOptions : IUnionValue, IEquatable<StringOrAudioSinkOptions>
{
    private StringOrAudioSinkOptions(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or an <see cref="AudioSinkOptions"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the identifier of the device when the value is a string, otherwise null.</summary>
    [NotInIdl]
    public string? DeviceId => Value as string;

    /// <summary>Gets the options when the value is an <see cref="AudioSinkOptions"/>, otherwise null.</summary>
    [NotInIdl]
    public AudioSinkOptions? Options => Value as AudioSinkOptions;

    /// <summary>Converts the identifier of a device to the union.</summary>
    [NotInIdl]
    public static implicit operator StringOrAudioSinkOptions(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts sink options to the union.</summary>
    [NotInIdl]
    public static implicit operator StringOrAudioSinkOptions(AudioSinkOptions value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the identifier of a device.</summary>
    [NotInIdl]
    public static StringOrAudioSinkOptions FromString(string value) => value;

    /// <summary>Creates the union from sink options.</summary>
    [NotInIdl]
    public static StringOrAudioSinkOptions FromAudioSinkOptions(AudioSinkOptions value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(StringOrAudioSinkOptions other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is StringOrAudioSinkOptions other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(StringOrAudioSinkOptions left, StringOrAudioSinkOptions right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(StringOrAudioSinkOptions left, StringOrAudioSinkOptions right) => !left.Equals(right);
}

/// <summary>
/// The <c>(DOMString or AudioSinkInfo)</c> union of the audio output device of an <see cref="AudioContext"/>
/// (https://webaudio.github.io/web-audio-api/#dom-audiocontext-sinkid): the identifier of a device (the empty string for the
/// default device), or the information of a sink without device.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-sinkid"/>
[IdlInterface("(DOMString or AudioSinkInfo)", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-sinkid")]
public readonly struct StringOrAudioSinkInfo : IUnionValue, IEquatable<StringOrAudioSinkInfo>
{
    private StringOrAudioSinkInfo(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or an <see cref="AudioSinkInfo"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the identifier of the device when the value is a string, otherwise null.</summary>
    [NotInIdl]
    public string? DeviceId => Value as string;

    /// <summary>Gets the information of the sink when the value is an <see cref="AudioSinkInfo"/>, otherwise null.</summary>
    [NotInIdl]
    public AudioSinkInfo? Info => Value as AudioSinkInfo;

    /// <summary>Converts the identifier of a device to the union.</summary>
    [NotInIdl]
    public static implicit operator StringOrAudioSinkInfo(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts the information of a sink to the union.</summary>
    [NotInIdl]
    public static implicit operator StringOrAudioSinkInfo(AudioSinkInfo value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the identifier of a device.</summary>
    [NotInIdl]
    public static StringOrAudioSinkInfo FromString(string value) => value;

    /// <summary>Creates the union from the information of a sink.</summary>
    [NotInIdl]
    public static StringOrAudioSinkInfo FromAudioSinkInfo(AudioSinkInfo value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(StringOrAudioSinkInfo other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is StringOrAudioSinkInfo other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(StringOrAudioSinkInfo left, StringOrAudioSinkInfo right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(StringOrAudioSinkInfo left, StringOrAudioSinkInfo right) => !left.Equals(right);
}
