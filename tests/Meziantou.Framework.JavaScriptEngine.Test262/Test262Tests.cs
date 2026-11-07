using Meziantou.Framework.JavaScriptEngine.Test262.Runner;

namespace Meziantou.Framework.JavaScriptEngine.Test262;

/// <summary>Runs test262 and compares the results with the expectations (expectations/&lt;area&gt;.txt).</summary>
public sealed class Test262Tests
{
    private static readonly string[] LargeBuiltIns = ["built-ins/Array", "built-ins/Object", "built-ins/RegExp", "built-ins/Temporal", "built-ins/TypedArray", "built-ins/TypedArrayConstructors"];
    private static readonly string[] LargeLanguageDirectories = ["language/expressions", "language/statements"];

    public static TheoryData<string> AnnexBTests => Test262Discovery.GetTheoryData("annexB");

    public static TheoryData<string> BuiltInsArrayTests => Test262Discovery.GetTheoryData("built-ins/Array");

    public static TheoryData<string> BuiltInsObjectTests => Test262Discovery.GetTheoryData("built-ins/Object");

    public static TheoryData<string> BuiltInsRegExpTests => Test262Discovery.GetTheoryData("built-ins/RegExp");

    public static TheoryData<string> BuiltInsTemporalTests => Test262Discovery.GetTheoryData("built-ins/Temporal");

    public static TheoryData<string> BuiltInsTypedArrayTests => Test262Discovery.GetTheoryData("built-ins/TypedArray");

    public static TheoryData<string> BuiltInsTypedArrayConstructorsTests => Test262Discovery.GetTheoryData("built-ins/TypedArrayConstructors");

    public static TheoryData<string> BuiltInsTests => Test262Discovery.GetTheoryData("built-ins", LargeBuiltIns);

    public static TheoryData<string> LanguageExpressionsTests => Test262Discovery.GetTheoryData("language/expressions");

    public static TheoryData<string> LanguageStatementsTests => Test262Discovery.GetTheoryData("language/statements");

    public static TheoryData<string> LanguageTests => Test262Discovery.GetTheoryData("language", LargeLanguageDirectories);

    public static TheoryData<string> Intl402Tests => Test262Discovery.GetTheoryData("intl402");

    public static TheoryData<string> StagingTests => Test262Discovery.GetTheoryData("staging");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(AnnexBTests), DisableDiscoveryEnumeration = true)]
    public void AnnexB(string test) => Run("annexB", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsArrayTests), DisableDiscoveryEnumeration = true)]
    public void BuiltInsArray(string test) => Run("built-ins/Array", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsObjectTests), DisableDiscoveryEnumeration = true)]
    public void BuiltInsObject(string test) => Run("built-ins/Object", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsRegExpTests), DisableDiscoveryEnumeration = true)]
    public void BuiltInsRegExp(string test) => Run("built-ins/RegExp", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsTemporalTests), DisableDiscoveryEnumeration = true)]
    public void BuiltInsTemporal(string test) => Run("built-ins/Temporal", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsTypedArrayTests), DisableDiscoveryEnumeration = true)]
    public void BuiltInsTypedArray(string test) => Run("built-ins/TypedArray", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsTypedArrayConstructorsTests), DisableDiscoveryEnumeration = true)]
    public void BuiltInsTypedArrayConstructors(string test) => Run("built-ins/TypedArrayConstructors", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BuiltInsTests), DisableDiscoveryEnumeration = true)]
    public void BuiltIns(string test) => Run("built-ins", test, LargeBuiltIns);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(LanguageExpressionsTests), DisableDiscoveryEnumeration = true)]
    public void LanguageExpressions(string test) => Run("language/expressions", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(LanguageStatementsTests), DisableDiscoveryEnumeration = true)]
    public void LanguageStatements(string test) => Run("language/statements", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(LanguageTests), DisableDiscoveryEnumeration = true)]
    public void Language(string test) => Run("language", test, LargeLanguageDirectories);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Intl402Tests), DisableDiscoveryEnumeration = true)]
    public void Intl402(string test) => Run("intl402", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(StagingTests), DisableDiscoveryEnumeration = true)]
    public void Staging(string test) => Run("staging", test);

    private static void Run(string directory, string id, params string[] excludedDirectories)
    {
        var test = Test262Discovery.Discover(directory, excludedDirectories).ById[id];
        var results = Test262Executor.Run(test);
        Test262Report.Write(test, results);
        Test262Expectations.Check(test, results);
    }
}
