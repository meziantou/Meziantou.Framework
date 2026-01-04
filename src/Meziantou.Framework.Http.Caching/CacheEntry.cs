using System.Globalization;
using System.Net.Http.Headers;

namespace HttpCaching;

internal sealed class CacheEntry
{
    private CacheEntry(byte[] serializedResponse)
    {
        SerializedResponse = serializedResponse;
    }

    public static async Task<CacheEntry> CreateAsync(HttpRequestMessage request, HttpResponseMessage response,
        DateTimeOffset requestTime, DateTimeOffset responseTime, CancellationToken cancellationToken)
    {
        var serializedResponse = await ResponseSerializer.SerializeAsync(response, cancellationToken).ConfigureAwait(false);
        var entry = new CacheEntry(serializedResponse)
        {
            RequestTime = requestTime,
            ResponseTime = responseTime,
        };

        // Parse Date header or use response time
        entry.ResponseDate = response.Headers.Date ?? responseTime;

        // Parse Age header if present
        entry.AgeValue = response.Headers.Age ?? TimeSpan.Zero;

        // Parse cache control directives
        var cacheControl = response.Headers.CacheControl;
        if (cacheControl != null)
        {
            entry.MaxAge = cacheControl.MaxAge;
            entry.SharedMaxAge = cacheControl.SharedMaxAge;
            entry.MustRevalidate = cacheControl.MustRevalidate || cacheControl.ProxyRevalidate;
            entry.ResponseNoCache = cacheControl.NoCache;

            // RFC 8246: Parse immutable directive
            entry.Immutable = HasImmutableDirective(cacheControl);

            // RFC 5861: Parse stale-if-error directive
            entry.StaleIfError = ParseStaleIfErrorDirective(cacheControl);
        }

        // Parse Expires header
        entry.Expires = ParseExpiresHeader(response);

        // Parse validators
        entry.ETag = response.Headers.ETag?.ToString();
        
        // RFC 7232: Last-Modified can be in content headers or response headers
        // For 204 No Content responses, it will be in response headers
        entry.LastModified = response.Content.Headers.LastModified;
        if (entry.LastModified is null && response.Headers.TryGetValues("Last-Modified", out var lastModifiedValues))
        {
            var lastModifiedValue = lastModifiedValues.FirstOrDefault();
            if (lastModifiedValue is not null && DateTimeOffset.TryParse(lastModifiedValue, CultureInfo.InvariantCulture, out var parsedDate))
            {
                entry.LastModified = parsedDate;
            }
        }

        // Handle Vary header for secondary key
        entry.SecondaryKey = BuildSecondaryKey(request, response);

        return entry;
    }

