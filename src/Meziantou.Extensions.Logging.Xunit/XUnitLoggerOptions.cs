namespace Meziantou.Extensions.Logging.Xunit;

public sealed class XUnitLoggerOptions
{
    /// <summary>
    /// Includes scopes when <see langword="true" />.
    /// </summary>
    public bool IncludeScopes { get; set; }

    /// <summary>
    /// Includes category when <see langword="true" />.
    /// </summary>
    public bool IncludeCategory { get; set; }

    /// <summary>
    /// Includes log level when <see langword="true" />.
    /// </summary>
    public bool IncludeLogLevel { get; set; }

    /// <summary>
    /// Gets or sets format string used to format timestamp in logging messages. Defaults to <see langword="null" />.
    /// </summary>
    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
    public string? TimestampFormat { get; set; }

    /// <summary>
    /// Gets or sets indication whether or not UTC timezone should be used to format timestamps in logging messages. Defaults to <see langword="false" />.
    /// </summary>
    public bool UseUtcTimestamp { get; set; }
}
