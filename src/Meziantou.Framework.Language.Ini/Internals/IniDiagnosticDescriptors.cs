namespace Meziantou.Framework.Language.Ini.Internals;

/// <summary>Every diagnostic the INI parser can produce.</summary>
internal static class IniDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor ExpectedSectionName = Error("INI0001", "Expected a section name", "Expected an INI section name.");
    public static readonly DiagnosticDescriptor ExpectedClosingBracket = Error("INI0002", "Expected a closing bracket", "Expected ']'.");
    public static readonly DiagnosticDescriptor ExpectedKey = Error("INI0003", "Expected a key", "Expected an INI key.");
    public static readonly DiagnosticDescriptor ExpectedSeparator = Error("INI0004", "Expected a key/value separator", "Expected '=' or ':'.");
    public static readonly DiagnosticDescriptor ExpectedEndOfLine = Error("INI0005", "Expected the end of the line", "Expected the end of the line after {0}, found '{1}'.");
    public static readonly DiagnosticDescriptor DuplicateSection = Warning("INI0006", "Duplicate section", "The section '{0}' is already defined.");
    public static readonly DiagnosticDescriptor DuplicateKey = Warning("INI0007", "Duplicate key", "The key '{0}' is already defined in {1}.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);
    private static DiagnosticDescriptor Warning(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Warning);
}
