namespace Meziantou.Framework.DiffEngine;

internal static class TargetPosition
{
    public static bool TargetOnLeft => string.Equals(Environment.GetEnvironmentVariable("DiffEngine_TargetOnLeft"), "true", StringComparison.OrdinalIgnoreCase);
}
