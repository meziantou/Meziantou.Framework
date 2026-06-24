namespace Meziantou.Framework.RobotsTxt;

/// <summary>Identifies the kind of parse error encountered while reading a <c>robots.txt</c> file.</summary>
public enum RobotsParseErrorKind
{
    /// <summary>A line did not contain the expected <c>directive: value</c> format (missing colon separator).</summary>
    MalformedLine,

    /// <summary>A directive name was not recognised by the parser.</summary>
    UnknownDirective,

    /// <summary>The value of a <c>Crawl-delay</c> directive could not be parsed as a non-negative number.</summary>
    InvalidCrawlDelay,
}
