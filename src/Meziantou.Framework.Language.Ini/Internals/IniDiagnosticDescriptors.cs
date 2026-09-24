namespace Meziantou.Framework.Language.Ini.Internals;

/// <summary>Every diagnostic the INI parser can produce.</summary>
internal static class IniDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor ExpectedSectionName = Error("INI0001", "Expected a section name", "Expected an INI section name.");
    public static readonly DiagnosticDescriptor ExpectedClosingBracket = Error("INI0002", "Expected a closing bracket", "Expected ']'.");
    public static readonly DiagnosticDescriptor ExpectedKey = Error("INI0003", "Expected a key", "Expected an INI key.");
    public static readonly DiagnosticDescriptor ExpectedSeparator = Error("INI0004", "Expected a key/value separator", "Expected '=' or ':'.");
    public static readonly DiagnosticDescriptor UnexpectedToken = Error("INI0005", "Unexpected token", "Unexpected token '{0}'.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);
}
