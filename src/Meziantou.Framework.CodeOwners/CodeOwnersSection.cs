using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a section in a CODEOWNERS file.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersSection : IEquatable<CodeOwnersSection>
{
    internal CodeOwnersSection(string name, int requiredReviewerCount = 1, IReadOnlyCollection<string>? defaultOwners = null)
    {
        Name = name;
        RequiredReviewerCount = requiredReviewerCount;
        DefaultOwners = defaultOwners ?? [];
    }

    /// <summary>
    /// Gets the name of the section.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the number of required reviewers for this section.
    /// </summary>
    public int RequiredReviewerCount { get; }

    /// <summary>
    /// Gets a value indicating whether this section is optional (requires 0 reviewers).
    /// </summary>
    public bool IsOptional => RequiredReviewerCount is 0;

    /// <summary>
    /// Gets a value indicating whether this section is mandatory (requires at least 1 reviewer).
    /// </summary>
    public bool IsMandatory => !IsOptional;

    /// <summary>
    /// Gets the default owners for this section.
    /// </summary>
    public IReadOnlyCollection<string> DefaultOwners { get; }

    /// <summary>
    /// Gets a value indicating whether this section has default owners.
    /// </summary>
    public bool HasDefaultOwners => DefaultOwners.Count > 0;

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

    public override bool Equals(object? obj)
    {
        return obj is CodeOwnersSection section && Equals(section);
    }

    public bool Equals(CodeOwnersSection other)
    {
        return Name == other.Name &&
               RequiredReviewerCount == other.RequiredReviewerCount &&
               DefaultOwners.SequenceEqual(other.DefaultOwners, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, RequiredReviewerCount, DefaultOwners.Count);
    }

    public static bool operator ==(CodeOwnersSection left, CodeOwnersSection right) => left.Equals(right);
    public static bool operator !=(CodeOwnersSection left, CodeOwnersSection right) => !(left == right);
}
