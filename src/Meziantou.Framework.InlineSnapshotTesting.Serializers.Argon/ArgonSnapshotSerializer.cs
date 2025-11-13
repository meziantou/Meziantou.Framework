using Argon;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

/// <summary>Serializes objects to JSON format using the Argon JSON serializer, compatible with Verify's snapshot format.</summary>
/// <example>
/// <code>
/// // Use with InlineSnapshot
/// InlineSnapshot
///     .WithSerializer(new ArgonSnapshotSerializer())
///     .Validate(data, """
///         {
///             Foo: Bar
///         }
///         """);
/// 
/// // Set as default serializer using a module initializer
/// [ModuleInitializer]
/// public static void Initialize()
/// {
///     InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
///     {
///         SnapshotSerializer = new ArgonSnapshotSerializer(),
///     };
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// This serializer uses the Argon JSON library to produce JSON-style output that is compatible with
/// the Verify snapshot testing library's format. It is particularly useful when migrating from
/// Verify to InlineSnapshotTesting or when you need cross-compatibility between the two libraries.
/// </para>
/// <para>
/// The serializer is configured to produce unquoted property names and values for better readability,
/// with indented formatting for nested structures. This format is similar to Verify's default output.
/// </para>
/// <para>
/// For new projects, consider using the default <see cref="HumanReadableSnapshotSerializer"/> which
/// provides better readability and more configuration options. Use <see cref="ArgonSnapshotSerializer"/>
/// when you specifically need Verify-compatible output or JSON-style formatting.
/// </para>
/// </remarks>
/// <seealso cref="SnapshotSerializer"/>
/// <seealso cref="HumanReadableSnapshotSerializer"/>
/// <seealso href="https://github.com/SimonCropp/Argon">Argon JSON Library</seealso>
/// <seealso href="https://github.com/VerifyTests/Verify">Verify Snapshot Testing</seealso>
public sealed class ArgonSnapshotSerializer : SnapshotSerializer
{
    private static readonly JsonSerializer Serializer = new();

    /// <inheritdoc />
    public override string Serialize(object? value)
    {
        var result = new StringBuilder();
        var textWriter = new StringWriter(result)
        {
            NewLine = "\n",
        };

        using var writer = new JsonTextWriter(textWriter)
        {
            QuoteName = false,
            QuoteValue = false,
            EscapeHandling = EscapeHandling.None,
            Formatting = Formatting.Indented,
        };

        Serializer.Serialize(writer, value);

        return result.ToString();
    }
}
