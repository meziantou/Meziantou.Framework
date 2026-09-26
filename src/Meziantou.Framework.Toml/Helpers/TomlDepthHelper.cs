using System.Runtime.CompilerServices;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Helpers;

internal static class TomlDepthHelper
{
    public const int DefaultMaxDepth = 64;

    public static int GetEffectiveMaxDepth(int maxDepth)
        => maxDepth == 0 ? DefaultMaxDepth : maxDepth;

    public static string GetMaxDepthExceededMessage(int effectiveMaxDepth)
        => $"The maximum depth of {effectiveMaxDepth} has been exceeded. Change {nameof(TomlSerializerOptions)}.{nameof(TomlSerializerOptions.MaxDepth)} to allow deeper nesting.";

    public const string InsufficientExecutionStackMessage = "The document is nested too deeply for the stack of the current thread. Reduce " + nameof(TomlSerializerOptions) + "." + nameof(TomlSerializerOptions.MaxDepth) + " or use a thread with a larger stack.";

    // MaxDepth has no upper limit, so the recursive readers and writers also check the stack before going one level deeper
    public static bool HasSufficientExecutionStack() => RuntimeHelpers.TryEnsureSufficientExecutionStack();

    public static void EnsureSufficientExecutionStack(TomlSourceSpan? span = null)
    {
        if (HasSufficientExecutionStack())
        {
            return;
        }

        if (span is { } locatedSpan)
        {
            throw new TomlException(locatedSpan, InsufficientExecutionStackMessage);
        }

        throw new TomlException(InsufficientExecutionStackMessage);
    }

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
