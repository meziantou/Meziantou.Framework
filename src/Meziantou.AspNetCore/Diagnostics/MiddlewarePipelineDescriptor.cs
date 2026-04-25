namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewarePipelineDescriptor
{
    public List<MiddlewareDescriptor> Middlewares { get; } = [];
}
