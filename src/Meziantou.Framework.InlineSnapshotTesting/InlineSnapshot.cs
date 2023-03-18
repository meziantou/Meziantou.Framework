using System.Runtime.CompilerServices;
using DiffEngine;
namespace Meziantou.Framework.InlineSnapshotTesting;

public static class InlineSnapshot
{
    [InlineSnapshotAssertion(nameof(expected))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? subject, InlineSnapshotSettings? settings, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        settings ??= InlineSnapshotSettings.Default;
        var context = CallerContext.Get(settings, filePath, lineNumber);
        ShouldMatchInlineSnapshot(subject, context, settings, expected);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? subject, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        var settings = InlineSnapshotSettings.Default;
        var context = CallerContext.Get(settings, filePath, lineNumber);
        ShouldMatchInlineSnapshot(subject, context, settings, expected);
    }

    private static void ShouldMatchInlineSnapshot(object? subject, CallerContext context, InlineSnapshotSettings settings, string? expected)
    {
        var actual = settings.SnapshotSerializer.Serialize(subject);
        var normalizedActual = settings.SnapshotComparer.NormalizeValue(actual);
        var normalizedExpected = settings.SnapshotComparer.NormalizeValue(expected);
        if (!settings.SnapshotComparer.AreEqual(normalizedActual, normalizedExpected))
        {
            var isOnCI = settings.AutoDetectContinuousEnvironment && (BuildServerDetector.Detected || ContinuousTestingDetector.Detected);
            if (!isOnCI && settings.SnapshotUpdateStrategy.CanUpdateSnapshot(settings, context.FilePath))
            {
                if (context.FilePath == null)
                    throw new InlineSnapshotException("Cannot update source file as the path is null");

                FileEditor.UpdateFile(context, settings, expected, actual);

                if (settings.SnapshotUpdateStrategy.MustReportError(settings, context.FilePath))
                {
                    settings.Assert(normalizedExpected, normalizedActual);
                }
            }
            else
            {
                settings.Assert(normalizedExpected, normalizedActual);
            }
        }
        else if (settings.ForceUpdateSnapshots)
        {
            if (context.FilePath == null)
                throw new InlineSnapshotException("Cannot update source file as the path is null");

            FileEditor.UpdateFile(context, settings, expected, actual);
        }
    }
}
