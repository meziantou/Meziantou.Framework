using TestUtilities;

namespace Meziantou.Framework.SnapshotTesting.PackageTests;

public sealed class SnapshotTestingPackageFixture()
    : NuGetPackageFixture(PackageName)
{
    public const string PackageName = "Meziantou.Framework.SnapshotTesting";
}
