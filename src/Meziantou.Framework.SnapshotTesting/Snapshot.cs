using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting;

public static class Snapshot
{
    public static AsyncLocal<SnapshotTestContext?> TestContext { get; } = new();

    /// <summary>
    /// Registers a deterministic source-root mapping so generated paths (for example <c>/_/</c>)
    /// can be resolved back to real source files on disk.
    /// </summary>
    /// <param name="mappedPath">Deterministic mapped prefix emitted by the compiler.</param>
    /// <param name="realPath">Real source-root path on disk.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterSourceRootMapping(string mappedPath, string realPath)
    {
        CallerContextUtilities.RegisterSourceRootMapping(mappedPath, realPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [OverloadResolutionPriority(-1)] // Snapshot type has an implicit conversion from string, so this method must be called only if the user explicitly specify the type of snapshot
    [SnapshotAssertion]
    public static void Validate(object? value, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, settings: null, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static void Validate(object? value, SnapshotSettings? settings, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, type: null, settings, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static void Validate(object? value, SnapshotType? type, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, type, settings: null, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static void Validate(object? value, SnapshotType? type, SnapshotSettings? settings, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = -1, [CallerMemberName] string? callerMemberName = null)
    {
        SnapshotEngine.Validate(type, value, settings ?? SnapshotSettings.Default, callerFilePath, callerLineNumber, callerMemberName, TestContext.Value);
    }

    /// <summary>Validates the content of a stream against its snapshot, reading the stream asynchronously.</summary>
    /// <inheritdoc cref="ValidateAsync(Stream, SnapshotType?, SnapshotSettings?, CancellationToken, string?, int, string?)" path="/remarks" />
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync(Stream stream, CancellationToken cancellationToken = default, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        return ValidateAsync(stream, type: null, settings: null, cancellationToken, filePath, lineNumber, memberName);
    }

    /// <summary>Validates the content of a stream against its snapshot, reading the stream asynchronously.</summary>
    /// <inheritdoc cref="ValidateAsync(Stream, SnapshotType?, SnapshotSettings?, CancellationToken, string?, int, string?)" path="/remarks" />
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync(Stream stream, SnapshotSettings? settings, CancellationToken cancellationToken = default, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        return ValidateAsync(stream, type: null, settings, cancellationToken, filePath, lineNumber, memberName);
    }

    /// <summary>Validates the content of a stream against its snapshot, reading the stream asynchronously.</summary>
    /// <inheritdoc cref="ValidateAsync(Stream, SnapshotType?, SnapshotSettings?, CancellationToken, string?, int, string?)" path="/remarks" />
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync(Stream stream, SnapshotType? type, CancellationToken cancellationToken = default, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        return ValidateAsync(stream, type, settings: null, cancellationToken, filePath, lineNumber, memberName);
    }

    /// <summary>Validates the content of a stream against its snapshot, reading the stream asynchronously.</summary>
    /// <remarks>
    /// The stream is read with <see cref="Stream.CopyToAsync(Stream, CancellationToken)" />, from its current position to
    /// its end, so a stream that only supports asynchronous reads can be validated. It is neither rewound nor disposed. Once
    /// read, the content is validated like a stream passed to <see cref="Validate(object?, SnapshotType?, SnapshotSettings?, string?, int, string?)" />.
    /// The returned task completes after the comparison and the snapshot updates. No snapshot file is read or written before
    /// the stream is fully read, so canceling the read leaves the snapshots untouched.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync(Stream stream, SnapshotType? type, SnapshotSettings? settings, CancellationToken cancellationToken = default, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = -1, [CallerMemberName] string? callerMemberName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return SnapshotEngine.ValidateAsync(type, stream, settings ?? SnapshotSettings.Default, callerFilePath, callerLineNumber, callerMemberName, TestContext.Value, cancellationToken);
    }

    /// <summary>Validates the items of an asynchronous sequence against their snapshot, enumerating the sequence asynchronously.</summary>
    /// <inheritdoc cref="ValidateAsync{T}(IAsyncEnumerable{T}, SnapshotType?, SnapshotSettings?, CancellationToken, string?, int, string?)" path="/remarks" />
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync<T>(IAsyncEnumerable<T> values, CancellationToken cancellationToken = default, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        return ValidateAsync(values, type: null, settings: null, cancellationToken, filePath, lineNumber, memberName);
    }

    /// <summary>Validates the items of an asynchronous sequence against their snapshot, enumerating the sequence asynchronously.</summary>
    /// <inheritdoc cref="ValidateAsync{T}(IAsyncEnumerable{T}, SnapshotType?, SnapshotSettings?, CancellationToken, string?, int, string?)" path="/remarks" />
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync<T>(IAsyncEnumerable<T> values, SnapshotSettings? settings, CancellationToken cancellationToken = default, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        return ValidateAsync(values, type: null, settings, cancellationToken, filePath, lineNumber, memberName);
    }

    /// <summary>Validates the items of an asynchronous sequence against their snapshot, enumerating the sequence asynchronously.</summary>
    /// <inheritdoc cref="ValidateAsync{T}(IAsyncEnumerable{T}, SnapshotType?, SnapshotSettings?, CancellationToken, string?, int, string?)" path="/remarks" />
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync<T>(IAsyncEnumerable<T> values, SnapshotType? type, CancellationToken cancellationToken = default, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        return ValidateAsync(values, type, settings: null, cancellationToken, filePath, lineNumber, memberName);
    }

    /// <summary>Validates the items of an asynchronous sequence against their snapshot, enumerating the sequence asynchronously.</summary>
    /// <remarks>
    /// The sequence is enumerated with <c>await foreach</c>, passing <paramref name="cancellationToken" />, instead of blocking the
    /// test while the items are produced. The items are then validated as a <see cref="List{T}" />: the human-readable serializer
    /// writes it as it writes the sequence, but a serializer registered for <see cref="IAsyncEnumerable{T}" /> does not apply.
    /// The returned task completes after the comparison and the snapshot updates. No snapshot file is read or written before the
    /// sequence is fully enumerated, so canceling the enumeration leaves the snapshots untouched.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static Task ValidateAsync<T>(IAsyncEnumerable<T> values, SnapshotType? type, SnapshotSettings? settings, CancellationToken cancellationToken = default, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = -1, [CallerMemberName] string? callerMemberName = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return SnapshotEngine.ValidateAsync(type, values, settings ?? SnapshotSettings.Default, callerFilePath, callerLineNumber, callerMemberName, TestContext.Value, cancellationToken);
    }
}
