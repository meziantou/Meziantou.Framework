using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Scanners;

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
            using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            using var jsonReader = new JsonTextReader(sr);
            var doc = await JToken.ReadFromAsync(jsonReader, context.CancellationToken).ConfigureAwait(false);

            ExtractSdk(context, doc);
            ExtractMsBuildSdks(context, doc);
        }
        catch (JsonException)
        {
        }
    }

    private void ExtractMsBuildSdks(ScanFileContext context, JToken doc)
    {
        var sdksToken = doc.SelectToken("$.msbuild-sdks");
        if (sdksToken is JObject sdks)
        {
            foreach (var sdk in sdks.Properties())
            {
                var sdkVersion = sdk.Value.Value<string>();
                if (sdkVersion is not null)
                {
                    context.ReportDependency(this, sdk.Name, sdkVersion, DependencyType.NuGet,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: new JsonLocation(context, sdk.Value));
                }
            }
        }
    }

    private void ExtractSdk(ScanFileContext context, JToken doc)
    {
        var token = doc.SelectToken("$.sdk.version");
        if (token?.Value<string>() is string version)
        {
            context.ReportDependency(this, name: null, version, DependencyType.DotNetSdk,
                nameLocation: null,
                versionLocation: new JsonLocation(context, token));
        }
    }
}
