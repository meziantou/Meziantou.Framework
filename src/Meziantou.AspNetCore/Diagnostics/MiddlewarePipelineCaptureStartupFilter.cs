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

            var captureBuilder = new MiddlewarePipelineCaptureApplicationBuilder(app, _captureState.Root);
            next(captureBuilder);

            // Everything the application registers has been observed. Stop recording before the pipeline is built, so
            // build-time registrations are not reported, then publish an immutable tree for readers.
            captureBuilder.CloseRecording();
            _captureState.Publish();
        };
    }
}
