namespace Meziantou.Framework.Fuzzing;

/// <summary>
/// A function that libFuzzer calls with each input it generates. The action catches the exceptions that the code under test
/// documents for invalid input and checks the properties of the result (<see cref="FuzzAssert"/>); any other exception is
/// a failure.
/// </summary>
public sealed class FuzzTarget
{
    public FuzzTarget(string name, FuzzTargetAction action)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(action);

        Name = name;
        Action = action;
    }

    /// <summary>Gets the name of the target, unique across all the fuzz projects (it names the corpus and the issues).</summary>
    public string Name { get; }

    public FuzzTargetAction Action { get; }

    /// <summary>Gets the inputs the corpus starts from. They must not fail.</summary>
    public Func<IEnumerable<FuzzSeed>> Seeds { get; init; } = static () => [];

    /// <summary>Gets the tokens libFuzzer inserts into the inputs (keywords, punctuation), written as UTF-8 (<see cref="FuzzText.Encode"/>).</summary>
    public IReadOnlyList<string> Dictionary { get; init; } = [];

    public override string ToString() => Name;
}
