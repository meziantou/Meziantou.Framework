using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// An import map (https://html.spec.whatwg.org/multipage/webappapis.html#import-maps): the module specifier maps of the
/// top-level imports and of the scopes, and the integrity metadata of the modules. The values of the specifier maps are the
/// serialized URLs, or null for the mappings that fail to resolve.
/// </summary>
internal sealed class ImportMap
{
    /// <summary>The top-level imports, sorted by key in descending code unit order.</summary>
    public List<KeyValuePair<string, string?>> Imports { get; } = [];

    /// <summary>The scopes (serialized URL prefix → specifier map), sorted by prefix in descending code unit order.</summary>
    public List<KeyValuePair<string, List<KeyValuePair<string, string?>>>> Scopes { get; } = [];

    /// <summary>The integrity metadata of the modules, by serialized URL (https://html.spec.whatwg.org/multipage/webappapis.html#concept-import-map-integrity).</summary>
    public Dictionary<string, string> Integrity { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The resolved module set of the global object (https://html.spec.whatwg.org/multipage/webappapis.html#resolved-module-set):
    /// the specifiers that were resolved, which the import maps registered afterwards cannot change.
    /// </summary>
    public HashSet<SpecifierResolutionRecord> ResolvedModuleSet { get; } = [];
}

/// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#specifier-resolution-record</summary>
/// <param name="SerializedBaseUrl">The serialized base URL of the script that resolved the specifier.</param>
/// <param name="Specifier">The normalized specifier.</param>
/// <param name="IsUrlLikeAndNotSpecial">Whether the specifier is a URL whose scheme is not special (the prefixes of the import maps do not apply to it).</param>
internal readonly record struct SpecifierResolutionRecord(string SerializedBaseUrl, string Specifier, bool IsUrlLikeAndNotSpecial);

/// <summary>The resolution of the module specifiers (https://html.spec.whatwg.org/multipage/webappapis.html#resolve-a-module-specifier).</summary>
internal static class ModuleSpecifierResolution
{
    /// <summary>
    /// Resolves a module specifier, relative to the base URL of the referring script (or of the document), with the import
    /// map of the global object. Throws a <see cref="TypeErrorException"/> when the specifier is a bare specifier that is not mapped,
    /// or when the mapping is blocked.
    /// </summary>
    public static string Resolve(GlobalScope window, string specifier, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(specifier);
        var baseRecord = UrlParser.Parse(baseUrl ?? window.ApiBaseUrl.Serialize(), baseUrl: null) ?? window.ApiBaseUrl;
        var importMap = window.ImportMap;
        var serializedBaseUrl = baseRecord.Serialize();
        var asUrl = ResolveUrlLikeSpecifier(specifier, baseRecord);
        var normalizedSpecifier = asUrl?.Serialize() ?? specifier;
        string? result = null;
        foreach (var (scopePrefix, scopeImports) in importMap.Scopes)
        {
            if (scopePrefix == serializedBaseUrl || (scopePrefix.EndsWith('/', StringComparison.Ordinal) && serializedBaseUrl.StartsWith(scopePrefix, StringComparison.Ordinal)))
            {
                result = ResolveImportsMatch(normalizedSpecifier, asUrl, scopeImports);
                if (result is not null)
                    break;
            }
        }

        result ??= ResolveImportsMatch(normalizedSpecifier, asUrl, importMap.Imports) ?? asUrl?.Serialize();
        if (result is null)
            throw new TypeErrorException($"Failed to resolve module specifier '{specifier}': relative references must start with '/', './' or '../'");

        // https://html.spec.whatwg.org/multipage/webappapis.html#add-module-to-resolved-module-set: the resolution of the
        // specifier cannot be changed by the import maps registered afterwards (only the windows have import maps)
        if (window is Window)
        {
            importMap.ResolvedModuleSet.Add(new SpecifierResolutionRecord(serializedBaseUrl, normalizedSpecifier, asUrl is { IsSpecial: false }));
        }

        return result;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#resolving-a-module-integrity-metadata: the integrity metadata
    /// that the import map of the global object gives to the URL of a module, or the empty string.
    /// </summary>
    public static string ResolveIntegrityMetadata(GlobalScope global, string url) => global.ImportMap.Integrity.GetValueOrDefault(url, "");

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#resolving-a-url-like-module-specifier</summary>
    private static UrlRecord? ResolveUrlLikeSpecifier(string specifier, UrlRecord baseUrl)
    {
        if (specifier.StartsWith('/', StringComparison.Ordinal) || specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal))
            return UrlParser.Parse(specifier, baseUrl);

        return UrlParser.Parse(specifier, baseUrl: null);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#resolving-an-imports-match</summary>
    private static string? ResolveImportsMatch(string normalizedSpecifier, UrlRecord? asUrl, List<KeyValuePair<string, string?>> specifierMap)
    {
        foreach (var (specifierKey, resolutionResult) in specifierMap)
        {
            if (specifierKey == normalizedSpecifier)
                return resolutionResult ?? throw new TypeErrorException($"The module specifier '{normalizedSpecifier}' is blocked by the import map");

            if (specifierKey.EndsWith('/', StringComparison.Ordinal) && normalizedSpecifier.StartsWith(specifierKey, StringComparison.Ordinal) && (asUrl is null || asUrl.IsSpecial))
            {
                if (resolutionResult is null)
                    throw new TypeErrorException($"The module specifier '{normalizedSpecifier}' is blocked by the import map");

                var afterPrefix = normalizedSpecifier[specifierKey.Length..];
                var url = UrlParser.Parse(afterPrefix, UrlParser.Parse(resolutionResult, baseUrl: null));
                if (url is null)
                    throw new TypeErrorException($"The module specifier '{normalizedSpecifier}' cannot be resolved with the import map");

                var serialized = url.Serialize();
                if (!serialized.StartsWith(resolutionResult, StringComparison.Ordinal))
                    throw new TypeErrorException($"The module specifier '{normalizedSpecifier}' backtracks above its prefix in the import map");

                return serialized;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses an import map string (https://html.spec.whatwg.org/multipage/webappapis.html#parse-an-import-map-string).
    /// Throws a <see cref="TypeErrorException"/> (or a <see cref="JsonException"/> for a syntax error) when the JSON is not an
    /// object or its imports or scopes are not objects. The invalid entries are ignored.
    /// </summary>
    public static ImportMap Parse(string input, UrlRecord baseUrl)
    {
        using var document = JsonDocument.Parse(input);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new TypeErrorException("The import map must be a JSON object");

        var importMap = new ImportMap();
        if (root.TryGetProperty("imports", out var imports))
        {
            if (imports.ValueKind != JsonValueKind.Object)
                throw new TypeErrorException("The imports of the import map must be a JSON object");

            importMap.Imports.AddRange(NormalizeSpecifierMap(imports, baseUrl));
        }

        if (root.TryGetProperty("scopes", out var scopes))
        {
            if (scopes.ValueKind != JsonValueKind.Object)
                throw new TypeErrorException("The scopes of the import map must be a JSON object");

            foreach (var scope in scopes.EnumerateObject())
            {
                if (scope.Value.ValueKind != JsonValueKind.Object)
                    throw new TypeErrorException($"The scope '{scope.Name}' of the import map must be a JSON object");

                if (UrlParser.Parse(scope.Name, baseUrl) is not { } scopePrefixUrl)
                    continue;

                var prefix = scopePrefixUrl.Serialize();
                importMap.Scopes.RemoveAll(entry => entry.Key == prefix);
                importMap.Scopes.Add(new(prefix, NormalizeSpecifierMap(scope.Value, baseUrl)));
            }

            importMap.Scopes.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));
        }

        // https://html.spec.whatwg.org/multipage/webappapis.html#normalizing-a-module-integrity-map: the keys are resolved as
        // URL-like specifiers; the entries whose key cannot be resolved or whose value is not a string are ignored
        if (root.TryGetProperty("integrity", out var integrity))
        {
            if (integrity.ValueKind != JsonValueKind.Object)
                throw new TypeErrorException("The integrity of the import map must be a JSON object");

            foreach (var entry in integrity.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String && ResolveUrlLikeSpecifier(entry.Name, baseUrl) is { } resolvedUrl)
                {
                    importMap.Integrity[resolvedUrl.Serialize()] = entry.Value.GetString()!;
                }
            }
        }

        return importMap;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#sorting-and-normalizing-a-module-specifier-map</summary>
    private static List<KeyValuePair<string, string?>> NormalizeSpecifierMap(JsonElement map, UrlRecord baseUrl)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in map.EnumerateObject())
        {
            // The specifier key: the empty string is ignored, a URL-like specifier is normalized
            if (entry.Name.Length == 0)
                continue;

            var normalizedKey = ResolveUrlLikeSpecifier(entry.Name, baseUrl)?.Serialize() ?? entry.Name;
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                normalized[normalizedKey] = null;
                continue;
            }

            var addressUrl = ResolveUrlLikeSpecifier(entry.Value.GetString()!, baseUrl);
            if (addressUrl is null || (entry.Name.EndsWith('/', StringComparison.Ordinal) && !addressUrl.Serialize().EndsWith('/', StringComparison.Ordinal)))
            {
                normalized[normalizedKey] = null;
                continue;
            }

            normalized[normalizedKey] = addressUrl.Serialize();
        }

        var result = normalized.ToList();
        result.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));
        return result;
    }

