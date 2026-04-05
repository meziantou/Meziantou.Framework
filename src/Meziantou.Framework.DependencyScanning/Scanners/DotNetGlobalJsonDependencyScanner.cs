using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans .NET global.json files for SDK versions and MSBuild SDK package references.</summary>
public sealed class DotNetGlobalJsonDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet, DependencyType.DotNetSdk];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("global.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);

            ExtractSdk(context, doc);
            ExtractMsBuildSdks(context, doc);
        }
        catch (JsonException)
        {
        }
    }

    private void ExtractMsBuildSdks(ScanFileContext context, JsonNodeDocument doc)
    {
        if (doc.GetRootObject() is not JsonObject root)
            return;

        if (!doc.TryGetObject(root, "msbuild-sdks", "$", out var sdks, out var sdksPath))
            return;

        foreach (var sdk in doc.GetProperties(sdks, sdksPath))
        {
            if (JsonNodeDocument.TryGetString(sdk.Value, out var sdkVersion))
            {
                context.ReportDependency(this, sdk.Name, sdkVersion, DependencyType.NuGet,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new JsonLocation(context, sdk.Path, doc.GetLineInfo(sdk.Path)));
            }
        }
    }

    private void ExtractSdk(ScanFileContext context, JsonNodeDocument doc)
    {
        if (doc.GetRootObject() is not JsonObject root)
            return;

        if (!doc.TryGetObject(root, "sdk", "$", out var sdk, out var sdkPath))
            return;

        if (!doc.TryGetProperty(sdk, "version", sdkPath, out var versionNode, out var versionPath))
            return;

        if (!JsonNodeDocument.TryGetString(versionNode, out var version))
            return;

        context.ReportDependency(this, name: null, version, DependencyType.DotNetSdk,
            nameLocation: null,
            versionLocation: new JsonLocation(context, versionPath, doc.GetLineInfo(versionPath)));
    }
}
