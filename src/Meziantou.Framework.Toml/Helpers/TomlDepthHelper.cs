using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Helpers;

internal static class TomlDepthHelper
{
    public const int DefaultMaxDepth = 64;

    public static int GetEffectiveMaxDepth(int maxDepth)
        => maxDepth == 0 ? DefaultMaxDepth : maxDepth;

    public static string GetMaxDepthExceededMessage(int effectiveMaxDepth)
        => $"The maximum depth of {effectiveMaxDepth} has been exceeded. Change {nameof(TomlSerializerOptions)}.{nameof(TomlSerializerOptions.MaxDepth)} to allow deeper nesting.";

    public static void ThrowDepthExceeded(int effectiveMaxDepth, TomlSourceSpan? span = null)
    {
        var message = GetMaxDepthExceededMessage(effectiveMaxDepth);
        if (span is { } locatedSpan)
        {
            throw new TomlException(locatedSpan, message);
        }

        throw new TomlException(message);
    }
}
