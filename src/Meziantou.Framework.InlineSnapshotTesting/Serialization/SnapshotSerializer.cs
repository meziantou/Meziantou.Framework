namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

/// <summary>
/// Provides a base class for snapshot serializers that convert objects to their string representation.
/// </summary>
public abstract class SnapshotSerializer
{
    /// <summary>
    /// Serializes the specified value to its string representation.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A string representation of the value, or <see langword="null"/> if the value cannot be serialized.</returns>
    public abstract string? Serialize(object? value);
}
