using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;
namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Provides a fluent API for configuring and validating inline snapshots.</summary>
/// <example>
/// <code>
/// InlineSnapshot
///     .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
///     .Validate(data, "expected snapshot");
/// </code>
/// </example>
public readonly struct InlineSnapshotBuilder
{
    private readonly InlineSnapshotSettings _settings;

    internal InlineSnapshotBuilder(InlineSnapshotSettings? settings)
    {
        _settings = settings ?? InlineSnapshotSettings.Default;
    }

    /// <summary>Configures the settings for the snapshot validation.</summary>
    public InlineSnapshotBuilder WithSettings(Action<InlineSnapshotSettings>? configure)
    {
        if (configure is null)
            return this;

        var settings = _settings with { };
        configure(settings);
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>Sets the snapshot serializer to use for serializing objects.</summary>
    public InlineSnapshotBuilder WithSerializer(SnapshotSerializer serializer)
    {
        var settings = _settings with { };
        settings.SnapshotSerializer = serializer;
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>Configures the human-readable serializer with the specified options.</summary>
    public InlineSnapshotBuilder WithSerializer(HumanReadableSerializerOptions? options)
    {
        if (options is null)
            return this;

        var settings = _settings with { };
        settings.UseHumanReadableSerializer(options);
        return new InlineSnapshotBuilder(settings);
    }

    /// <summary>Configures the human-readable serializer using an action.</summary>
    public InlineSnapshotBuilder WithSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        if (configure is null)
            return this;

        var settings = _settings with { };
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
    public void Validate(object? subject, string? expected = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        var settings = _settings;
        var context = CallerContext.Get(settings, filePath, lineNumber);
        InlineSnapshot.ShouldMatchInlineSnapshot(subject, context, settings, expected);
    }
}
