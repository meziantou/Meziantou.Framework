using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>Options of <see cref="GoldenAssert"/>.</summary>
public sealed class GoldenAssertOptions
{
    /// <summary>Gets the default options (previews according to <see cref="TestArtifacts"/>).</summary>
    public static GoldenAssertOptions Default { get; } = new();

    /// <summary>Gets a value indicating whether previews are written on failure (default: <see cref="TestArtifacts.PreviewsEnabled"/>).</summary>
    public bool WritePreviews { get; init; } = TestArtifacts.PreviewsEnabled;

    /// <summary>Gets the preview directory; <see langword="null"/> uses <see cref="TestArtifacts.GetDirectory"/> with the fixture id.</summary>
    public FullPath? PreviewDirectory { get; init; }

    /// <summary>Gets the maximum number of violating samples listed per frame.</summary>
    public int MaxReportedMismatches { get; init; } = PixelBufferComparer.DefaultMaxReportedMismatches;
}
