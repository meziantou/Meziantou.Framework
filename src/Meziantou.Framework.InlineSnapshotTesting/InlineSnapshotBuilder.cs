using System.Runtime.CompilerServices;
namespace Meziantou.Framework.InlineSnapshotTesting;

public readonly struct InlineSnapshotBuilder
{
    private readonly InlineSnapshotSettings? _settings;

    internal InlineSnapshotBuilder(InlineSnapshotSettings? settings)
    {
        _settings = settings;
    }

    [InlineSnapshotAssertion(nameof(expected))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void Validate(object? subject, string? expected = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        var settings = _settings ?? InlineSnapshotSettings.Default;    
        var context = CallerContext.Get(settings, filePath, lineNumber);
        InlineSnapshot.ShouldMatchInlineSnapshot(subject, context, settings, expected);
    }
}
