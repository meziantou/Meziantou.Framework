using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class DotNetGlobalJsonDependencyScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.FileName.Equals("global.json", StringComparison.Ordinal);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            using var jsonReader = new JsonTextReader(sr);
            var doc = await JToken.ReadFromAsync(jsonReader, context.CancellationToken).ConfigureAwait(false);

            await ExtractSdk(context, doc).ConfigureAwait(false);
            await ExtractMsBuildSdks(context, doc).ConfigureAwait(false);
        }
        catch (JsonException)
        {
        }
    }

    private static async Task ExtractMsBuildSdks(ScanFileContext context, JToken doc)
    {
        var sdksToken = doc.SelectToken("$.msbuild-sdks");
        if (sdksToken is JObject sdks)
        {
            foreach (var sdk in sdks.Properties())
            {
                var sdkVersion = sdk.Value.Value<string>();
                if (sdkVersion != null)
                {
                    await context.ReportDependency(new Dependency(sdk.Name, sdkVersion, DependencyType.NuGet, new JsonLocation(context.FullPath, LineInfo.FromJToken(sdk.Value), sdk.Value.Path))).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task ExtractSdk(ScanFileContext context, JToken doc)
    {
        var token = doc.SelectToken("$.sdk.version");
        if (token?.Value<string>() is string version)
        {
            await context.ReportDependency(new Dependency(".NET SDK", version, DependencyType.DotNetSdk, new JsonLocation(context.FullPath, LineInfo.FromJToken(token), token.Path))).ConfigureAwait(false);
        }
    }
}
