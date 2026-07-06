using TestUtilities;

namespace Meziantou.Framework.EmbeddedConstantsGenerator.Tests;

public sealed class EmbeddedConstantsGeneratorPackageFixture()
    : NuGetPackageFixture(PackageName)
{
    public const string PackageName = "Meziantou.Framework.EmbeddedConstantsGenerator";
}
