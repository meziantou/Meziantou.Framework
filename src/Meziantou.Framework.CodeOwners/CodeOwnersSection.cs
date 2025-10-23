using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersSection : IEquatable<CodeOwnersSection>
{
    internal CodeOwnersSection(string name, int requiredReviewerCount = 1, IReadOnlyCollection<string>? defaultOwners = null)
    {
        Name = name;
        RequiredReviewerCount = requiredReviewerCount;
        DefaultOwners = defaultOwners ?? [];
    }

    public string Name { get; }

    public int RequiredReviewerCount { get; }
    public bool IsOptional => RequiredReviewerCount is 0;
    public bool IsMandatory => !IsOptional;

    public IReadOnlyCollection<string> DefaultOwners { get; }
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
