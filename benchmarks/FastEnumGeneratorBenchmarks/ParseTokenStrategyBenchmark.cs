using System.Buffers;
using BenchmarkDotNet.Attributes;

namespace FastEnumGeneratorBenchmarks;

[MemoryDiagnoser]
public class ParseTokenStrategyBenchmark
{
    private static readonly SearchValues<char> CommaSearchValues = SearchValues.Create(",");
    private static readonly string[] SmallTokens = ["None", "Read", "Write", "Execute", "Delete", "ReadWrite"];
    private static readonly ulong[] SmallValues = [0UL, 1UL, 2UL, 4UL, 8UL, 3UL];
    private static readonly SearchValues<string> SmallSearchValues = SearchValues.Create(SmallTokens, StringComparison.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ulong> SmallLookup = SmallTokens.Zip(SmallValues).ToDictionary(static pair => pair.First, static pair => pair.Second, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] LargeTokens = Enum.GetNames<LargeEnum>();
    private static readonly ulong[] LargeValues = CreateLargeValues();
    private static readonly SearchValues<string> LargeSearchValues = SearchValues.Create(LargeTokens, StringComparison.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ulong> LargeLookup = LargeTokens.Zip(LargeValues).ToDictionary(static pair => pair.First, static pair => pair.Second, StringComparer.OrdinalIgnoreCase);

    [Benchmark]
    public bool ParseFlags_linear_indexof_small() => TryParseFlagsLinearIndexOf("Read, Write", SmallTokens, SmallValues, out _);

    [Benchmark]
    public bool ParseFlags_linear_indexofany_small() => TryParseFlagsLinearIndexOfAny("Read, Write", SmallTokens, SmallValues, out _);

    [Benchmark]
    public bool ParseFlags_searchvalues_indexof_small() => TryParseFlagsSearchValuesIndexOf("Read, Write", SmallSearchValues, SmallLookup, out _);

    [Benchmark]
    public bool ParseFlags_searchvalues_indexofany_small() => TryParseFlagsSearchValuesIndexOfAny("Read, Write", SmallSearchValues, SmallLookup, out _);

    [Benchmark]
    public bool ParseFlags_linear_indexof_large() => TryParseFlagsLinearIndexOf("V010, V020", LargeTokens, LargeValues, out _);

    [Benchmark]
    public bool ParseFlags_linear_indexofany_large() => TryParseFlagsLinearIndexOfAny("V010, V020", LargeTokens, LargeValues, out _);

    [Benchmark]
    public bool ParseFlags_searchvalues_indexof_large() => TryParseFlagsSearchValuesIndexOf("V010, V020", LargeSearchValues, LargeLookup, out _);

    [Benchmark]
    public bool ParseFlags_searchvalues_indexofany_large() => TryParseFlagsSearchValuesIndexOfAny("V010, V020", LargeSearchValues, LargeLookup, out _);

    private static bool TryParseFlagsLinearIndexOf(ReadOnlySpan<char> input, string[] tokens, ulong[] values, out ulong result)
    {
        var remaining = input;
        var parsedValue = 0UL;
        while (true)
        {
            var separatorIndex = MemoryExtensions.IndexOf(remaining, ',');
            var token = separatorIndex >= 0 ? remaining[..separatorIndex] : remaining;
            token = MemoryExtensions.Trim(token);
            if (token.IsEmpty || !TryParseSingleLinear(token, tokens, values, out var tokenValue))
            {
                result = default;
                return false;
            }

            parsedValue |= tokenValue;
            if (separatorIndex < 0)
                break;

            remaining = remaining[(separatorIndex + 1)..];
        }

        result = parsedValue;
        return true;
    }

    private static bool TryParseFlagsLinearIndexOfAny(ReadOnlySpan<char> input, string[] tokens, ulong[] values, out ulong result)
    {
        var remaining = input;
        var parsedValue = 0UL;
        while (true)
        {
            var separatorIndex = MemoryExtensions.IndexOfAny(remaining, CommaSearchValues);
            var token = separatorIndex >= 0 ? remaining[..separatorIndex] : remaining;
            token = MemoryExtensions.Trim(token);
            if (token.IsEmpty || !TryParseSingleLinear(token, tokens, values, out var tokenValue))
            {
                result = default;
                return false;
            }

            parsedValue |= tokenValue;
            if (separatorIndex < 0)
                break;

            remaining = remaining[(separatorIndex + 1)..];
        }

        result = parsedValue;
        return true;
    }

    private static bool TryParseFlagsSearchValuesIndexOf(ReadOnlySpan<char> input, SearchValues<string> searchValues, Dictionary<string, ulong> lookup, out ulong result)
    {
        var remaining = input;
        var parsedValue = 0UL;
        while (true)
        {
            var separatorIndex = MemoryExtensions.IndexOf(remaining, ',');
            var token = separatorIndex >= 0 ? remaining[..separatorIndex] : remaining;
            token = MemoryExtensions.Trim(token);
            if (token.IsEmpty)
            {
                result = default;
                return false;
            }

            var tokenString = token.ToString();
            if (!searchValues.Contains(tokenString) || !lookup.TryGetValue(tokenString, out var tokenValue))
            {
                result = default;
                return false;
            }

            parsedValue |= tokenValue;
            if (separatorIndex < 0)
                break;

            remaining = remaining[(separatorIndex + 1)..];
        }

        result = parsedValue;
        return true;
    }

    private static bool TryParseFlagsSearchValuesIndexOfAny(ReadOnlySpan<char> input, SearchValues<string> searchValues, Dictionary<string, ulong> lookup, out ulong result)
    {
        var remaining = input;
        var parsedValue = 0UL;
        while (true)
        {
            var separatorIndex = MemoryExtensions.IndexOfAny(remaining, CommaSearchValues);
            var token = separatorIndex >= 0 ? remaining[..separatorIndex] : remaining;
            token = MemoryExtensions.Trim(token);
            if (token.IsEmpty)
            {
                result = default;
                return false;
            }

            var tokenString = token.ToString();
            if (!searchValues.Contains(tokenString) || !lookup.TryGetValue(tokenString, out var tokenValue))
            {
                result = default;
                return false;
            }

            parsedValue |= tokenValue;
            if (separatorIndex < 0)
                break;

            remaining = remaining[(separatorIndex + 1)..];
        }

        result = parsedValue;
        return true;
    }

    private static bool TryParseSingleLinear(ReadOnlySpan<char> token, string[] tokens, ulong[] values, out ulong tokenValue)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            if (MemoryExtensions.Equals(token, tokens[i], StringComparison.OrdinalIgnoreCase))
            {
                tokenValue = values[i];
                return true;
            }
        }

        tokenValue = default;
        return false;
    }

    private static ulong[] CreateLargeValues()
    {
        var values = new ulong[LargeTokens.Length];
        for (var i = 0; i < LargeTokens.Length; i++)
        {
            values[i] = (ulong)(int)Enum.Parse<LargeEnum>(LargeTokens[i]);
        }

        return values;
    }
}
