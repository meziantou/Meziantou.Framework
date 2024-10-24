using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

[StructLayout(LayoutKind.Auto)]
public readonly struct CodeOwnersSection : IEquatable<CodeOwnersSection>
{
    internal CodeOwnersSection(string name, int requiredReviewerCount = 1, IReadOnlyCollection<string> defaultOwners = null)
    {
        Name = name;
        RequiredReviewerCount = requiredReviewerCount;
        DefaultOwners = defaultOwners ?? new List<string>();
    }

    public string Name { get; }

    public int RequiredReviewerCount { get; }
    public bool IsOptional => RequiredReviewerCount == 0;
    public bool IsMandatory => !IsOptional;

    public IReadOnlyCollection<string> DefaultOwners { get; }
    public bool HasDefaultOwners => DefaultOwners is not null && DefaultOwners.Count > 0;

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
        var hashCode = 1707150943;
        hashCode = hashCode * -1521134295 + StringComparer.Ordinal.GetHashCode(Name);
        hashCode = hashCode * -1521134295 + RequiredReviewerCount.GetHashCode();
        hashCode = hashCode * -1521134295 + DefaultOwners.GetHashCode();
        return hashCode;
    }

    public static bool operator ==(CodeOwnersSection left, CodeOwnersSection right) => left.Equals(right);
    public static bool operator !=(CodeOwnersSection left, CodeOwnersSection right) => !(left == right);
}
