namespace Meziantou.Framework.Language.Toml.Internals;

/// <summary>Every diagnostic the TOML parser can produce.</summary>
internal static class TomlDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor ExpectedKey = Error("TOML0001", "Expected a key", "Expected a key.");
    public static readonly DiagnosticDescriptor ExpectedCharacter = Error("TOML0002", "Expected a character", "Expected '{0}'.");
    public static readonly DiagnosticDescriptor ExpectedValue = Error("TOML0003", "Expected a value", "Expected a value.");
    public static readonly DiagnosticDescriptor ExpectedEndOfLine = Error("TOML0004", "Expected the end of the line", "Expected the end of the line after {0}.");
    public static readonly DiagnosticDescriptor UnexpectedToken = Error("TOML0005", "Unexpected token", "Unexpected token '{0}'.");
    public static readonly DiagnosticDescriptor InvalidValue = Error("TOML0006", "Invalid value", "Invalid value '{0}'.");
    public static readonly DiagnosticDescriptor InvalidNumber = Error("TOML0006", "Invalid number", "Invalid number '{0}'.");
    public static readonly DiagnosticDescriptor IntegerOutOfRange = Error("TOML0006", "Integer out of range", "The integer '{0}' does not fit in 64 bits.");
    public static readonly DiagnosticDescriptor FloatOutOfRange = Error("TOML0006", "Float out of range", "The float '{0}' is too large for a 64-bit float.");
    public static readonly DiagnosticDescriptor InvalidDateTime = Error("TOML0006", "Invalid date or time", "Invalid date or time '{0}'.");
    public static readonly DiagnosticDescriptor UnsupportedDateTime = Error("TOML0006", "Unsupported date or time", "The date or time '{0}' cannot be represented: {1}.");
    public static readonly DiagnosticDescriptor InvalidKey = Error("TOML0007", "Invalid key", "Invalid key '{0}'. A bare key may only contain the characters A-Z, a-z, 0-9, '_' and '-'.");
    public static readonly DiagnosticDescriptor MultiLineStringKey = Error("TOML0007", "Multi-line string key", "A key cannot be a multi-line string.");
    public static readonly DiagnosticDescriptor UnterminatedString = Error("TOML0008", "Unterminated string", "Unterminated string.");
    public static readonly DiagnosticDescriptor InvalidEscapeSequence = Error("TOML0009", "Invalid escape sequence", "Invalid escape sequence '{0}'.");
    public static readonly DiagnosticDescriptor InvalidUnicodeEscapeSequence = Error("TOML0009", "Invalid unicode escape sequence", "The escape sequence '{0}' is not a Unicode scalar value.");
    public static readonly DiagnosticDescriptor InvalidCharacter = Error("TOML0010", "Invalid character", "The character U+{0} is not allowed here.");
    public static readonly DiagnosticDescriptor InvalidWhitespace = Error("TOML0010", "Invalid whitespace", "The character U+{0} is not whitespace in TOML, which only allows spaces and tabs.");
    public static readonly DiagnosticDescriptor BareCarriageReturn = Error("TOML0011", "Carriage return without line feed", "A carriage return must be followed by a line feed.");
    public static readonly DiagnosticDescriptor NestingTooDeep = Error("TOML0012", "Nesting too deep", "Arrays and inline tables cannot nest more than {0} deep.");
    public static readonly DiagnosticDescriptor RequiresNewerVersion = Error("TOML0013", "Requires a newer TOML version", "{0} requires TOML {1}.");
    public static readonly DiagnosticDescriptor DuplicateKey = Error("TOML0020", "Duplicate key", "The key '{0}' is already defined.");
    public static readonly DiagnosticDescriptor DuplicateTable = Error("TOML0021", "Duplicate table", "The table '{0}' is already defined.");
    public static readonly DiagnosticDescriptor ImmutableValue = Error("TOML0022", "Value cannot be extended", "'{0}' is an inline table or an array, which cannot be extended.");
    public static readonly DiagnosticDescriptor NotATable = Error("TOML0023", "Not a table", "'{0}' is already defined as a value, not a table.");
    public static readonly DiagnosticDescriptor TableDefinedByHeader = Error("TOML0024", "Table defined by a header", "The table '{0}' is defined by a table header, so a dotted key cannot add to it.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);
}
