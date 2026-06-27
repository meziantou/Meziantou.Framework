namespace Meziantou.Framework.Assertions;

internal sealed class StructuralMissingValue
{
    public static StructuralMissingValue Instance { get; } = new();

    private StructuralMissingValue()
    {
    }
}
