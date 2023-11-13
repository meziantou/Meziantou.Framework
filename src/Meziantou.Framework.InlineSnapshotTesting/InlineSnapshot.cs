using System.Runtime.CompilerServices;
using DiffEngine;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class InlineSnapshot
{
    public static InlineSnapshotBuilder WithSettings(InlineSnapshotSettings? settings) => new(settings);
    public static InlineSnapshotBuilder WithSettings(Action<InlineSnapshotSettings>? configure)
    {
        if (configure is null)
            return new(settings: null);

        var settings = InlineSnapshotSettings.Default with { };
        configure.Invoke(settings);
        return new InlineSnapshotBuilder(settings);
    }

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
                actual = scrubber.Scrub(actual);
            }
        }

        var normalizedActual = settings.SnapshotComparer.NormalizeValue(actual);
        var normalizedExpected = settings.SnapshotComparer.NormalizeValue(expected);
        if (!settings.SnapshotComparer.AreEqual(normalizedActual, normalizedExpected))
        {
            var isOnCI = settings.AutoDetectContinuousEnvironment && (BuildServerDetector.Detected || ContinuousTestingDetector.Detected);
            if (!isOnCI && settings.SnapshotUpdateStrategy.CanUpdateSnapshot(settings, context.FilePath, expected, actual))
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
