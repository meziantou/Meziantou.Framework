using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewarePipelineCaptureStartupFilter(MiddlewarePipelineCaptureState captureState) : IStartupFilter
{
    private readonly MiddlewarePipelineCaptureState _captureState = captureState;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            _captureState.Reset();
            next(new MiddlewarePipelineCaptureApplicationBuilder(app, _captureState.Root));
        };
    }
}
