namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>A track of an image sequence, as <c>heif-info</c> prints it.</summary>
/// <param name="Id">The identifier of the track.</param>
/// <param name="Handler">The handler type: <c>pict</c> (image sequence), <c>vide</c> (video) or <c>auxv</c> (auxiliary: alpha).</param>
/// <param name="Resolution">The size of the images, or <see langword="null"/> when heif-info prints none.</param>
/// <param name="Repetitions">
/// The number of times the track is played (<c>1</c> for the single playback of a track without repetition), or
/// <see langword="null"/> when it repeats forever or when heif-info does not read its edit list (<see cref="HasUnsupportedEditList"/>).
/// </param>
/// <param name="SampleEntryType">The type of the sample entry: <c>hvc1</c> or <c>hev1</c>.</param>
public sealed record HeifInfoTrack(int Id, string Handler, (int Width, int Height)? Resolution, int? Repetitions, string SampleEntryType)
{
    /// <summary>
    /// Gets a value indicating whether heif-info prints <c>unsupported editlist</c> instead of a number of repetitions:
    /// libheif 1.23.6 does so for an edit list that is not repeated (the <c>elst</c> box without its repeat flag), which is
    /// how a file of the AV1 Image File Format rules says that a sequence is played once. <c>heif-dec -S</c> plays such a
    /// sequence once.
    /// </summary>
    public bool HasUnsupportedEditList { get; init; }
}
