namespace Meziantou.Framework.Language.Json.Internals;

/// <summary>Every diagnostic the JSON parser can produce.</summary>
internal static class JsonDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor UnterminatedBlockComment = Error("JSON0001", "Unterminated block comment", "Unterminated block comment.");
    public static readonly DiagnosticDescriptor UnterminatedString = Error("JSON0002", "Unterminated string literal", "Unterminated string literal.");
    public static readonly DiagnosticDescriptor InvalidEscapeSequence = Error("JSON0003", "Invalid escape sequence", "Invalid escape sequence.");
    public static readonly DiagnosticDescriptor InvalidUnicodeEscapeSequence = Error("JSON0003", "Invalid unicode escape sequence", "Invalid unicode escape sequence.");
    public static readonly DiagnosticDescriptor LeadingZero = Error("JSON0004", "Leading zero", "JSON numbers cannot contain leading zeroes.");
    public static readonly DiagnosticDescriptor InvalidNumber = Error("JSON0004", "Invalid number literal", "Invalid number literal.");
    public static readonly DiagnosticDescriptor ExpectedFractionDigit = Error("JSON0004", "Missing fraction digit", "Expected at least one digit after the decimal point.");
    public static readonly DiagnosticDescriptor ExpectedExponentDigit = Error("JSON0004", "Missing exponent digit", "Expected at least one digit in the exponent.");
    public static readonly DiagnosticDescriptor UnexpectedComma = Error("JSON0005", "Unexpected comma", "Unexpected comma.");
    public static readonly DiagnosticDescriptor UnexpectedToken = Error("JSON0005", "Unexpected token", "Unexpected token '{0}'.");
    public static readonly DiagnosticDescriptor ExpectedCharacter = Error("JSON0006", "Expected a character", "Expected '{0}'.");
    public static readonly DiagnosticDescriptor ExpectedValue = Error("JSON0007", "Expected a value", "Expected a JSON value.");
    public static readonly DiagnosticDescriptor ExpectedPropertyName = Error("JSON0008", "Expected a property name", "Expected a JSON property name.");
    public static readonly DiagnosticDescriptor ExpectedCommaOrEndOfObject = Error("JSON0009", "Expected a comma or the end of the object", "Expected a comma or the end of the object.");
    public static readonly DiagnosticDescriptor ExpectedCommaOrEndOfArray = Error("JSON0009", "Expected a comma or the end of the array", "Expected a comma or the end of the array.");
    public static readonly DiagnosticDescriptor UnexpectedDataAfterRootValue = Error("JSON0010", "Unexpected data after the root value", "Unexpected data after the root JSON value.");
    public static readonly DiagnosticDescriptor LineBreakInString = Error("JSON0011", "Line break in a string", "Line breaks are not allowed in JSON strings.");
    public static readonly DiagnosticDescriptor NestingTooDeep = Error("JSON0012", "Nesting too deep", "Objects and arrays cannot nest more than {0} deep.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);
}