    /// <summary>
    /// Merges a new import map into the import map of the window (https://html.spec.whatwg.org/multipage/webappapis.html#merge-existing-and-new-import-maps):
    /// the existing mappings win, and the new mappings that would change the resolution of a specifier that was already
    /// resolved (the resolved module set) are ignored.
    /// </summary>
    public static void Merge(ImportMap existing, ImportMap newImportMap)
    {
        foreach (var (scopePrefix, newScopeImports) in newImportMap.Scopes)
        {
            // The rules of the scope that apply to a specifier already resolved by a script of the scope are removed
            var scopeImports = new List<KeyValuePair<string, string?>>(newScopeImports);
            foreach (var record in existing.ResolvedModuleSet)
            {
                if (scopePrefix == record.SerializedBaseUrl || (scopePrefix.EndsWith('/', StringComparison.Ordinal) && record.SerializedBaseUrl.StartsWith(scopePrefix, StringComparison.Ordinal)))
                {
                    scopeImports.RemoveAll(mapping => mapping.Key == record.Specifier
                        || (mapping.Key.EndsWith('/', StringComparison.Ordinal) && record.Specifier.StartsWith(mapping.Key, StringComparison.Ordinal) && !record.IsUrlLikeAndNotSpecial));
                }
            }

            var index = existing.Scopes.FindIndex(entry => entry.Key == scopePrefix);
            if (index < 0)
            {
                existing.Scopes.Add(new(scopePrefix, scopeImports));
                continue;
            }

            var existingScope = existing.Scopes[index].Value;
            foreach (var mapping in scopeImports)
            {
                if (!existingScope.Exists(entry => entry.Key == mapping.Key))
                {
                    existingScope.Add(mapping);
                }
            }

            existingScope.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));
        }

        existing.Scopes.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));

        foreach (var (url, integrity) in newImportMap.Integrity)
        {
            existing.Integrity.TryAdd(url, integrity);
        }

        // The top-level rules that apply to a specifier that was already resolved are ignored. The specification removes the
        // rules whose specifier starts with the resolved specifier, but the browsers remove the rules that match it (the
        // specifier itself, or a prefix of it), as for the scopes: a prefix rule cannot change a resolved specifier either
        foreach (var mapping in newImportMap.Imports)
        {
            var isResolved = false;
            foreach (var record in existing.ResolvedModuleSet)
            {
                if (mapping.Key == record.Specifier || (mapping.Key.EndsWith('/', StringComparison.Ordinal) && record.Specifier.StartsWith(mapping.Key, StringComparison.Ordinal) && !record.IsUrlLikeAndNotSpecial))
                {
                    isResolved = true;
                    break;
                }
            }

            if (!isResolved && !existing.Imports.Exists(entry => entry.Key == mapping.Key))
            {
                existing.Imports.Add(mapping);
            }
        }

        existing.Imports.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));
    }
}
