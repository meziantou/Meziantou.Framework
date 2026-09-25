namespace Meziantou.Framework.Language.Toml.Internals;

/// <summary>Every diagnostic the TOML parser can produce.</summary>
internal static class TomlDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor ExpectedSectionName = Error("TOML0001", "Expected a table name", "Expected a TOML table name.");
    public static readonly DiagnosticDescriptor ExpectedClosingBracket = Error("TOML0002", "Expected a closing bracket", "Expected ']'.");
    public static readonly DiagnosticDescriptor ExpectedKey = Error("TOML0003", "Expected a key", "Expected a TOML key.");
    public static readonly DiagnosticDescriptor ExpectedSeparator = Error("TOML0004", "Expected a key/value separator", "Expected '='.");
    public static readonly DiagnosticDescriptor UnexpectedToken = Error("TOML0005", "Unexpected token", "Unexpected token '{0}'.");
    public static readonly DiagnosticDescriptor InvalidValue = Error("TOML0006", "Invalid value", "Invalid TOML value '{0}'.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);
}
