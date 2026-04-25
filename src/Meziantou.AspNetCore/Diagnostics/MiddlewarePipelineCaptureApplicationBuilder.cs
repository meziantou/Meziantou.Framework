using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewarePipelineCaptureApplicationBuilder : IApplicationBuilder
{
    private const string NextMiddlewareNamePropertyName = "analysis.NextMiddlewareName";

    private readonly IApplicationBuilder _innerBuilder;
    private readonly MiddlewarePipelineDescriptor _pipeline;
    private readonly Queue<MiddlewarePipelineDescriptor> _pendingBranches = new();

    public MiddlewarePipelineCaptureApplicationBuilder(IApplicationBuilder innerBuilder, MiddlewarePipelineDescriptor pipeline)
    {
        _innerBuilder = innerBuilder;
        _pipeline = pipeline;
    }

    public IServiceProvider ApplicationServices
    {
        get => _innerBuilder.ApplicationServices;
        set => _innerBuilder.ApplicationServices = value;
    }

    public IDictionary<string, object?> Properties => _innerBuilder.Properties;

    public IFeatureCollection ServerFeatures => _innerBuilder.ServerFeatures;

    public RequestDelegate Build() => _innerBuilder.Build();

    public IApplicationBuilder New()
    {
        var branch = new MiddlewarePipelineDescriptor();
        _pendingBranches.Enqueue(branch);

        return new MiddlewarePipelineCaptureApplicationBuilder(_innerBuilder.New(), branch);
    }

    public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);

        var descriptor = new MiddlewareDescriptor
        {
            Name = GetMiddlewareName(Properties, middleware),
            DelegateType = middleware.GetType().FullName ?? middleware.GetType().Name,
            DelegateMethod = middleware.Method.Name,
        };

        while (_pendingBranches.TryDequeue(out var branch))
        {
            descriptor.Branches.Add(branch);
        }

        _pipeline.Middlewares.Add(descriptor);
        _innerBuilder.Use(middleware);

        return this;
    }

    private static string GetMiddlewareName(IDictionary<string, object?> properties, Func<RequestDelegate, RequestDelegate> middleware)
    {
        if (properties.TryGetValue(NextMiddlewareNamePropertyName, out var middlewareName))
        {
            properties.Remove(NextMiddlewareNamePropertyName);

            var middlewareNameString = middlewareName?.ToString();
            if (!string.IsNullOrWhiteSpace(middlewareNameString))
            {
                return middlewareNameString;
            }
        }

        if (TryGetMiddlewareTypeName(middleware.Target, out var middlewareTypeName))
        {
            return middlewareTypeName;
        }

        var declaringType = middleware.Method.DeclaringType?.FullName;
        if (declaringType is null)
            return middleware.Method.Name;

        return $"{declaringType}.{middleware.Method.Name}";
    }

    private static bool TryGetMiddlewareTypeName(object? middlewareTarget, [NotNullWhen(true)] out string? middlewareTypeName)
    {
        if (middlewareTarget is null)
        {
            middlewareTypeName = null;
            return false;
        }

        if (TryGetMiddlewareType(middlewareTarget, new HashSet<object>(ReferenceEqualityComparer.Instance), maxDepth: 6, out var middlewareType))
        {
            middlewareTypeName = middlewareType.FullName ?? middlewareType.Name;
            return true;
        }

        middlewareTypeName = null;
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Middleware diagnostics uses reflective best-effort discovery over ASP.NET internals.")]
    private static bool TryGetMiddlewareType(object value, HashSet<object> visited, int maxDepth, [NotNullWhen(true)] out Type? middlewareType)
    {
        if (maxDepth < 0)
        {
            middlewareType = null;
            return false;
        }

        if (!visited.Add(value))
        {
            middlewareType = null;
            return false;
        }

        if (value is Delegate { Target: not null } delegateValue && TryGetMiddlewareType(delegateValue.Target, visited, maxDepth - 1, out middlewareType))
            return true;

        var valueType = value.GetType();
        if (value is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                if (item is not string && item is not null && !item.GetType().IsValueType && TryGetMiddlewareType(item, visited, maxDepth - 1, out middlewareType))
                    return true;
            }
        }

        if (!ShouldInspectObject(valueType))
        {
            middlewareType = null;
            return false;
        }

        var middlewareField = valueType.GetField("_middleware", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (middlewareField?.FieldType == typeof(Type) && middlewareField.GetValue(value) is Type explicitMiddlewareType && IsMiddlewareType(explicitMiddlewareType))
        {
            middlewareType = explicitMiddlewareType;
            return true;
        }

        foreach (var field in valueType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.GetValue(value) is not { } fieldValue)
                continue;

            if (fieldValue is Type fieldTypeValue && IsMiddlewareType(fieldTypeValue))
            {
                middlewareType = fieldTypeValue;
                return true;
            }

            if (fieldValue is not string && !fieldValue.GetType().IsValueType && TryGetMiddlewareType(fieldValue, visited, maxDepth - 1, out middlewareType))
                return true;
        }

        middlewareType = null;
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Middleware diagnostics inspects middleware invoke methods by reflection.")]
    private static bool IsMiddlewareType(Type type)
    {
        if (type == typeof(RequestDelegate))
            return false;

        return type.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null ||
               type.GetMethod("InvokeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
    }

    private static bool ShouldInspectObject(Type type)
    {
        var @namespace = type.Namespace;
        if (@namespace is null)
            return false;

        return @namespace.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
               @namespace.StartsWith("Meziantou", StringComparison.Ordinal);
    }
}
