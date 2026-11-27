using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Assertions.Fuzz;

/// <summary>
/// The fast paths of the equality assertions only ever conclude that two values are equal: a byte comparison of arrays of
/// simple values, a loop over arrays and lists of strings, a single in-order pass of <c>EqualUnordered</c>, a linear
/// search of the object pairs <c>Equivalent</c> visited. Any other result falls back to the general comparison, which
/// builds the failure message. The targets check that the assertions agree with that general comparison, and with a
/// model of their result.
/// </summary>
public static partial class AssertionsFuzzTargets
{
    // A null item of a collection of strings (assertions-sequences)
    private const string NullStringMarker = "\u0001";

    // assertions-sequences: zero, all bits set, -0.0 and NaN (double, float, Half), the text separator and the null
    // marker, letters of both cases. Declared before All, which reads it: the static fields of the other files of the class
    // may be initialized after All.
    private static readonly string[] SequenceTokens =
    [
        Token(0, 0, 0, 0), Token(0, 0, 0, 0, 0, 0, 0, 0), Token(0xFF, 0xFF, 0xFF, 0xFF), Token(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF),
        Token(0, 0, 0, 0, 0, 0, 0, 0x80), Token(0, 0, 0, 0, 0, 0, 0xF8, 0x7F), Token(0, 0, 0, 0x80), Token(0, 0, 0xC0, 0x7F), Token(0, 0x80), Token(0, 0x7E),
        "\0", NullStringMarker, "a", "A", "\u00E9", "\u00C9",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("assertions-sequences", Sequences) { Seeds = GetSequenceSeeds, Dictionary = SequenceTokens },
        new("assertions-object-graph", ObjectGraph) { Seeds = GetObjectGraphSeeds },
    ];

    private static string Token(params ReadOnlySpan<byte> bytes) => FuzzText.Decode(bytes);

    /// <summary>Runs an assertion: <see langword="true"/> when it succeeds, <see langword="false"/> when it fails.</summary>
    private static bool Passes(Action assertion)
    {
        try
        {
            assertion();
            return true;
        }
        catch (AssertionException)
        {
            return false;
        }
    }
}