    private static bool HasImmutableDirective(CacheControlHeaderValue cacheControl)
    {
        // RFC 8246: Check for immutable directive in cache-control extensions
        if (cacheControl.Extensions is null)
            return false;

        foreach (var extension in cacheControl.Extensions)
        {
            if (string.Equals(extension.Name, "immutable", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static TimeSpan? ParseStaleIfErrorDirective(CacheControlHeaderValue cacheControl)
    {
        // RFC 5861: Check for stale-if-error directive in cache-control extensions
        if (cacheControl.Extensions is null)
            return null;

        foreach (var extension in cacheControl.Extensions)
        {
            if (string.Equals(extension.Name, "stale-if-error", StringComparison.OrdinalIgnoreCase))
            {
                if (extension.Value != null && int.TryParse(extension.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseExpiresHeader(HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues("Expires", out var values))
        {
            if (!response.Headers.TryGetValues("Expires", out values))
                return null;
        }

        var expiresValue = values.FirstOrDefault();
        if (string.IsNullOrEmpty(expiresValue))
            return null;

        // RFC 7234 Section 5.3: Invalid dates (including "0") are treated as past
        if (expiresValue is "0" || expiresValue is "-1")
            return DateTimeOffset.MinValue;

        // RFC 7231 Section 7.1.1.1: Try RFC 1123 format first (preferred)
        if (DateTimeOffset.TryParseExact(expiresValue, "R", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;

        // RFC 7231: Try RFC 850 format (obsolete but still used)
        // Format: Sunday, 06-Nov-94 08:49:37 GMT
        if (DateTimeOffset.TryParseExact(expiresValue, "dddd, dd-MMM-yy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // Try without GMT suffix in case it was already parsed
        if (DateTimeOffset.TryParseExact(expiresValue, "dddd, dd-MMM-yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // RFC 7231: Try asctime format (obsolete but still used)  
        // Format: Sun Nov  6 08:49:37 1994
        if (DateTimeOffset.TryParseExact(expiresValue, "ddd MMM  d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // Also try single-digit day format for asctime
        if (DateTimeOffset.TryParseExact(expiresValue, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // Try general parsing as fallback
        if (DateTimeOffset.TryParse(expiresValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        return DateTimeOffset.MinValue; // Invalid date treated as expired
    }

    private static CacheEntrySecondaryKey BuildSecondaryKey(HttpRequestMessage request, HttpResponseMessage response)
    {
        var varyHeaders = response.Headers.Vary;
        if (varyHeaders is null || varyHeaders.Count is 0)
            return CacheEntrySecondaryKey.MatchAll;

        var secondaryKey = new CacheEntrySecondaryKey();
        foreach (var headerName in varyHeaders)
        {
            // RFC 7234 Section 4.1: Vary: * always fails to match
            if (headerName is "*")
                return CacheEntrySecondaryKey.MatchNone;

            if (request.Headers.TryGetValues(headerName, out var values))
            {
                // Sort values to ensure consistent ordering for cache matching
                // RFC 7234: Order of header values shouldn't affect cache matching
                foreach (var value in values.Order(StringComparer.Ordinal))
                {
                    secondaryKey.Add(headerName, value);
                }
            }
        }

        return secondaryKey;
    }

    public CacheEntrySecondaryKey SecondaryKey { get; private set; }
    public DateTimeOffset RequestTime { get; private set; }
    public DateTimeOffset ResponseTime { get; private set; }
    public DateTimeOffset ResponseDate { get; private set; }
    public TimeSpan AgeValue { get; private set; }
    public TimeSpan? MaxAge { get; private set; }
    public TimeSpan? SharedMaxAge { get; private set; }
    public DateTimeOffset? Expires { get; private set; }
    public bool MustRevalidate { get; private set; }
    public bool ResponseNoCache { get; private set; }
    public bool Immutable { get; private set; }

    // RFC 5861: stale-if-error directive
    public TimeSpan? StaleIfError { get; private set; }

    // Validators for conditional requests
    public string? ETag { get; private set; }
    public DateTimeOffset? LastModified { get; private set; }

    public byte[] SerializedResponse { get; private set; }

    /// <summary>Calculates the freshness lifetime per RFC 7234 Section 4.2.1.</summary>
    public TimeSpan FreshnessLifetime
    {
        get
        {
            // 1. Use s-maxage if present (takes precedence for shared caches), otherwise max-age
            if (SharedMaxAge.HasValue)
                return SharedMaxAge.Value;

            if (MaxAge.HasValue)
                return MaxAge.Value;

            // 2. Use Expires - Date if present
            if (Expires.HasValue)
            {
                var expiresTime = Expires.Value;
                if (expiresTime == DateTimeOffset.MinValue)
                    return TimeSpan.Zero; // Already expired

                var freshness = expiresTime - ResponseDate;
                return freshness > TimeSpan.Zero ? freshness : TimeSpan.Zero;
            }

            // 3. Heuristic freshness (RFC 7234 Section 4.2.2)
            // Use 10% of time since Last-Modified
            if (LastModified.HasValue)
            {
                var age = ResponseDate - LastModified.Value;
                if (age > TimeSpan.Zero)
                {
                    return TimeSpan.FromSeconds(age.TotalSeconds * 0.1);
                }
            }

            // No explicit expiration and no heuristic available
            return TimeSpan.Zero;
        }
    }

    /// <summary>Calculates the current age per RFC 7234 Section 4.2.3.</summary>
    public TimeSpan CalculateCurrentAge(DateTimeOffset now)
    {
        // apparent_age = max(0, response_time - date_value)
        var apparentAge = ResponseTime - ResponseDate;
        if (apparentAge < TimeSpan.Zero)
            apparentAge = TimeSpan.Zero;

        // response_delay = response_time - request_time
        var responseDelay = ResponseTime - RequestTime;

        // corrected_age_value = age_value + response_delay
        var correctedAgeValue = AgeValue + responseDelay;

        // corrected_initial_age = max(apparent_age, corrected_age_value)
        var correctedInitialAge = apparentAge > correctedAgeValue ? apparentAge : correctedAgeValue;

        // resident_time = now - response_time
        var residentTime = now - ResponseTime;

        // current_age = corrected_initial_age + resident_time
        return correctedInitialAge + residentTime;
    }

    /// <summary>Updates this cache entry from a 304 Not Modified validation response.</summary>
    public async ValueTask UpdateFromValidationResponse(HttpResponseMessage validationResponse, DateTimeOffset requestTime, DateTimeOffset responseTime, CancellationToken cancellationToken)
    {
        // RFC 7234 Section 4.3.4: Update metadata from 304 response

        // Update cache control if provided
        var cacheControl = validationResponse.Headers.CacheControl;
        var updatedMaxAge = false;
        if (cacheControl is not null)
        {
            if (cacheControl.MaxAge is not null && cacheControl.MaxAge != MaxAge)
            {
                MaxAge = cacheControl.MaxAge;
                updatedMaxAge = true;
            }

            if (cacheControl.SharedMaxAge is not null && cacheControl.SharedMaxAge != SharedMaxAge)
            {
                SharedMaxAge = cacheControl.SharedMaxAge;
                updatedMaxAge = true;
            }

            MustRevalidate = cacheControl.MustRevalidate || cacheControl.ProxyRevalidate;
            ResponseNoCache = cacheControl.NoCache;
        }

        // If the 304 response provides a new max-age, treat it as a fresh revalidation
        // and update the timing. Otherwise, preserve original timing so age continues to accumulate.
        if (updatedMaxAge || validationResponse.Headers.Date is not null)
        {
            RequestTime = requestTime;
            ResponseTime = responseTime;
            ResponseDate = validationResponse.Headers.Date ?? responseTime;
        }

        AgeValue = validationResponse.Headers.Age ?? TimeSpan.Zero;

        // Update validators if provided
        if (validationResponse.Headers.ETag is not null)
        {
            ETag = validationResponse.Headers.ETag.ToString();
        }

        // RFC 7232: Last-Modified can be in content headers or response headers
        if (validationResponse.Content.Headers.LastModified is not null)
        {
            LastModified = validationResponse.Content.Headers.LastModified;
        }
        else if (validationResponse.Headers.TryGetValues("Last-Modified", out var lastModifiedValues))
        {
            var lastModifiedValue = lastModifiedValues.FirstOrDefault();
            if (lastModifiedValue is not null && DateTimeOffset.TryParse(lastModifiedValue, CultureInfo.InvariantCulture, out var parsedDate))
            {
                LastModified = parsedDate;
            }
        }

        // Update Expires if provided
        var newExpires = ParseExpiresHeader(validationResponse);
        if (newExpires is not null)
        {
            Expires = newExpires;
        }

        // RFC 7234 Section 4.3.4: Update stored response headers with headers from 304 response
        // The cache MUST update the stored response with header fields provided in the 304 response
        await UpdateSerializedResponseHeaders(validationResponse, cancellationToken);
    }

    private async ValueTask UpdateSerializedResponseHeaders(HttpResponseMessage validationResponse, CancellationToken cancellationToken)
    {
        // Deserialize the stored response
        var storedResponse = ResponseSerializer.Deserialize(SerializedResponse);

        // Collect headers to update from the 304 response
        // RFC 7234 Section 4.3.4: Update stored response headers, but exclude headers
        // that are managed as metadata properties (Cache-Control, Date, ETag, Expires, Last-Modified)
        var headersToUpdate = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in validationResponse.Headers)
        {
            // Skip headers managed separately via properties
            if (string.Equals(header.Key, "Cache-Control", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Date", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Expires", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            headersToUpdate[header.Key] = header.Value.ToArray();
        }

        foreach (var header in validationResponse.Content.Headers)
        {
            // Skip headers managed separately via properties
            if (string.Equals(header.Key, "Last-Modified", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            headersToUpdate[header.Key] = header.Value.ToArray();
        }

        // Update existing headers in the stored response while preserving order
        // First, update headers that exist in both
        var existingHeaders = storedResponse.Headers.ToList();
        foreach (var header in existingHeaders)
        {
            if (headersToUpdate.TryGetValue(header.Key, out var newValue))
            {
                storedResponse.Headers.Remove(header.Key);
                storedResponse.Headers.TryAddWithoutValidation(header.Key, newValue);
                headersToUpdate.Remove(header.Key);
            }
        }

        // Update existing content headers
        if (storedResponse.Content?.Headers is not null)
        {
            var existingContentHeaders = storedResponse.Content.Headers.ToList();
            foreach (var header in existingContentHeaders)
            {
                if (headersToUpdate.TryGetValue(header.Key, out var newValue))
                {
                    storedResponse.Content.Headers.Remove(header.Key);
                    storedResponse.Content.Headers.TryAddWithoutValidation(header.Key, newValue);
                    headersToUpdate.Remove(header.Key);
                }
            }
        }

        // Add any new headers from the validation response
        foreach (var kvp in headersToUpdate)
        {
            // Try adding to response headers first, then content headers if that fails
            if (!storedResponse.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value))
            {
                storedResponse.Content.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }
        }

        // Remove Age header as it's calculated dynamically in CreateCachedResponse
        storedResponse.Headers.Remove("Age");

        // Re-serialize the updated response
        SerializedResponse = await ResponseSerializer.SerializeAsync(storedResponse, cancellationToken);

        storedResponse.Dispose();
    }
}

