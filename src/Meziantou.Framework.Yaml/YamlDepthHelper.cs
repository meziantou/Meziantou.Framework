namespace Meziantou.Framework.Yaml;

internal static class YamlDepthHelper
{
    public const int DefaultMaxDepth = 64;

    public static void ValidateMaxDepth(int maxDepth, string paramName)
    {
        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, maxDepth, "Max depth must be greater than or equal to 0.");
        }
    }

    public static int GetEffectiveMaxDepth(int maxDepth)
    {
        return maxDepth == 0 ? DefaultMaxDepth : maxDepth;
    }

    public static int GetEffectiveMaxDepth(int maxDepth, string paramName)
    {
        ValidateMaxDepth(maxDepth, paramName);
        return GetEffectiveMaxDepth(maxDepth);
    }

    public static YamlException CreateMaxDepthExceededException(int maxDepth)
    {
        return CreateMaxDepthExceededException(maxDepth, Mark.Empty, Mark.Empty, sourceName: null);
    }

    public static YamlException CreateMaxDepthExceededException(int maxDepth, Mark start, Mark end, string? sourceName)
    {
        return new YamlException(sourceName, start, end, $"The maximum nesting depth of {maxDepth} has been exceeded.");
    }
}
