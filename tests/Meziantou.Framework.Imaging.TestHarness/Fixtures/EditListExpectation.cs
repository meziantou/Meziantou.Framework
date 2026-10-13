using System.Numerics;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The raw fields that give the number of plays of an AVIF image sequence (AV1 Image File Format, section 3.2): the edit
/// list of the track of the color images and the duration of its track header.
/// </summary>
public sealed class EditListExpectation
{
    /// <summary>Gets a value indicating whether the edit list repeats (flags and 1 of the <c>elst</c> box).</summary>
    [JsonPropertyName("repeating")]
    public required bool Repeating { get; init; }

    /// <summary>Gets the duration of the edits (their sum) in units of the time scale of the movie; <see langword="null"/> when every bit of the field of one of them is set.</summary>
    [JsonPropertyName("segmentDuration")]
    public required long? SegmentDuration { get; init; }

    /// <summary>Gets the duration of the track header in units of the time scale of the movie; <see langword="null"/> when every bit of the field is set (indefinite).</summary>
    [JsonPropertyName("trackDuration")]
    public required long? TrackDuration { get; init; }

    /// <summary>
    /// Computes the number of plays these fields mean: a sequence without edit list repeats forever (<see langword="null"/>);
    /// an edit list that does not repeat is played once; a repeating one is played until the duration of the track, which
    /// is forever when the duration is indefinite, and otherwise the duration of the track divided by the duration of the
    /// edit, rounded up. A number of plays above <see cref="int.MaxValue"/> stands for forever.
    /// </summary>
    /// <param name="editList">The fields, or <see langword="null"/> without edit list.</param>
    /// <returns>The number of plays, or <see langword="null"/> for a sequence that repeats forever.</returns>
    public static int? GetTotalPlays(EditListExpectation? editList)
    {
        if (editList is null)
            return null;

        if (!editList.Repeating)
            return 1;

        if (editList.TrackDuration is not { } track || editList.SegmentDuration is not { } segment || segment <= 0)
            return null;

        var plays = (new BigInteger(track) + segment - 1) / segment;
        return plays > int.MaxValue ? null : (int)BigInteger.Max(plays, 1);
    }
}
