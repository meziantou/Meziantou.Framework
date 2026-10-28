using TestUtilities;

namespace Meziantou.Framework.SnapshotTesting.PackageTests;

public sealed class SnapshotTestingPackageFixture()
    : NuGetPackageFixture(PackageName, InlineSnapshotTestingPackageName)
{
    public const string PackageName = "Meziantou.Framework.SnapshotTesting";
    public const string InlineSnapshotTestingPackageName = "Meziantou.Framework.InlineSnapshotTesting";

    // The consumer projects of the tests only target net10.0
    protected override string? TargetFrameworks => "net10.0";
}
