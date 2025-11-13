using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Provides methods for validating objects against inline snapshots in test code.</summary>
/// <example>
/// <code>
/// var data = new { FirstName = "John", LastName = "Doe" };
/// InlineSnapshot.Validate(data, """
///     FirstName: John,
///     LastName: Doe
///     """);
/// </code>
/// </example>
public static class InlineSnapshot
{
    /// <summary>Creates a new <see cref="InlineSnapshotBuilder"/> with the specified settings.</summary>
    public static InlineSnapshotBuilder WithSettings(InlineSnapshotSettings? settings) => new(settings);

    /// <summary>Creates a new <see cref="InlineSnapshotBuilder"/> with settings configured by the specified action.</summary>
    public static InlineSnapshotBuilder WithSettings(Action<InlineSnapshotSettings>? configure)
    {
        if (configure is null)
            return new(settings: null);

        var settings = InlineSnapshotSettings.Default with { };
        configure.Invoke(settings);
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>Creates a new <see cref="InlineSnapshotBuilder"/> with the specified snapshot serializer.</summary>
    public static InlineSnapshotBuilder WithSerializer(SnapshotSerializer serializer)
    {
        var settings = InlineSnapshotSettings.Default with { };
        settings.SnapshotSerializer = serializer;
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>Creates a new <see cref="InlineSnapshotBuilder"/> with a human-readable serializer configured with the specified options.</summary>
    public static InlineSnapshotBuilder WithSerializer(HumanReadableSerializerOptions? options)
    {
        if (options is null)
            return new(settings: null);

        var settings = InlineSnapshotSettings.Default with { };
        settings.UseHumanReadableSerializer(options);
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>Creates a new <see cref="InlineSnapshotBuilder"/> with a human-readable serializer configured by the specified action.</summary>
    public static InlineSnapshotBuilder WithSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        if (configure is null)
            return new(settings: null);

        var settings = InlineSnapshotSettings.Default with { };
        settings.UseHumanReadableSerializer(configure);
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>
    /// Validates that the serialized representation of the subject matches the expected snapshot.
    /// If the snapshot doesn't match, the source file is updated with the actual value based on the configured update strategy.
    /// </summary>
    /// <param name="subject">The object to validate.</param>
    /// <param name="expected">The expected snapshot value. This parameter will be automatically updated when the snapshot changes.</param>
    /// <param name="filePath">The source file path. Automatically populated by the compiler.</param>
    /// <param name="lineNumber">The line number in the source file. Automatically populated by the compiler.</param>
    [InlineSnapshotAssertion(nameof(expected))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? subject, string? expected = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        var settings = InlineSnapshotSettings.Default;
        var context = CallerContext.Get(settings, filePath, lineNumber);
        ShouldMatchInlineSnapshot(subject, context, settings, expected);
    }

    internal static void ShouldMatchInlineSnapshot(object? subject, CallerContext context, InlineSnapshotSettings settings, string? expected)
    {
        var actual = settings.SnapshotSerializer.Serialize(subject);
        if (actual is not null)
        {
            foreach (var scrubber in settings.Scrubbers)
            {
                if (scrubber is not null)
                {
                    actual = scrubber.Scrub(actual);
                }
            }
        }

        var normalizedActual = settings.SnapshotComparer.NormalizeValue(actual);
        var normalizedExpected = settings.SnapshotComparer.NormalizeValue(expected);
        if (!settings.SnapshotComparer.AreEqual(normalizedActual, normalizedExpected))
        {
            if (settings.SnapshotUpdateStrategy.CanUpdateSnapshotInternal(settings, context.FilePath, expected, actual))
            {
                if (context.FilePath is null)
                    throw new InlineSnapshotException("Cannot update source file as the path is null");

                FileEditor.UpdateFile(context, settings, expected, actual);

                if (settings.SnapshotUpdateStrategy.MustReportError(settings, context.FilePath))
                {
                    settings.AssertSnapshot(normalizedExpected, normalizedActual);
                }
            }
            else
            {
                settings.AssertSnapshot(normalizedExpected, normalizedActual);
            }
        }
        else if (settings.ForceUpdateSnapshots)
        {
            if (context.FilePath is null)
                throw new InlineSnapshotException("Cannot update source file as the path is null");

            FileEditor.UpdateFile(context, settings, expected, actual);
        }
    }
}
