using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Expected animation-wide settings, stored independently of any tool's playback normalization.</summary>
public sealed class AnimationExpectation
{
    /// <summary>Gets the normalized total number of plays including the first; <see langword="null"/> means infinite.</summary>
    [JsonPropertyName("totalPlays")]
    public required int? TotalPlays { get; init; }

    /// <summary>
    /// Gets the raw loop field as encoded: APNG <c>num_plays</c>, GIF NETSCAPE2.0 loop count (repetitions), WebP <c>ANIM</c>
    /// loop count (total plays, 0 is infinite), or
    /// <see langword="null"/> when the input has no loop field.
    /// </summary>
    [JsonPropertyName("encodedLoopValue")]
    public int? EncodedLoopValue { get; init; }
}
