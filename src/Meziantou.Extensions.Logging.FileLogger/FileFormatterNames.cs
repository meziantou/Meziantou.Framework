namespace Meziantou.Extensions.Logging;

/// <summary>Contains the names of the built-in <see cref="FileFormatter"/> implementations.</summary>
public static class FileFormatterNames
{
    /// <summary>The name of the <see cref="SimpleFileFormatter"/>, which writes one human-readable line per log entry.</summary>
    public const string Simple = "simple";

    /// <summary>The name of the <see cref="JsonFileFormatter"/>, which writes one JSON object per log entry.</summary>
    public const string Json = "json";
}
