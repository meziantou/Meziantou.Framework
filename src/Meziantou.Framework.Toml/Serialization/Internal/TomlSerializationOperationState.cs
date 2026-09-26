using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlSerializationOperationState
{
    // The metadata is cached per options instance (the options are immutable) and shared by all the operations. The
    // table compares the options by reference, so a copy created with a 'with' expression has its own cache.
    private static readonly ConditionalWeakTable<TomlSerializerOptions, ConcurrentDictionary<Type, TomlTypeInfo>> TypeInfoCaches = new();

    private readonly ConcurrentDictionary<Type, TomlTypeInfo> _typeInfoCache;

    public TomlSerializationOperationState(TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        Options = options;
        _typeInfoCache = TypeInfoCaches.GetValue(options, static _ => new ConcurrentDictionary<Type, TomlTypeInfo>());
        SingleOrArrayCollections = new TomlSingleOrArrayCollectionHelper();
    }

    public TomlSerializerOptions Options { get; }

    public TomlSingleOrArrayCollectionHelper SingleOrArrayCollections { get; }

    public DiagnosticsBag? Diagnostics { get; private set; }

    public bool HasDiagnostics => Diagnostics is { Count: > 0 };

    // Only TomlSerializer reports the recorded diagnostics once the document is read. A reader created by TomlReader.Create
    // has no one to report them, so its values throw instead of being skipped.
    public bool RecoversValueErrors { get; set; }

    public int DiagnosticCount => Diagnostics?.Count ?? 0;

    // A value error can be as short as a few characters, and a table can report every missing required key: past this
    // count, the next error stops the reading, so the work and memory stay bounded
    internal const int MaxRecordedDiagnostics = 1000;

    public bool IsRecordedValueError(TomlException exception) => exception.IsRecordedValueError && ReferenceEquals(exception.OperationDiagnostics, Diagnostics);

    // The error of the value that started at valueStart, which was read completely. Only a table records its errors, and an
    // array of tables starts at the same place as its first table, so the token type tells them apart.
    public bool IsRecordedValueError(TomlException exception, TomlTokenType valueTokenType, TomlSourceSpan? valueStart)
        => IsRecordedValueError(exception) && valueTokenType == TomlTokenType.StartTable && Nullable.Equals(exception.RecordedValueStart, valueStart);

    // A table with errors cannot be used, but it was read completely, so its parent continues with its next value
    public void ThrowIfDiagnosticsSince(int diagnosticCount, TomlSourceSpan? tableStart)
    {
        if (DiagnosticCount > diagnosticCount)
        {
            throw TomlException.CreateRecordedValueError(Diagnostics!, diagnosticCount, tableStart);
        }
    }

    // For an error found after the value was read, such as a missing required key: it is recorded with the others, and the
    // table checks for errors once all of them are found
    public void RecordOrThrow(TomlException exception)
    {
        ArgumentGuard.ThrowIfNull(exception, nameof(exception));

        if (!CanAddDiagnostics(exception))
        {
            throw exception;
        }

        AddDiagnostics(exception);
    }

    // The error of a value that was read completely, such as a buffered polymorphic value: it is recorded, unless it already
    // was, so the table that contains the value continues with its next value
    public bool TryRecordErrorOfReadValue(TomlException exception)
    {
        if (IsRecordedValueError(exception))
        {
            return true;
        }

        if (exception.IsConfigurationError || !CanAddDiagnostics(exception))
        {
            return false;
        }

        AddDiagnostics(exception);
        return true;
    }

    public bool CanAddDiagnostics(TomlException exception)
    {
        ArgumentGuard.ThrowIfNull(exception, nameof(exception));

        return RecoversValueErrors &&
            DiagnosticCount < MaxRecordedDiagnostics &&
            !IsRecordedValueError(exception) &&
            (exception.Diagnostics.Count > 0 || exception.Span.HasValue);
    }

    public void AddDiagnostics(TomlException exception)
    {
        ArgumentGuard.ThrowIfNull(exception, nameof(exception));

        if (IsRecordedValueError(exception) || ReferenceEquals(exception.Diagnostics, Diagnostics))
        {
            return;
        }

        var diagnostics = Diagnostics ??= new DiagnosticsBag();
        if (exception.Diagnostics.Count > 0)
        {
            diagnostics.AddRange(exception.Diagnostics);
            return;
        }

        if (exception.Span is { } span)
        {
            diagnostics.Error(ToLegacySpan(span), exception.Message);
        }
    }

    [RequiresUnreferencedCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    public TomlTypeInfo ResolveTypeInfo(Type type)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));

        return TomlTypeInfoResolverPipeline.Resolve(this, type);
    }

    public bool TryGetCachedTypeInfo(Type type, [NotNullWhen(true)] out TomlTypeInfo? typeInfo)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        return _typeInfoCache.TryGetValue(type, out typeInfo);
    }

    public void CacheTypeInfo(Type type, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        _typeInfoCache[type] = typeInfo;
    }

    private static SourceSpan ToLegacySpan(TomlSourceSpan span)
        => new(span.SourceName, new TextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TextPosition(span.End.Offset, span.End.Line, span.End.Column));
}
