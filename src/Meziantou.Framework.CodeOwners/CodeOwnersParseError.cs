using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Describes why a CODEOWNERS file is invalid, and where.
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs" ref:region="CodeOwnersParseError_Example" ref:dedent="true">
/// var isValid = CodeOwnersFile.TryParse("[Section\n* @user1", CodeOwnersDialect.GitLab, out var file, out var error);
///
/// Assert.False(isValid);
/// Assert.Null(file);
/// Assert.Equal(CodeOwnersParseErrorKind.UnterminatedSectionHeader, error.Kind);
/// Assert.Equal(1, error.LineNumber);
/// Assert.Equal(1, error.LinePosition);
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
    public override string ToString() => Kind is CodeOwnersParseErrorKind.None
        ? "no error"
        : $"line {LineNumber}, position {LinePosition}: {GetDescription(Kind)}";

    private static string GetDescription(CodeOwnersParseErrorKind kind) => kind switch
    {
        CodeOwnersParseErrorKind.None => "no error",
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
