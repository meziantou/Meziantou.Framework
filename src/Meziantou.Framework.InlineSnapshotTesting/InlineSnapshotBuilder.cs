using System.Runtime.CompilerServices;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;
namespace Meziantou.Framework.InlineSnapshotTesting;

public readonly struct InlineSnapshotBuilder
{
    private readonly InlineSnapshotSettings _settings;

    internal InlineSnapshotBuilder(InlineSnapshotSettings? settings)
    {
        _settings = settings ?? InlineSnapshotSettings.Default;
    }

    public InlineSnapshotBuilder WithSettings(Action<InlineSnapshotSettings>? configure)
    {
        if (configure is null)
            return this;

        var settings = _settings with { };
        configure(settings);
        return new InlineSnapshotBuilder(settings);
    }

    public InlineSnapshotBuilder WithSerializer(SnapshotSerializer serializer)
    {
        var settings = _settings with { };
        settings.SnapshotSerializer = serializer;
        return new InlineSnapshotBuilder(settings);
    }

    public InlineSnapshotBuilder WithSerializer(HumanReadableSerializerOptions? options)
    {
        if (options is null)
            return this;

        var settings = _settings with { };
        settings.UseHumanReadableSerializer(options);
        return new InlineSnapshotBuilder(settings);
    }

    public InlineSnapshotBuilder WithSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        if (configure is null)
            return this;

        var settings = _settings with { };
        settings.UseHumanReadableSerializer(configure);
        return new InlineSnapshotBuilder(settings);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void Validate(object? subject, string? expected = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        var settings = _settings;
        var context = CallerContext.Get(settings, filePath, lineNumber);
        InlineSnapshot.ShouldMatchInlineSnapshot(subject, context, settings, expected);
    }
}
