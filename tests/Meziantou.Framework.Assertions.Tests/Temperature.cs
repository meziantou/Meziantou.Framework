namespace Meziantou.Framework.Assertions.Tests;

#pragma warning disable MA0095 // A ref struct cannot be boxed, so Equals(object) can never receive one
#pragma warning disable MA0097 // The documentation sample only needs CompareTo; operators would hide what InRange uses
#region Assert_RefStructType
internal readonly ref struct Temperature(int degrees) : IEquatable<Temperature>, IComparable<Temperature>
{
    public int Degrees { get; } = degrees;

    public bool Equals(Temperature other) => Degrees == other.Degrees;

    public int CompareTo(Temperature other) => Degrees.CompareTo(other.Degrees);
}
#endregion
#pragma warning restore MA0097
#pragma warning restore MA0095
