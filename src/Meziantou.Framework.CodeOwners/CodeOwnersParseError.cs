using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Describes why a CODEOWNERS file is invalid, and where.
/// <example>
/// <code>
/// if (!CodeOwnersParser.TryParse("[Section\n* @user1", out var entries, out var error))
/// {
///     // error.Kind: CodeOwnersParseErrorKind.UnterminatedSectionHeader
///     // error.LineNumber: 1
///     // error.LinePosition: 1
/// }
/// </code>
/// </example>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersParseError : IEquatable<CodeOwnersParseError>
{
    internal CodeOwnersParseError(CodeOwnersParseErrorKind kind, int lineNumber, int linePosition)
    {
        Kind = kind;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>Gets the kind of error.</summary>
    public CodeOwnersParseErrorKind Kind { get; }

    /// <summary>Gets the one-based number of the line containing the error, or 0 when the position is unknown.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the one-based position of the error within its line, or 0 when the position is unknown.</summary>
    public int LinePosition { get; }

    /// <summary>Returns a description of the error and of its location.</summary>
    public override string ToString() => $"line {LineNumber}, position {LinePosition}: {GetDescription(Kind)}";

    private static string GetDescription(CodeOwnersParseErrorKind kind) => kind switch
    {
        CodeOwnersParseErrorKind.UnterminatedSectionHeader => "the section header is not terminated by ']'",
        CodeOwnersParseErrorKind.UnterminatedRequiredReviewerCount => "the required reviewer count is not terminated by ']'",
        CodeOwnersParseErrorKind.InvalidRequiredReviewerCount => "the required reviewer count is not a positive integer",
        CodeOwnersParseErrorKind.EmptyOwner => "'@' does not identify an owner",
        CodeOwnersParseErrorKind.InvalidOwner => "the owner is neither a username nor an email address",
        _ => kind.ToString(),
    };

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is CodeOwnersParseError error && Equals(error);
    }

    public bool Equals(CodeOwnersParseError other)
    {
        return Kind == other.Kind &&
               LineNumber == other.LineNumber &&
               LinePosition == other.LinePosition;
    }

    public override int GetHashCode() => HashCode.Combine(Kind, LineNumber, LinePosition);

    public static bool operator ==(CodeOwnersParseError left, CodeOwnersParseError right) => left.Equals(right);
    public static bool operator !=(CodeOwnersParseError left, CodeOwnersParseError right) => !(left == right);
}
