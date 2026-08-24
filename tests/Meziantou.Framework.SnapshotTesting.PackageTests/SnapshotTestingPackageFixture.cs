using TestUtilities;

namespace Meziantou.Framework.SnapshotTesting.PackageTests;

public sealed class SnapshotTestingPackageFixture()
    : NuGetPackageFixture(PackageName, DependencyPackageNames)
{
    public const string PackageName = "Meziantou.Framework.SnapshotTesting";

    private static readonly string[] DependencyPackageNames =
    [
        "Meziantou.Framework.DiffEngine",
        "Meziantou.Framework.FullPath",
        "Meziantou.Framework.HumanReadableSerializer",
        "Meziantou.Framework.LLMContext",
    ];
}
