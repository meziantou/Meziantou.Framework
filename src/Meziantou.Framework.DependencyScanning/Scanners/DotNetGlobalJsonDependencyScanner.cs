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
        foreach (var sdksMatch in doc.Select("$['msbuild-sdks']"))
        {
            if (sdksMatch.Node is not JsonObject sdks)
                continue;

            foreach (var sdk in sdks)
            {
                if (sdk.Value is JsonValue sdkValue && sdkValue.TryGetValue<string>(out var sdkVersion))
                {
                    var sdkVersionPath = JsonNodeDocument.AppendPropertyPath(sdksMatch.Path, sdk.Key);
                    context.ReportDependency(this, sdk.Key, sdkVersion, DependencyType.NuGet,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: new JsonLocation(context, sdkVersionPath, doc.GetLineInfo(sdkVersionPath)));
                }
            }
        }
    }

    private void ExtractSdk(ScanFileContext context, JsonNodeDocument doc)
    {
        foreach (var sdkMatch in doc.Select("$.sdk.version"))
        {
            if (sdkMatch.Node is not JsonValue sdkVersion || !sdkVersion.TryGetValue<string>(out var version))
                continue;

            context.ReportDependency(this, name: null, version, DependencyType.DotNetSdk,
                nameLocation: null,
                versionLocation: new JsonLocation(context, sdkMatch.Path, sdkMatch.LineInfo));
        }
    }
}
