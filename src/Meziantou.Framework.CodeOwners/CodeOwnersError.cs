using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Describes why a CODEOWNERS file is invalid, and where.
/// <example>
/// <code>
/// if (!CodeOwnersParser.TryParse("[Section\n* @user1", out var entries, out var error))
/// {
///     // error.Kind: CodeOwnersErrorKind.UnterminatedSectionHeader
///     // error.LineNumber: 1
///     // error.LinePosition: 1
/// }
/// </code>
/// </example>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersError : IEquatable<CodeOwnersError>
{
    internal CodeOwnersError(CodeOwnersErrorKind kind, int lineNumber, int linePosition)
    {
        Kind = kind;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>Gets the kind of error.</summary>
    public CodeOwnersErrorKind Kind { get; }

    /// <summary>Gets the one-based number of the line containing the error, or 0 when the position is unknown.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the one-based position of the error within its line, or 0 when the position is unknown.</summary>
    public int LinePosition { get; }

    /// <summary>Returns a description of the error and of its location.</summary>
    public override string ToString() => $"line {LineNumber}, position {LinePosition}: {GetDescription(Kind)}";

    private static string GetDescription(CodeOwnersErrorKind kind) => kind switch
    {
        CodeOwnersErrorKind.UnterminatedSectionHeader => "the section header is not terminated by ']'",
        CodeOwnersErrorKind.UnterminatedRequiredReviewerCount => "the required reviewer count is not terminated by ']'",
        CodeOwnersErrorKind.InvalidRequiredReviewerCount => "the required reviewer count is not a positive integer",
        CodeOwnersErrorKind.EmptyOwner => "'@' does not identify an owner",
        CodeOwnersErrorKind.InvalidOwner => "the owner is neither a username nor an email address",
        _ => kind.ToString(),
    };

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is CodeOwnersError error && Equals(error);
    }

    public bool Equals(CodeOwnersError other)
    {
        return Kind == other.Kind &&
               LineNumber == other.LineNumber &&
               LinePosition == other.LinePosition;
    }

    public override int GetHashCode() => HashCode.Combine(Kind, LineNumber, LinePosition);

    public static bool operator ==(CodeOwnersError left, CodeOwnersError right) => left.Equals(right);
    public static bool operator !=(CodeOwnersError left, CodeOwnersError right) => !(left == right);
}
