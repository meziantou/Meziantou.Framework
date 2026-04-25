using System.Globalization;
using System.Text;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Represents a snapshot of the ASP.NET Core middleware pipeline and endpoint list.</summary>
public sealed record MiddlewarePipelineDebugSnapshot
{
    /// <summary>Gets the middleware pipeline tree.</summary>
    public required MiddlewarePipelineDebugPipeline Pipeline { get; init; }

    /// <summary>Gets the list of endpoints registered in the application.</summary>
    public required IReadOnlyList<MiddlewarePipelineDebugEndpoint> Endpoints { get; init; }

    /// <summary>Renders the middleware pipeline tree and endpoint list as text.</summary>
    /// <returns>A text representation of the snapshot.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Pipeline:");
        AppendPipeline(sb, Pipeline, indentationLevel: 1);
        sb.AppendLine();
        sb.AppendLine("Endpoints:");
        AppendEndpoints(sb, Endpoints);
        return sb.ToString();
    }

    private static void AppendPipeline(StringBuilder sb, MiddlewarePipelineDebugPipeline pipeline, int indentationLevel)
    {
        if (pipeline.Middlewares.Count == 0)
        {
            AppendIndentation(sb, indentationLevel);
            sb.AppendLine("(none)");
            return;
        }

        foreach (var middleware in pipeline.Middlewares)
        {
            AppendIndentation(sb, indentationLevel);
            sb.Append("- ");
            sb.Append(middleware.Name);
            sb.Append(" [");
            sb.Append(middleware.DelegateType);
            sb.Append("::");
            sb.Append(middleware.DelegateMethod);
            sb.AppendLine("]");

            for (var branchIndex = 0; branchIndex < middleware.Branches.Count; branchIndex++)
            {
                AppendIndentation(sb, indentationLevel + 1);
                sb.Append("Branch ");
                sb.Append(branchIndex + 1);
                sb.AppendLine(":");
                AppendPipeline(sb, middleware.Branches[branchIndex], indentationLevel + 2);
            }
        }
    }

    private static void AppendEndpoints(StringBuilder sb, IReadOnlyList<MiddlewarePipelineDebugEndpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            sb.AppendLine("  (none)");
            return;
        }

        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.HttpMethods.Count == 0 ? "*" : string.Join(",", endpoint.HttpMethods);
            var routePattern = endpoint.RoutePattern ?? "(no route pattern)";
            var displayName = endpoint.DisplayName ?? "(no display name)";
            var order = endpoint.Order?.ToString(CultureInfo.InvariantCulture) ?? "-";

            sb.Append("  - [");
            sb.Append(methods);
            sb.Append("] ");
            sb.Append(routePattern);
            sb.Append(" (Order: ");
            sb.Append(order);
            sb.Append(") ");
            sb.Append(displayName);
            sb.Append(" [");
            sb.Append(endpoint.EndpointType);
            sb.AppendLine("]");
        }
    }

    private static void AppendIndentation(StringBuilder sb, int indentationLevel)
    {
        _ = sb.Append(' ', indentationLevel * 2);
    }
}
