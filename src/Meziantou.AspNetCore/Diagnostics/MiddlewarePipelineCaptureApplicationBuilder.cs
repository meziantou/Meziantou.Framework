using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using System.Collections;
using System.Reflection;

namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewarePipelineCaptureApplicationBuilder : IApplicationBuilder
{
    private const string NextMiddlewareNamePropertyName = "analysis.NextMiddlewareName";

    private readonly IApplicationBuilder _innerBuilder;
    private readonly MiddlewarePipelineDescriptor _pipeline;
    private readonly Queue<MiddlewarePipelineDescriptor> _pendingBranches = new();
    private readonly List<MiddlewarePipelineCaptureApplicationBuilder> _branchBuilders = [];
    private bool _recording = true;

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

    /// <summary>
    /// Stops recording, so registrations made while the pipeline is being built are not reported as middleware the
    /// application registered.
    /// </summary>
    /// <remarks>
    /// Several framework helpers call <see cref="Use"/> or <see cref="New"/> from inside the
    /// <see cref="Func{RequestDelegate, RequestDelegate}"/> they registered, which runs during <see cref="Build"/>
    /// rather than during configuration: <c>UseWhen</c> rejoins its branch with <c>branchBuilder.Run(main)</c>, and
    /// <c>UseExceptionHandler</c>, <c>UsePathBase</c> and <c>UseStatusCodePagesWithReExecute</c> reach
    /// <c>RerouteHelper.Reroute</c>, which calls <c>New()</c>. Recording those would add middleware the application
    /// never registered and would append again on every additional <see cref="Build"/> call.
    /// </remarks>
    public void CloseRecording()
    {
        if (!_recording)
            return;

        _recording = false;

        // A branch created without a following Use() has no middleware to attach to. Report it rather than dropping it.
        if (_pendingBranches.Count > 0)
        {
            var descriptor = new MiddlewareDescriptor { Name = "(unattached branch)" };
            while (_pendingBranches.TryDequeue(out var branch))
            {
                descriptor.Branches.Add(branch);
            }

            _pipeline.Middlewares.Add(descriptor);
        }

        foreach (var branchBuilder in _branchBuilders)
        {
            branchBuilder.CloseRecording();
        }
    }

    public IApplicationBuilder New()
    {
        if (!_recording)
            return _innerBuilder.New();

        var branch = new MiddlewarePipelineDescriptor();
        _pendingBranches.Enqueue(branch);

        var branchBuilder = new MiddlewarePipelineCaptureApplicationBuilder(_innerBuilder.New(), branch);
        _branchBuilders.Add(branchBuilder);

        return branchBuilder;
    }

    public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);

        if (!_recording)
        {
            _innerBuilder.Use(middleware);
            return this;
        }

        var properties = Properties;
        var hasExplicitName = properties.TryGetValue(NextMiddlewareNamePropertyName, out var explicitName);

        var descriptor = new MiddlewareDescriptor
        {
            Name = GetMiddlewareName(hasExplicitName ? explicitName?.ToString() : null, middleware),
            DelegateType = middleware.Method.DeclaringType?.FullName ?? middleware.GetType().Name,
            DelegateMethod = middleware.Method.Name,
        };

        while (_pendingBranches.TryDequeue(out var branch))
        {
            descriptor.Branches.Add(branch);
        }

        _pipeline.Middlewares.Add(descriptor);

        // Delegate before clearing the property so another builder decorating this one still observes it, then clear
        // it so the name is not reused for the next middleware. The framework always sets it immediately before its
        // own Use call, so nothing expects it to outlive this registration.
        _innerBuilder.Use(middleware);

        if (hasExplicitName)
        {
            properties.Remove(NextMiddlewareNamePropertyName);
        }

        return this;
    }

    private static string GetMiddlewareName(string? explicitName, Func<RequestDelegate, RequestDelegate> middleware)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName;

        if (TryGetMiddlewareTypeName(middleware.Target, out var middlewareTypeName))
            return middlewareTypeName;

        var declaringType = middleware.Method.DeclaringType?.FullName;
        if (declaringType is null)
            return middleware.Method.Name;

        return $"{declaringType}.{middleware.Method.Name}";
    }

    private static bool TryGetMiddlewareTypeName(object? middlewareTarget, [NotNullWhen(true)] out string? middlewareTypeName)
    {
        if (middlewareTarget is not null)
        {
            // Best-effort discovery over framework internals: never let it prevent the application from starting.
            try
            {
                if (new MiddlewareTypeProbe().TryResolve(middlewareTarget, depth: 0, out var middlewareType))
                {
                    middlewareTypeName = middlewareType.FullName ?? middlewareType.Name;
                    return true;
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
            }
        }

        middlewareTypeName = null;
        return false;
    }

    /// <summary>
    /// Walks a middleware registration delegate's captured state looking for the middleware type, with hard bounds so
    /// that a hostile or merely unusual object graph cannot make application startup fail, hang or allocate without end.
    /// </summary>
    private sealed class MiddlewareTypeProbe
    {
        private const int MaxDepth = 6;
        private const int MaxNodes = 512;
        private const int MaxItemsPerCollection = 32;

        private readonly HashSet<object> _path = new(ReferenceEqualityComparer.Instance);
        private int _remainingNodes = MaxNodes;

        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Middleware diagnostics uses reflective best-effort discovery over ASP.NET internals; an unresolved name degrades to the declaring type name.")]
        public bool TryResolve(object value, int depth, [NotNullWhen(true)] out Type? middlewareType)
        {
            middlewareType = null;

            if (depth > MaxDepth || _remainingNodes <= 0)
                return false;

            // Tracked per path rather than globally: a node reached first by a long path must not be excluded from a
            // later, shallower path that still has depth budget left to find the type below it.
            if (!_path.Add(value))
                return false;

            _remainingNodes--;

            try
            {
                if (value is Delegate { Target: not null } delegateValue && TryResolve(delegateValue.Target, depth + 1, out middlewareType))
                    return true;

                var valueType = value.GetType();
                if (IsTraversalBoundary(value))
                    return false;

                // Only indexable collections, and only a bounded prefix: enumerating an arbitrary IEnumerable can run
                // application code, block, throw, or never end. The framework hops that matter (ApplicationBuilder's
                // component list, endpoint data source lists, delegate factory arrays) are all ILists.
                if (value is IList list)
                {
                    var count = Math.Min(list.Count, MaxItemsPerCollection);
                    for (var i = 0; i < count; i++)
                    {
                        if (list[i] is { } item && item is not string && !item.GetType().IsValueType && TryResolve(item, depth + 1, out middlewareType))
                            return true;
                    }
                }

                if (!ShouldInspectObject(valueType))
                    return false;

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

                    if (fieldValue is Type fieldTypeValue)
                    {
                        if (IsMiddlewareType(fieldTypeValue))
                        {
                            middlewareType = fieldTypeValue;
                            return true;
                        }

                        continue;
                    }

                    if (fieldValue is not string && !fieldValue.GetType().IsValueType && TryResolve(fieldValue, depth + 1, out middlewareType))
                        return true;
                }

                return false;
            }
            finally
            {
                _path.Remove(value);
            }
        }
    }

    /// <summary>
    /// Objects that lead away from the middleware being registered and into the rest of the application.
    /// </summary>
    /// <remarks>
    /// Following them produces names that belong to something else. A registration delegate capturing an
    /// <see cref="IApplicationBuilder"/> reaches its component list and resolves an unrelated middleware: that is how
    /// <c>UseWhen</c> ends up named after a middleware inside its own branch, and how the single component that carries
    /// a whole <c>WebApplication</c> pipeline ends up named after the first middleware registered on it.
    /// </remarks>
    private static bool IsTraversalBoundary(object value)
        => value is IApplicationBuilder or IServiceProvider or IEndpointRouteBuilder or IDictionary
                 or EndpointDataSource or MiddlewarePipelineDescriptor or MiddlewareDescriptor;

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Middleware diagnostics inspects middleware invoke methods by reflection; an unresolved name degrades to the declaring type name.")]
    private static bool IsMiddlewareType(Type type)
    {
        // Every delegate declares Invoke, so the shape test below would accept any Type-valued field holding one.
        if (type == typeof(RequestDelegate) || typeof(Delegate).IsAssignableFrom(type))
            return false;

        if (typeof(IMiddleware).IsAssignableFrom(type))
            return true;

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name is not ("Invoke" or "InvokeAsync"))
                continue;

            if (!typeof(Task).IsAssignableFrom(method.ReturnType))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(HttpContext))
                return true;
        }

        return false;
    }

    private static bool ShouldInspectObject(Type type)
        => type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) is true;
}
