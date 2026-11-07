/**
 * Returns the IANA timezone identifier of the browser (for instance "Europe/Paris"),
 * or null when the environment does not expose it.
 * @returns {string | null}
 */
export function blazorGetTimezone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
}

/**
 * Returns the offset from UTC, in minutes, at the given instant. The offset of a timezone depends on the
 * instant it is evaluated at because of daylight saving time, so the caller must pass the instant of interest.
 * @param {number | null} epochMilliseconds Milliseconds since the Unix epoch, or null for the current instant.
 * @returns {number}
 */
export function blazorGetTimezoneOffset(epochMilliseconds) {
    const date = epochMilliseconds === null || epochMilliseconds === undefined
        ? new Date()
        : new Date(epochMilliseconds);
    return date.getTimezoneOffset();
}
