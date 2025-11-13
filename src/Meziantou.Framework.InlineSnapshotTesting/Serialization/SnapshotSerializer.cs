namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

/// <summary>Provides methods for serializing objects to snapshot strings.</summary>
public abstract class SnapshotSerializer
{
    /// <summary>Serializes an object to a string representation.</summary>
    /// <param name="value">The object to serialize.</param>
    /// <returns>The serialized string representation of the object.</returns>
    public abstract string? Serialize(object? value);
}
