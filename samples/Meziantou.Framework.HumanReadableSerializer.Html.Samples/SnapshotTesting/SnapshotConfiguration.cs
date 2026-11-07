using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingSample;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.ConfigureHumanReadableSerializer(options => options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
        }));
    }
}
