using System.Collections.Concurrent;
using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Provides context information and methods for validating a NuGet package.</summary>
public sealed class NuGetPackageValidationContext : IDisposable
{
    private readonly ConcurrentBag<NuGetPackageValidationError> _errors = [];
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

    /// <summary>Gets the path to the NuGet package being validated.</summary>
    public FullPath PackagePath { get; }

    /// <summary>Gets the cancellation token for the validation operation.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the package reader for accessing the contents of the NuGet package.</summary>
    public PackageReaderBase Package { get; }

    /// <summary>Gets the package reader for the symbol package (.snupkg) if it exists alongside the main package.</summary>
    public PackageReaderBase? SymbolPackage { get; }

    /// <summary>Gets the collection of symbol server URLs configured for checking symbol availability.</summary>
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

    /// <summary>Determines whether a specific validation rule is excluded from validation.</summary>
    /// <param name="ruleId">The error code of the rule to check.</param>
    /// <returns><see langword="true"/> if the rule is excluded; otherwise, <see langword="false"/>.</returns>
    public bool IsRuleExcluded(int ruleId)
    {
        return _options.ExcludedRuleIds.Contains(ruleId);
    }

    public void Dispose()
    {
        Package.Dispose();
        SymbolPackage?.Dispose();
    }

    /// <summary>Reports a validation error found during package validation.</summary>
    /// <param name="errorCode">The numeric error code identifying the type of validation error.</param>
    /// <param name="message">A human-readable message describing the validation error.</param>
    /// <param name="helpText">Optional help text providing guidance on how to fix the error.</param>
    /// <param name="fileName">Optional file name within the package where the error was found.</param>
    public void ReportError(int errorCode, string message, string? helpText = null, string? fileName = null)
    {
        if (_options.ExcludedRuleIds.Contains(errorCode))
            return;

        _errors.Add(new NuGetPackageValidationError(errorCode, message, helpText, fileName));
    }

    /// <summary>Sends an HTTP request using the configured HTTP client and request configuration.</summary>
    /// <param name="httpRequestMessage">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A token to cancel the HTTP request.</param>
    /// <returns>A task that represents the asynchronous HTTP request. The task result contains the HTTP response message.</returns>
    internal Task<HttpResponseMessage> SendHttpRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
    {
        _options.ConfigureRequest?.Invoke(httpRequestMessage);
        return SharedHttpClient.Instance.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Checks if a URL is accessible by sending an HTTP GET request.</summary>
    /// <param name="url">The URL to check.</param>
    /// <param name="cancellationToken">A token to cancel the HTTP request.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the URL is accessible; otherwise, <see langword="false"/>.</returns>
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
