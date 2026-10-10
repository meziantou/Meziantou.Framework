using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The raw delay field of a frame as read from the encoded input: APNG <c>fcTL</c> <c>delay_num</c>/<c>delay_den</c>
/// (a zero denominator means 100), the GIF Graphic Control Extension delay in hundredths of a second, the WebP <c>ANMF</c>
/// frame duration in milliseconds, or the sample delta of an AVIF image sequence with the time scale of its media. It lets
/// tests check timing conversions against the encoded data rather than against any tool's normalized playback timestamps.
/// </summary>
public sealed class EncodedDelayExpectation
{
    /// <summary>Gets the APNG <c>delay_num</c> field, or <see langword="null"/> for GIF.</summary>
    [JsonPropertyName("numerator")]
    public int? Numerator { get; init; }

    /// <summary>Gets the APNG <c>delay_den</c> field as encoded (0 means 100), or <see langword="null"/> for GIF.</summary>
    [JsonPropertyName("denominator")]
    public int? Denominator { get; init; }

    /// <summary>Gets the GIF delay in hundredths of a second, or <see langword="null"/> for APNG.</summary>
    [JsonPropertyName("hundredths")]
    public int? Hundredths { get; init; }

    /// <summary>Gets the WebP <c>ANMF</c> frame duration in milliseconds (24-bit field), or <see langword="null"/> for APNG and GIF.</summary>
    [JsonPropertyName("milliseconds")]
    public int? Milliseconds { get; init; }

    /// <summary>Gets the duration of the sample of an AVIF image sequence (<c>stts</c> sample delta), in units of <see cref="Timescale"/>.</summary>
    [JsonPropertyName("sampleDelta")]
    public long? SampleDelta { get; init; }

    /// <summary>Gets the number of time units of the media of an AVIF image sequence in a second (<c>mdhd</c> time scale).</summary>
    [JsonPropertyName("timescale")]
    public long? Timescale { get; init; }

    /// <summary>Gets a value indicating whether this is an APNG <c>fcTL</c> delay.</summary>
    [JsonIgnore]
    public bool IsApng => Numerator is not null && Denominator is not null && Hundredths is null && Milliseconds is null && SampleDelta is null && Timescale is null;

    /// <summary>Gets a value indicating whether this is a GIF delay.</summary>
    [JsonIgnore]
    public bool IsGif => Hundredths is not null && Numerator is null && Denominator is null && Milliseconds is null && SampleDelta is null && Timescale is null;

    /// <summary>Gets a value indicating whether this is a WebP <c>ANMF</c> duration.</summary>
    [JsonIgnore]
    public bool IsWebP => Milliseconds is not null && Numerator is null && Denominator is null && Hundredths is null && SampleDelta is null && Timescale is null;

    /// <summary>Gets a value indicating whether this is the duration of a sample of an AVIF image sequence.</summary>
    [JsonIgnore]
    public bool IsAvif => SampleDelta is not null && Timescale is not null && Numerator is null && Denominator is null && Hundredths is null && Milliseconds is null;

    /// <inheritdoc/>
    public override string ToString() => IsGif
        ? string.Create(CultureInfo.InvariantCulture, $"{Hundredths} hundredths")
        : IsWebP
            ? string.Create(CultureInfo.InvariantCulture, $"{Milliseconds} ms (ANMF)")
            : IsAvif
                ? string.Create(CultureInfo.InvariantCulture, $"{SampleDelta}/{Timescale} (stts/mdhd)")
                : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator} (fcTL)");
}
