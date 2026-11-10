namespace Meziantou.Framework.FastEnumGenerator.FuzzEnums;

/// <summary>The parse calls of an enum: the intercepted System.Enum overloads and the generated members.</summary>
public sealed class EnumParsers<T>
    where T : struct, Enum
{
    public required IReadOnlyList<ParseDelegate<T>> Intercepted { get; init; }

    /// <summary>The overloads without an <c>ignoreCase</c> parameter, which ignore the one they receive.</summary>
    public required IReadOnlyList<ParseDelegate<T>> InterceptedCaseSensitive { get; init; }

    public required IReadOnlyList<TryParseDelegate<T>> InterceptedTry { get; init; }

    /// <summary>The overloads without an <c>ignoreCase</c> parameter, which ignore the one they receive.</summary>
    public required IReadOnlyList<TryParseDelegate<T>> InterceptedTryCaseSensitive { get; init; }

    public required IReadOnlyList<ParseDelegate<T>> Generated { get; init; }

    public required IReadOnlyList<TryParseDelegate<T>> GeneratedTry { get; init; }

    /// <summary>The intercepted <c>ToString</c>.</summary>
    public required Func<T, string> Format { get; init; }
}
