using System.Runtime.CompilerServices;
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.SnapshotTesting.Imaging;

namespace Meziantou.AspNetCore.Testing.Tests;

#region ComponentTestHost_SnapshotSetup
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Compare the images by their pixels. The antialiasing of the edges can differ slightly between CPU architectures
        // (floating-point rounding): a peak signal-to-noise ratio of 40 dB ignores it, not a change of color or layout.
        SnapshotSettings.Default.AddImaging(new ImageComparisonSettings { PsnrThreshold = 40 });
    }
}
#endregion
