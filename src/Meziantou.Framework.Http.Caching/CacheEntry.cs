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
            entry.MaxAge = cacheControl.SharedMaxAge ?? cacheControl.MaxAge;
            entry.MustRevalidate = cacheControl.MustRevalidate || cacheControl.ProxyRevalidate;
            entry.ResponseNoCache = cacheControl.NoCache;

            // RFC 8246: Parse immutable directive
            entry.Immutable = HasImmutableDirective(cacheControl);
        }

        // Parse Expires header
        entry.Expires = ParseExpiresHeader(response);

        // Parse validators
        entry.ETag = response.Headers.ETag?.ToString();
        entry.LastModified = response.Content.Headers.LastModified;

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
    public DateTimeOffset? Expires { get; private set; }
    public bool MustRevalidate { get; private set; }
    public bool ResponseNoCache { get; private set; }
    public bool Immutable { get; private set; }

    // Validators for conditional requests
    public string? ETag { get; private set; }
    public DateTimeOffset? LastModified { get; private set; }

    public byte[] SerializedResponse { get; private set; }

    /// <summary>Calculates the freshness lifetime per RFC 7234 Section 4.2.1.</summary>
    public TimeSpan FreshnessLifetime
    {
        get
        {
            // 1. Use max-age (or s-maxage) if present
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
    public void UpdateFromValidationResponse(HttpResponseMessage validationResponse, DateTimeOffset responseTime)
    {
        // RFC 7234 Section 4.3.4: Update metadata from 304 response
        ResponseTime = responseTime;
        ResponseDate = validationResponse.Headers.Date ?? responseTime;
        AgeValue = validationResponse.Headers.Age ?? TimeSpan.Zero;

        // Update validators if provided
        if (validationResponse.Headers.ETag != null)
        {
            ETag = validationResponse.Headers.ETag.ToString();
        }

        if (validationResponse.Content.Headers.LastModified != null)
        {
            LastModified = validationResponse.Content.Headers.LastModified;
        }

        // Update cache control if provided
        var cacheControl = validationResponse.Headers.CacheControl;
        if (cacheControl != null)
        {
            MaxAge = cacheControl.SharedMaxAge ?? cacheControl.MaxAge ?? MaxAge;
            MustRevalidate = cacheControl.MustRevalidate || cacheControl.ProxyRevalidate;
            ResponseNoCache = cacheControl.NoCache;
        }

        // Update Expires if provided
        var newExpires = ParseExpiresHeader(validationResponse);
        if (newExpires != null)
        {
            Expires = newExpires;
        }
    }
}

