// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Tomlyn.Helpers;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Internal;

internal sealed class TomlSerializationOperationState
{
    private readonly Dictionary<Type, TomlTypeInfo> _typeInfoCache = new();

    public TomlSerializationOperationState(TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        Options = options;
        SingleOrArrayCollections = new TomlSingleOrArrayCollectionHelper();
    }

    public TomlSerializerOptions Options { get; }

    public TomlSingleOrArrayCollectionHelper SingleOrArrayCollections { get; }

    public DiagnosticsBag? Diagnostics { get; private set; }

    public bool HasDiagnostics => Diagnostics is { Count: > 0 };

    public bool CanAddDiagnostics(TomlException exception)
    {
        ArgumentGuard.ThrowIfNull(exception, nameof(exception));

        return !ReferenceEquals(exception.Diagnostics, Diagnostics) &&
            (exception.Diagnostics.Count > 0 || exception.Span.HasValue);
    }

    public void AddDiagnostics(TomlException exception)
    {
        ArgumentGuard.ThrowIfNull(exception, nameof(exception));

        if (ReferenceEquals(exception.Diagnostics, Diagnostics))
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
