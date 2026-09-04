namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a section in a CODEOWNERS file, which groups patterns and defines review requirements.
/// <example>
/// <code>
/// var content = """
///     [Backend][2] @backend-team
///     *.cs @csharp-owner
///
///     ^[Optional]
///     docs/* @docs-owner
///     """;
/// var file = CodeOwnersFile.Parse(content);
/// // file.Entries[0].Section: Name="Backend", RequiredReviewerCount=2
/// // file.Entries[1].Section: Name="Optional", IsOptional=true
/// </code>
/// </example>
/// </summary>
public sealed class CodeOwnersSection : IEquatable<CodeOwnersSection>
{
    internal CodeOwnersSection(string name, bool isOptional = false, int requiredReviewerCount = 1, IReadOnlyList<CodeOwner>? defaultOwners = null)
    {
        Name = name;
        IsOptional = isOptional;
        RequiredReviewerCount = requiredReviewerCount;
        DefaultOwners = defaultOwners ?? [];
    }

    /// <summary>Gets the name of the section.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether this section is optional, meaning its owners are requested for review but their approval is not required.</summary>
    public bool IsOptional { get; }

    /// <summary>Gets a value indicating whether the approval of this section's owners is required.</summary>
    public bool IsMandatory => !IsOptional;

    /// <summary>Gets the number of reviewers declared by the section header, or 1 when the header does not declare one.</summary>
    /// <remarks>The value is the one written in the file even when <see cref="IsOptional"/> is <see langword="true"/>, in which case no approval is required regardless of the count.</remarks>
    public int RequiredReviewerCount { get; }

    /// <summary>Gets the owners used by the patterns of this section that do not declare any owner.</summary>
    public IReadOnlyList<CodeOwner> DefaultOwners { get; }

    /// <summary>Gets a value indicating whether this section has default owners defined.</summary>
    public bool HasDefaultOwners => DefaultOwners.Count > 0;

    /// <summary>Returns the section header as written in a CODEOWNERS file.</summary>
    public override string ToString()
    {
        var result = IsOptional ? "^[" : "[";
        result += Name + ']';
        if (RequiredReviewerCount > 1)
        {
            result += $"[{RequiredReviewerCount}]";
        }

        if (HasDefaultOwners)
        {
            result += " " + string.Join(' ', DefaultOwners);
        }

        return result;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CodeOwnersSection);

    public bool Equals([NotNullWhen(true)] CodeOwnersSection? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return string.Equals(Name, other.Name, StringComparison.Ordinal) &&
               IsOptional == other.IsOptional &&
               RequiredReviewerCount == other.RequiredReviewerCount &&
               DefaultOwners.SequenceEqual(other.DefaultOwners);
    }

    public override int GetHashCode() => HashCode.Combine(Name, IsOptional, RequiredReviewerCount, DefaultOwners.Count);

    public static bool operator ==(CodeOwnersSection? left, CodeOwnersSection? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(CodeOwnersSection? left, CodeOwnersSection? right) => !(left == right);
}
