using System.Collections.Concurrent;
using Meziantou.Framework.NuGetPackageValidation.Internal;
using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationContext : IDisposable
{
    private readonly ConcurrentBag<NuGetPackageValidationError> _errors = new();
    private readonly NuGetPackageValidationOptions _options;

    internal NuGetPackageValidationContext(FullPath file, NuGetPackageValidationOptions options, CancellationToken cancellationToken)
    {
        PackagePath = file;
        _options = options;
        CancellationToken = cancellationToken;
        Package = new PackageArchiveReader(file);

        var symbolPackagePath = file.ChangeExtension(".snupkg");
        if (File.Exists(symbolPackagePath))
        {
            SymbolPackage = new PackageArchiveReader(symbolPackagePath);
        }
    }

    public FullPath PackagePath { get; }
    public CancellationToken CancellationToken { get; }
    public PackageReaderBase Package { get; }
    public PackageReaderBase? SymbolPackage { get; }
    public IEnumerable<Uri> SymbolServers
    {
        get
        {
            foreach (var server in _options.SymbolServers)
            {
                if (string.IsNullOrEmpty(server))
                    continue;

                yield return new Uri(EnsureTrailingSlash(server));

                static string EnsureTrailingSlash(string url)
                {
                    if (url.EndsWith('/'))
                        return url;

                    return url + "/";
                }
            }
        }
    }

    internal IReadOnlyCollection<NuGetPackageValidationError> Errors => _errors;

    public bool IsRuleExcluded(int ruleId)
    {
        return _options.ExcludedRuleIds.Contains(ruleId);
    }

    public void Dispose()
    {
        Package.Dispose();
        SymbolPackage?.Dispose();
    }

    public void ReportError(int errorCode, string message, string? helpText = null, string? fileName = null)
    {
        if (_options.ExcludedRuleIds.Contains(errorCode))
            return;

        _errors.Add(new NuGetPackageValidationError(errorCode, message, helpText, fileName));
    }

    internal Task<HttpResponseMessage> SendHttpRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
    {
        _options.ConfigureRequest?.Invoke(httpRequestMessage);
        return SharedHttpClient.Instance.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    internal async Task<bool> IsUrlAccessible(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

}
