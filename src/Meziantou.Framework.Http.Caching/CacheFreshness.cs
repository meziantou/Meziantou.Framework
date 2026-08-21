namespace Meziantou.Framework.Http.Caching;

/// <summary>Freshness and age computations shared by the cache and the cache stores.</summary>
internal static class CacheFreshness
{
    /// <summary>Computes the freshness lifetime per RFC 7234 Section 4.2.1.</summary>
    public static TimeSpan GetFreshnessLifetime(TimeSpan? sharedMaxAge, TimeSpan? maxAge, DateTimeOffset? expires, DateTimeOffset responseDate, DateTimeOffset? lastModified)
    {
        // 1. Use s-maxage if present (takes precedence for shared caches), otherwise max-age
        if (sharedMaxAge.HasValue)
            return sharedMaxAge.Value;

        if (maxAge.HasValue)
            return maxAge.Value;

        // 2. Use Expires - Date if present
        if (expires.HasValue)
        {
            if (expires.Value == DateTimeOffset.MinValue)
                return TimeSpan.Zero; // Already expired

            var freshness = expires.Value - responseDate;
            return freshness > TimeSpan.Zero ? freshness : TimeSpan.Zero;
        }

        // 3. Heuristic freshness (RFC 7234 Section 4.2.2)
        // Use 10% of time since Last-Modified
        if (lastModified.HasValue)
        {
            var age = responseDate - lastModified.Value;
            if (age > TimeSpan.Zero)
                return TimeSpan.FromSeconds(age.TotalSeconds * 0.1);
        }

        // No explicit expiration and no heuristic available
        return TimeSpan.Zero;
    }

    /// <summary>Computes the corrected initial age per RFC 7234 Section 4.2.3.</summary>
    public static TimeSpan GetCorrectedInitialAge(DateTimeOffset requestTime, DateTimeOffset responseTime, DateTimeOffset responseDate, TimeSpan ageValue)
    {
        // apparent_age = max(0, response_time - date_value)
        var apparentAge = responseTime - responseDate;
        if (apparentAge < TimeSpan.Zero)
            apparentAge = TimeSpan.Zero;

        // corrected_age_value = age_value + response_delay
        var correctedAgeValue = ageValue + (responseTime - requestTime);

        // corrected_initial_age = max(apparent_age, corrected_age_value)
        return apparentAge > correctedAgeValue ? apparentAge : correctedAgeValue;
    }

    /// <summary>Computes the current age per RFC 7234 Section 4.2.3.</summary>
    public static TimeSpan GetCurrentAge(DateTimeOffset requestTime, DateTimeOffset responseTime, DateTimeOffset responseDate, TimeSpan ageValue, DateTimeOffset now)
    {
        // current_age = corrected_initial_age + resident_time
        return GetCorrectedInitialAge(requestTime, responseTime, responseDate, ageValue) + (now - responseTime);
    }
}
