using System.Globalization;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>The tag of a MAPI property: a 16-bit identifier and a 16-bit type.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct MapiPropertyTag : IEquatable<MapiPropertyTag>
{
    /// <summary>Initializes a new instance of the <see cref="MapiPropertyTag"/> struct.</summary>
    /// <param name="id">The identifier of the property. Identifiers from 0x8000 are named properties, mapped by each file.</param>
    /// <param name="type">The type of the property.</param>
    public MapiPropertyTag(ushort id, MapiPropertyType type)
    {
        Id = id;
        Type = type;
    }

    /// <summary>Gets the identifier of the property.</summary>
    public ushort Id { get; }

    /// <summary>Gets the type of the property.</summary>
    public MapiPropertyType Type { get; }

    /// <summary>Gets the tag as a 32-bit value: the identifier in the high 16 bits and the type in the low 16 bits.</summary>
    public uint Value => ((uint)Id << 16) | (ushort)Type;

    /// <summary>Gets whether the identifier is in the range of the named properties (0x8000 and above).</summary>
    public bool IsNamed => Id >= 0x8000;

    /// <summary>Creates a tag from its 32-bit value.</summary>
    /// <param name="value">The identifier in the high 16 bits and the type in the low 16 bits.</param>
    /// <returns>The tag.</returns>
    public static MapiPropertyTag FromValue(uint value) => new((ushort)(value >> 16), (MapiPropertyType)(ushort)value);

    /// <inheritdoc />
    public bool Equals(MapiPropertyTag other) => Id == other.Id && Type == other.Type;

    /// <inheritdoc />
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is MapiPropertyTag other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Value;

    /// <summary>Returns the tag as an hexadecimal value, such as <c>0x0037001F</c>.</summary>
    /// <returns>The tag as an hexadecimal value.</returns>
    public override string ToString() => "0x" + Value.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Compares two tags.</summary>
    /// <param name="left">The first tag.</param>
    /// <param name="right">The second tag.</param>
    /// <returns><see langword="true"/> when the tags are equal.</returns>
    public static bool operator ==(MapiPropertyTag left, MapiPropertyTag right) => left.Equals(right);

    /// <summary>Compares two tags.</summary>
    /// <param name="left">The first tag.</param>
    /// <param name="right">The second tag.</param>
    /// <returns><see langword="true"/> when the tags are different.</returns>
    public static bool operator !=(MapiPropertyTag left, MapiPropertyTag right) => !left.Equals(right);
}
