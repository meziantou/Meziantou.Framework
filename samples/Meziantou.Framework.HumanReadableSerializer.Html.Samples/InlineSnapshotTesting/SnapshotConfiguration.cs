using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.InlineSnapshotTesting;

namespace InlineSnapshotTestingSample;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var settings = InlineSnapshotSettings.Default with { };
        settings.UseHumanReadableSerializer(options => options.AddHtmlFormatter(new HtmlFormatterOptions
        {
            OrderAttributes = true,
        }));
        InlineSnapshotSettings.Default = settings;
    }
}
