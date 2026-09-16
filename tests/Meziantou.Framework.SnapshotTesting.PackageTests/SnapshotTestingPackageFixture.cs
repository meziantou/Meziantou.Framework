using TestUtilities;

namespace Meziantou.Framework.SnapshotTesting.PackageTests;

public sealed class SnapshotTestingPackageFixture()
    : NuGetPackageFixture(PackageName, InlineSnapshotTestingPackageName)
{
    public const string PackageName = "Meziantou.Framework.SnapshotTesting";
    public const string InlineSnapshotTestingPackageName = "Meziantou.Framework.InlineSnapshotTesting";
}
