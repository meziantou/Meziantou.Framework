using System;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Scanners
{
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

                var token = doc.SelectToken("$.sdk.version");
                if (token?.Value<string>() is string version)
                {
                    await context.ReportDependency(new Dependency(".NET SDK", version, DependencyType.DotNetSdk, new JsonLocation(context.FullPath, LineInfo.FromJToken(token), token.Path))).ConfigureAwait(false);
                }

                var sdksToken = doc.SelectToken("$.msbuild-sdks");
                if (sdksToken is JObject sdks)
                {
                    foreach (var sdk in sdks.Properties())
                    {
                        version = sdk.Value.Value<string>();
                        if (version != null)
                        {
                            await context.ReportDependency(new Dependency(sdk.Name, version, DependencyType.NuGet, new JsonLocation(context.FullPath, LineInfo.FromJToken(sdk.Value), sdk.Value.Path))).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
    }
}
