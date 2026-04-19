using System;

namespace Meziantou.Framework.SnapshotTesting;

public readonly record struct SnapshotType(string Type, string? MimeType = null, string? DisplayName = null)
{
    public static SnapshotType Default { get; } = new("txt", "text/plain", "Text");
    public static SnapshotType Png { get; } = new("png", "image/png", "PNG image");

    public string FileExtension => $".{Type}";

    public static SnapshotType Create(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return Default;

        var nameSpan = name.AsSpan();
        if (nameSpan.StartsWith('.'))
        {
            nameSpan = nameSpan.Slice(1);
        }

        foreach (var prop in typeof(SnapshotType).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (prop.GetValue(null) is SnapshotType snapshotType && nameSpan.Equals(snapshotType.Type, StringComparison.OrdinalIgnoreCase))
                return snapshotType;
        }

        return new SnapshotType(nameSpan.ToString());
    }

    public bool Equals(SnapshotType other) => StringComparer.Ordinal.Equals(Type, other.Type);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Type);
}
