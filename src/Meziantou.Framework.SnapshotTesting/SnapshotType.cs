namespace Meziantou.Framework.SnapshotTesting;

public sealed class SnapshotType : IEquatable<SnapshotType>
{
    public static SnapshotType Default { get; } = new("txt", "text/plain", "Text");
    public static SnapshotType Png { get; } = new("png", "image/png", "PNG image");

    private static readonly Dictionary<string, SnapshotType> Cache = new(StringComparer.OrdinalIgnoreCase)
    {
        ["txt"] = Default,
        ["png"] = Png,
    };

    private SnapshotType(string type, string? mimeType = null, string? displayName = null)
    {
        Type = type;
        MimeType = mimeType;
        DisplayName = displayName;
    }

    public string Type { get; }
    public string? MimeType { get; }
    public string? DisplayName { get; }
    public string FileExtension => $".{Type}";

    public static SnapshotType Create(string type, string? mimeType, string? displayName)
        => new(type, mimeType, displayName);

    public static SnapshotType Create(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return Default;

        var nameSpan = name.AsSpan();
        if (nameSpan.StartsWith('.'))
        {
            nameSpan = nameSpan[1..];
            name = null;
        }

#if NET9_0_OR_GREATER
        if (Cache.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(nameSpan, out var snapshotType))
            return snapshotType;
#else
        if (Cache.TryGetValue(name ?? nameSpan.ToString(), out var snapshotType))
            return snapshotType;
#endif

        return new SnapshotType(name ?? nameSpan.ToString());
    }

    public bool Equals(SnapshotType? other) => other is not null && StringComparer.Ordinal.Equals(Type, other.Type);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Type);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SnapshotType snapshotType && Equals(snapshotType);

    public static bool operator ==(SnapshotType? left, SnapshotType? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SnapshotType? left, SnapshotType? right) => !(left == right);

    public static implicit operator SnapshotType(string? name) => Create(name);
}
