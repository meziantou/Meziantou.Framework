using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting.Pdf.Tests;

// The setup of the readme: the tests that validate with SnapshotSettings.Default use it
#region SnapshotTestingPdf_Setup
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddPdf();
    }
}
#endregion
