using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting.Imaging.Tests;

// The setup of the readme: the tests that validate with SnapshotSettings.Default (SampleTests) use it
#region SnapshotTestingImaging_Setup
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddImaging();
    }
}
#endregion
