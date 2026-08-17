namespace Meziantou.Framework.Annotations;

/// <summary>
/// Specifies the strategy used by the generated <c>New()</c> method to create new <see cref="Guid"/> values.
/// </summary>
public enum GuidGenerationStrategy
{
    /// <summary>No <c>New()</c> method is generated.</summary>
    None = 0,

    /// <summary>Creates a random UUID (version 4) using <see cref="Guid.NewGuid"/>. This is the strategy used when the property is not set.</summary>
    Version4 = 1,

    /// <summary>Creates a time-ordered UUID (version 7) using <c>Guid.CreateVersion7()</c>. Requires .NET 9 or later.</summary>
    Version7 = 2,
}
