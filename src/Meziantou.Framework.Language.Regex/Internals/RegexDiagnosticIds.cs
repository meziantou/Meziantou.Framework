namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The identifiers the parsers report diagnostics under.</summary>
/// <remarks>
/// <para>
/// The identifiers are banded: <c>REGEX0001</c>-<c>REGEX0019</c> are structural facts about brackets, parentheses, and
/// quantifiers that every flavor shares; <c>REGEX0030</c>-<c>REGEX0034</c> concern escapes, backreferences, and Unicode
/// properties, which POSIX has none of; <c>REGEX0050</c>-<c>REGEX0056</c> are constructs only .NET has;
/// <c>REGEX0070</c>, <c>REGEX0090</c>, and <c>REGEX0110</c> are reserved for JavaScript, PCRE, and POSIX; and
/// <c>REGEX0200</c> and up belong to the parser itself rather than to the grammar.
/// </para>
/// <para>
/// A construct the flavor does not have is not reported at all. It is simply not that construct: <c>\A</c> is the
/// letter <c>A</c> where there is no such anchor, which is what an engine without it does.
/// </para>
/// <para>
/// The identifiers in the first three bands correspond one to one with the members of the engine's
/// <c>RegexParseError</c>, which is what makes the differential test against the runtime meaningful.
/// </para>
/// </remarks>
internal static class RegexDiagnosticIds
{
    // ---- structural, every flavor ----
    public const string InsufficientOpeningParentheses = "REGEX0001";
    public const string InsufficientClosingParentheses = "REGEX0002";
    public const string UnterminatedBracket = "REGEX0003";
    public const string UnescapedEndingBackslash = "REGEX0004";
    public const string QuantifierAfterNothing = "REGEX0005";
    public const string NestedQuantifiersNotParenthesized = "REGEX0006";
    public const string ReversedQuantifierRange = "REGEX0007";
    public const string QuantifierOrCaptureGroupOutOfRange = "REGEX0008";
    public const string ReversedCharacterRange = "REGEX0009";
    public const string InvalidGroupingConstruct = "REGEX0010";
    public const string UnterminatedComment = "REGEX0011";
    public const string InsufficientOrInvalidHexDigits = "REGEX0012";
    public const string MissingControlCharacter = "REGEX0013";
    public const string UnrecognizedControlCharacter = "REGEX0014";
    public const string UnrecognizedEscape = "REGEX0015";
    public const string ShorthandClassInCharacterRange = "REGEX0016";
    public const string CaptureGroupNameInvalid = "REGEX0017";
    public const string CaptureGroupOfZero = "REGEX0018";
    public const string MalformedNamedReference = "REGEX0019";

    // ---- escapes, references, and Unicode properties ----
    public const string UndefinedNumberedReference = "REGEX0030";
    public const string UndefinedNamedReference = "REGEX0031";
    public const string InvalidUnicodePropertyEscape = "REGEX0032";
    public const string MalformedUnicodePropertyEscape = "REGEX0033";
    public const string UnrecognizedUnicodeProperty = "REGEX0034";

    // ---- .NET only ----
    public const string ExclusionGroupNotLast = "REGEX0050";
    public const string AlternationHasTooManyConditions = "REGEX0051";
    public const string AlternationHasNamedCapture = "REGEX0053";
    public const string AlternationHasComment = "REGEX0054";
    public const string AlternationHasMalformedReference = "REGEX0055";
    public const string AlternationHasUndefinedReference = "REGEX0056";

    // ---- the parser itself ----
    public const string MaxRecursionDepthExceeded = "REGEX0200";
    public const string TrailingContent = "REGEX0204";
    public const string UnknownFlag = "REGEX0205";
    public const string DuplicateFlag = "REGEX0206";
    public const string ConflictingFlags = "REGEX0207";
    public const string LineTerminatorInLiteral = "REGEX0208";
}
