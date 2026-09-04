using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a section in a CODEOWNERS file, which groups patterns and defines review requirements.
/// <example>
/// <code>
/// // Parse a CODEOWNERS file with sections
/// var content = """
///     [Backend][2] @backend-team
///     *.cs @csharp-owner
///
///     ^[Optional]
///     docs/* @docs-owner
///     """;
/// var entries = CodeOwnersParser.Parse(content);
/// // entries[0].Section: Name="Backend", RequiredReviewerCount=2
/// // entries[1].Section: Name="Optional", IsOptional=true
/// </code>
/// </example>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersSection : IEquatable<CodeOwnersSection>
{
    internal CodeOwnersSection(string name, int requiredReviewerCount = 1, IReadOnlyList<CodeOwner>? defaultOwners = null)
    {
        Name = name;
        RequiredReviewerCount = requiredReviewerCount;
        DefaultOwners = defaultOwners ?? [];
    }

    /// <summary>Gets the name of the section.</summary>
    public string Name { get; }

    /// <summary>Gets the number of required reviewers for this section. A value of 0 indicates an optional section.</summary>
    public int RequiredReviewerCount { get; }

    /// <summary>Gets a value indicating whether this section is optional (requires 0 reviewers).</summary>
    public bool IsOptional => RequiredReviewerCount is 0;

    /// <summary>Gets a value indicating whether this section is mandatory (requires at least 1 reviewer).</summary>
    public bool IsMandatory => !IsOptional;

    /// <summary>Gets the owners used by the patterns of this section that do not declare any owner.</summary>
    public IReadOnlyList<CodeOwner> DefaultOwners { get; }

    /// <summary>Gets a value indicating whether this section has default owners defined.</summary>
    public bool HasDefaultOwners => DefaultOwners.Count > 0;

    /// <summary>Returns the section header as written in a CODEOWNERS file.</summary>
    public override string ToString()
    {
        string result;
        if (IsOptional)
        {
            result = "^[";
        }
        else
        {
            result = "[";
        }

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

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is CodeOwnersSection section && Equals(section);
    }

    public bool Equals(CodeOwnersSection other)
    {
        return Name == other.Name &&
               RequiredReviewerCount == other.RequiredReviewerCount &&
               DefaultOwners.SequenceEqual(other.DefaultOwners);
    }

    public override int GetHashCode() => HashCode.Combine(Name, RequiredReviewerCount, DefaultOwners.Count);

    public static bool operator ==(CodeOwnersSection left, CodeOwnersSection right) => left.Equals(right);
    public static bool operator !=(CodeOwnersSection left, CodeOwnersSection right) => !(left == right);
}
