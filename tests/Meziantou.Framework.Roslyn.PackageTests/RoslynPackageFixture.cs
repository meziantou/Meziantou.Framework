using TestUtilities;

namespace Meziantou.Framework.Roslyn.PackageTests;

public sealed class RoslynPackageFixture()
    : NuGetPackageFixture(PackageName)
{
    public const string PackageName = "Meziantou.Framework.Roslyn";
}
