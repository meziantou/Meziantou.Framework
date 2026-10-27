namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A compiled pattern (the [[RegExpMatcher]] of a RegExp object). It is immutable and thread-safe: the state of a
/// match lives in a <see cref="RegExpRunner"/>.
/// </summary>
internal sealed class RegExpProgram
{
    internal RegExpProgram(
        string source,
        RegExpFlags flags,
        int[] code,
        RegExpCharClass[] classes,
        string[] strings,
        RegExpLookaroundInfo[] lookarounds,
        RegExpBackreferenceInfo[] backreferences,
        int registerCount,
        int groupCount,
        string?[] groupNames,
        RegExpSearchInfo search)
    {
        Source = source;
        Flags = flags;
        Code = code;
        Classes = classes;
        Strings = strings;
        Lookarounds = lookarounds;
        Backreferences = backreferences;
        RegisterCount = registerCount;
        GroupCount = groupCount;
        GroupNames = groupNames;
        Search = search;
        foreach (var name in groupNames)
        {
            if (name is not null)
            {
                HasNamedGroups = true;
                break;
            }
        }
    }

    /// <summary>Gets the pattern.</summary>
    public string Source { get; }

    /// <summary>Gets the flags the pattern was compiled with.</summary>
    public RegExpFlags Flags { get; }

    /// <summary>Gets whether the pattern has the "u" or the "v" flag: it matches code points.</summary>
    public bool IsUnicode => Flags.HasEitherUnicodeFlag();

    /// <summary>Gets the number of capturing groups.</summary>
    public int GroupCount { get; }

    /// <summary>Gets the name of each group (index 0 is the whole match), null for an unnamed group.</summary>
    public string?[] GroupNames { get; }

    /// <summary>Gets whether the pattern contains a GroupName.</summary>
    public bool HasNamedGroups { get; }

    internal int[] Code { get; }

    internal RegExpCharClass[] Classes { get; }

    internal string[] Strings { get; }

    internal RegExpLookaroundInfo[] Lookarounds { get; }

    internal RegExpBackreferenceInfo[] Backreferences { get; }

    internal int RegisterCount { get; }

    internal RegExpSearchInfo Search { get; }

    /// <summary>Compiles a pattern.</summary>
    public static RegExpProgram? Compile(string pattern, RegExpFlags flags, out string? error)
    {
        if (!RegExpParser.TryParse(pattern, flags, out var parsed, out error))
            return null;

        return RegExpCodeGenerator.Generate(parsed!, flags, pattern);
    }

    /// <summary>
    /// Finds the first match at or after <paramref name="lastIndex"/> (only at <paramref name="lastIndex"/> when
    /// <paramref name="sticky"/>), as RegExpBuiltinExec does.
    /// </summary>
    /// <param name="input">The string.</param>
    /// <param name="lastIndex">The index where the search starts, at most the length of the string.</param>
    /// <param name="sticky">Whether the match must start at <paramref name="lastIndex"/>.</param>
    /// <param name="engine">The engine whose budget the match uses, if any.</param>
    /// <returns>
    /// The capture slots (start and end of each group, -1 when undefined), or null when there is no match.
    /// </returns>
    public int[]? Exec(string input, int lastIndex, bool sticky, JsEngine? engine)
    {
        var runner = RegExpRunner.Rent();
        try
        {
            return runner.Exec(this, input, lastIndex, sticky, engine);
        }
        finally
        {
            RegExpRunner.Return(runner);
        }
    }

    /// <summary>Gets whether the pattern matches somewhere in the input.</summary>
    public bool IsMatch(string input) => Exec(input, 0, sticky: false, engine: null) is not null;
}
