namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The values of <see cref="FixtureEntry.Kind"/>.</summary>
public static class FixtureKinds
{
    /// <summary>A valid input that decodes to the expected pixels and metadata.</summary>
    public const string Valid = "valid";

    /// <summary>A malformed, inconsistent or truncated input that must fail (usually <c>InvalidImageContentException</c>).</summary>
    public const string Invalid = "invalid";

    /// <summary>A valid input using a recognized but unsupported feature (<c>UnsupportedImageFeatureException</c>).</summary>
    public const string Unsupported = "unsupported";

    /// <summary>An input that must exceed the resource limits of <see cref="FixtureEntry.DecodeOptions"/> (<c>ImageResourceLimitException</c>).</summary>
    public const string Limit = "limit";

    /// <summary>Gets all the kinds.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { Valid, Invalid, Unsupported, Limit };
}
