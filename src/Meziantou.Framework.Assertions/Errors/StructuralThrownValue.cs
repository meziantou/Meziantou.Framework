namespace Meziantou.Framework.Assertions;

/// <summary>Stands for the value of a member whose getter threw, so the member can still be compared and reported.</summary>
internal sealed class StructuralThrownValue(Exception exception)
{
    public Type ExceptionType { get; } = exception.GetType();

    public override string ToString() => "<threw " + ExceptionType.FullName + ">";
}
