using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>Tool-free checks of the interop infrastructure, so the project always runs at least one test.</summary>
public sealed class InteropSettingsTests
{
    [Fact]
    public void ArtifactsDirectoryIsCreated()
    {
        var directory = InteropSettings.GetArtifactsDirectory(nameof(ArtifactsDirectoryIsCreated));
        Assert.True(Directory.Exists(directory));
    }
}
